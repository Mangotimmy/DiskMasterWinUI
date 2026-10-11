using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// System Performance & Deep Process Hunter Service.
/// Implements typeperf hardware metrics, WMI/tasklist deep process enumeration,
/// ghost/orphan process detection, and resilient force-termination (taskkill /F /T + Win32 TerminateProcess).
/// </summary>
public partial class SystemPerformanceService
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_TERMINATE = 0x0001;

    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe",
        "winlogon.exe", "fontdrvhost.exe", "dwm.exe", "Registry", "Memory Compression"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public void Init()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ═════════════════════════════════════════════════════════════════════
    // 1. Real-Time Hardware Performance Metrics (GlobalMemoryStatusEx + typeperf)
    // ═════════════════════════════════════════════════════════════════════

    public async Task<SystemMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken ct = default)
    {
        var snap = new SystemMetricsSnapshot();

        // 1. Memory Load & Physical RAM via Win32 GlobalMemoryStatusEx (0ms latency, 100% accurate)
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.Init();
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                snap.RamUsagePercent = Math.Clamp(memStatus.dwMemoryLoad, 0, 100);
                snap.TotalRamMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                snap.AvailableRamMb = (long)(memStatus.ullAvailPhys / (1024 * 1024));
            }
        }
        catch { }

        // Fallback for RAM if Win32 API returned 0
        if (snap.TotalRamMb == 0)
        {
            try
            {
                var memInfo = GC.GetGCMemoryInfo();
                if (memInfo.TotalAvailableMemoryBytes > 0)
                {
                    snap.TotalRamMb = memInfo.TotalAvailableMemoryBytes / (1024 * 1024);
                }
            }
            catch { }
        }

        // 2. Query Physical Disk & GPU hardware metadata (cached)
        await EnsurePhysicalDiskCacheAsync();
        await EnsureGpuHardwareCacheAsync();
        snap.GpuModelName = _cachedGpuModelName;
        snap.GpuMemoryTotalMb = _cachedGpuTotalVramMb;

        // 3. CPU, Disk, and GPU Metrics via typeperf sample
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            const string typeperfCounters = @"""\Processor(_Total)\% Processor Time"" ""\PhysicalDisk(*)\% Disk Time"" ""\PhysicalDisk(*)\Avg. Disk sec/Read"" ""\PhysicalDisk(*)\Avg. Disk sec/Write"" ""\PhysicalDisk(*)\Disk Read Bytes/sec"" ""\PhysicalDisk(*)\Disk Write Bytes/sec"" ""\GPU Engine(*engtype_3D)\Utilization Percentage"" ""\GPU Adapter Memory(*)\Dedicated Usage"" -sc 1";
            var (outStr, _, code) = await ProcessHelper.RunProcessAsync("typeperf.exe", typeperfCounters, cancellationToken: timeoutCts.Token);
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var headerLine = lines.FirstOrDefault(l => l.Contains("PhysicalDisk") || l.Contains("Processor"));
                var dataLine = lines.LastOrDefault(l => l.Contains(','));

                if (headerLine != null && dataLine != null)
                {
                    var headers = ParseCsvLine(headerLine);
                    var values = ParseCsvLine(dataLine);

                    int count = Math.Min(headers.Length, values.Length);
                    var diskData = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                    double gpuUsageSum = 0;
                    double gpuMemBytesSum = 0;

                    for (int i = 0; i < count; i++)
                    {
                        string header = headers[i];
                        string valStr = values[i];

                        if (!double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            continue;
                        }

                        if (header.Contains("Processor(_Total)\\% Processor Time", StringComparison.OrdinalIgnoreCase))
                        {
                            snap.CpuUsagePercent = Math.Clamp(Math.Round(val, 1), 0.0, 100.0);
                            continue;
                        }

                        if (header.Contains("GPU Engine", StringComparison.OrdinalIgnoreCase) && header.Contains("Utilization Percentage", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuUsageSum += val;
                            continue;
                        }

                        if (header.Contains("GPU Adapter Memory", StringComparison.OrdinalIgnoreCase) && header.Contains("Dedicated Usage", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuMemBytesSum += val;
                            continue;
                        }

                        var match = Regex.Match(header, @"PhysicalDisk\((?<inst>[^)]+)\)\\(?<counter>[^""]+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string inst = match.Groups["inst"].Value.Trim();
                            string counter = match.Groups["counter"].Value.Trim();

                            if (!diskData.TryGetValue(inst, out var counters))
                            {
                                counters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                                diskData[inst] = counters;
                            }
                            counters[counter] = val;
                        }
                    }

                    snap.GpuUsagePercent = Math.Clamp(Math.Round(gpuUsageSum, 1), 0.0, 100.0);
                    if (gpuMemBytesSum > 0)
                    {
                        snap.GpuMemoryUsedMb = (long)(gpuMemBytesSum / (1024 * 1024));
                    }

                    // Process each disk instance
                    var diskItems = new List<DiskPerformanceItem>();
                    foreach (var (inst, counters) in diskData)
                    {
                        counters.TryGetValue("% Disk Time", out double diskPct);
                        counters.TryGetValue("Avg. Disk sec/Read", out double readSec);
                        counters.TryGetValue("Avg. Disk sec/Write", out double writeSec);
                        counters.TryGetValue("Disk Read Bytes/sec", out double readBytes);
                        counters.TryGetValue("Disk Write Bytes/sec", out double writeBytes);

                        if (inst.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                        {
                            snap.DiskUsagePercent = Math.Clamp(Math.Round(diskPct, 1), 0.0, 100.0);
                            snap.DiskReadLatencyMs = Math.Max(0, Math.Round(readSec * 1000.0, 2));
                            snap.DiskWriteLatencyMs = Math.Max(0, Math.Round(writeSec * 1000.0, 2));
                            snap.DiskReadRateDisplay = FormatBytesPerSec(readBytes);
                            snap.DiskWriteRateDisplay = FormatBytesPerSec(writeBytes);

                            var totalItem = new DiskPerformanceItem
                            {
                                DiskIndex = -1,
                                InstanceName = "_Total",
                                DisplayName = "全部磁碟總計 (_Total)",
                                ModelName = "All Physical Disks Combined",
                                MediaType = "彙總",
                                CapacityDisplay = "",
                                ActiveTimePercent = Math.Clamp(Math.Round(diskPct, 1), 0.0, 100.0),
                                ReadLatencyMs = Math.Max(0, Math.Round(readSec * 1000.0, 2)),
                                WriteLatencyMs = Math.Max(0, Math.Round(writeSec * 1000.0, 2)),
                                ReadRateDisplay = FormatBytesPerSec(readBytes),
                                WriteRateDisplay = FormatBytesPerSec(writeBytes)
                            };
                            diskItems.Add(totalItem);
                        }
                        else
                        {
                            // Parse index and letters from instance like "0 C:" or "1 D: E:" or "0"
                            int diskIdx = -1;
                            string letters = "";
                            var idxMatch = Regex.Match(inst, @"^(?<idx>\d+)(\s+(?<letters>.+))?$");
                            if (idxMatch.Success)
                            {
                                int.TryParse(idxMatch.Groups["idx"].Value, out diskIdx);
                                letters = idxMatch.Groups["letters"].Value.Trim();
                            }

                            string model = $"Disk {diskIdx}";
                            string mediaType = "SSD";
                            string capacity = "";

                            if (diskIdx >= 0 && _physicalDiskCache.TryGetValue(diskIdx, out var cachedInfo))
                            {
                                model = cachedInfo.Model;
                                mediaType = cachedInfo.MediaType;
                                capacity = cachedInfo.Capacity;
                            }

                            string display = !string.IsNullOrEmpty(letters)
                                ? $"磁碟 {diskIdx} ({letters})"
                                : $"磁碟 {diskIdx}";

                            var item = new DiskPerformanceItem
                            {
                                DiskIndex = diskIdx,
                                InstanceName = inst,
                                DisplayName = display,
                                ModelName = model,
                                MediaType = mediaType,
                                CapacityDisplay = capacity,
                                ActiveTimePercent = Math.Clamp(Math.Round(diskPct, 1), 0.0, 100.0),
                                ReadLatencyMs = Math.Max(0, Math.Round(readSec * 1000.0, 2)),
                                WriteLatencyMs = Math.Max(0, Math.Round(writeSec * 1000.0, 2)),
                                ReadRateDisplay = FormatBytesPerSec(readBytes),
                                WriteRateDisplay = FormatBytesPerSec(writeBytes)
                            };
                            diskItems.Add(item);
                        }
                    }

                    // Order by DiskIndex (0, 1, 2... and _Total at the end)
                    snap.Disks = diskItems.OrderBy(d => d.DiskIndex < 0 ? int.MaxValue : d.DiskIndex).ToList();
                }
            }
        }
        catch { }

        // 4. Sample Real-Time Network / LAN Adapter metrics (0ms latency, native Win32/C#)
        SampleNetworkMetrics(snap);

        return snap;
    }

    private readonly Dictionary<int, (string Model, string MediaType, string Capacity)> _physicalDiskCache = new();
    private DateTime _lastDiskCacheTime = DateTime.MinValue;

    private async Task EnsurePhysicalDiskCacheAsync()
    {
        if (DateTime.Now - _lastDiskCacheTime < TimeSpan.FromSeconds(60) && _physicalDiskCache.Count > 0) return;

        await Task.Run(() =>
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Index, Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");
                foreach (var obj in searcher.Get())
                {
                    int index = Convert.ToInt32(obj["Index"]);
                    string model = obj["Model"]?.ToString()?.Trim() ?? $"Disk {index}";
                    ulong size = Convert.ToUInt64(obj["Size"] ?? 0);
                    string mediaType = obj["MediaType"]?.ToString() ?? "";
                    string iface = obj["InterfaceType"]?.ToString() ?? "";

                    string typeStr = iface.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                        ? "NVMe SSD"
                        : mediaType.Contains("External", StringComparison.OrdinalIgnoreCase) || iface.Contains("USB", StringComparison.OrdinalIgnoreCase)
                            ? "外接磁碟 (USB)"
                            : mediaType.Contains("Fixed", StringComparison.OrdinalIgnoreCase)
                                ? "SSD / 內建"
                                : "硬碟";

                    string capStr = size > 0
                        ? size >= 1_000_000_000_000UL
                            ? $"{size / 1_000_000_000_000.0:F1} TB ({size / (1024 * 1024 * 1024):N0} GB)"
                            : $"{size / (1000 * 1000 * 1000):N0} GB"
                        : "";

                    _physicalDiskCache[index] = (model, typeStr, capStr);
                }
                _lastDiskCacheTime = DateTime.Now;
            }
            catch { }
        });
    }

    private string _cachedGpuModelName = "GPU";
    private long _cachedGpuTotalVramMb = 0;
    private DateTime _lastGpuCacheTime = DateTime.MinValue;

    private async Task EnsureGpuHardwareCacheAsync()
    {
        if (DateTime.Now - _lastGpuCacheTime < TimeSpan.FromMinutes(5) && _cachedGpuTotalVramMb > 0) return;

        await Task.Run(() =>
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        _cachedGpuModelName = name;
                        if (obj["AdapterRAM"] != null && ulong.TryParse(obj["AdapterRAM"]?.ToString(), out ulong ramBytes) && ramBytes > 0)
                        {
                            _cachedGpuTotalVramMb = (long)(ramBytes / (1024 * 1024));
                        }
                        break;
                    }
                }
                _lastGpuCacheTime = DateTime.Now;
            }
            catch { }
        });
    }

    private DateTime _lastNetSampleTime = DateTime.MinValue;
    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;

    private void SampleNetworkMetrics(SystemMetricsSnapshot snap)
    {
        try
        {
            var now = DateTime.UtcNow;
            long currentBytesRecv = 0;
            long currentBytesSent = 0;
            string bestAdapterName = "乙太網路 / Wi-Fi";
            string bestAdapterSpeed = "連線中";
            long maxSpeed = 0;

            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                    ni.Description.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("Pseudo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var stats = ni.GetIPStatistics();
                    currentBytesRecv += stats.BytesReceived;
                    currentBytesSent += stats.BytesSent;

                    if (ni.Speed > maxSpeed)
                    {
                        maxSpeed = ni.Speed;
                        bestAdapterName = string.IsNullOrWhiteSpace(ni.Description) ? ni.Name : ni.Description;
                        bestAdapterSpeed = ni.Speed switch
                        {
                            >= 10_000_000_000 => "10 Gbps",
                            >= 2_500_000_000 => "2.5 Gbps",
                            >= 1_000_000_000 => "1.0 Gbps",
                            >= 100_000_000 => "100 Mbps",
                            > 0 => $"{ni.Speed / 1_000_000} Mbps",
                            _ => "連線中"
                        };
                    }
                }
                catch { }
            }

            snap.NetAdapterName = bestAdapterName;
            snap.NetAdapterSpeedDisplay = bestAdapterSpeed;

            if (_lastNetSampleTime != DateTime.MinValue && now > _lastNetSampleTime)
            {
                var elapsedSec = (now - _lastNetSampleTime).TotalSeconds;
                if (elapsedSec > 0.1)
                {
                    var deltaRecv = Math.Max(0, currentBytesRecv - _lastBytesReceived);
                    var deltaSent = Math.Max(0, currentBytesSent - _lastBytesSent);

                    snap.NetDownloadSpeedBps = deltaRecv / elapsedSec;
                    snap.NetUploadSpeedBps = deltaSent / elapsedSec;
                    snap.NetDownloadRateDisplay = FormatBytesPerSec(snap.NetDownloadSpeedBps);
                    snap.NetUploadRateDisplay = FormatBytesPerSec(snap.NetUploadSpeedBps);
                    snap.NetThroughputDisplay = $"⬇️ {snap.NetDownloadRateDisplay}  ⬆️ {snap.NetUploadRateDisplay}";
                }
            }
            else
            {
                snap.NetDownloadRateDisplay = "0 KB/s";
                snap.NetUploadRateDisplay = "0 KB/s";
                snap.NetThroughputDisplay = "0 KB/s";
            }

            _lastNetSampleTime = now;
            _lastBytesReceived = currentBytesRecv;
            _lastBytesSent = currentBytesSent;
        }
        catch { }
    }

    private static string[] ParseCsvLine(string line)
    {
        var list = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                list.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        list.Add(current.ToString().Trim());
        return list.ToArray();
    }

    private static string FormatBytesPerSec(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 KB/s";
        if (bytesPerSec >= 1024 * 1024 * 1024) return $"{bytesPerSec / (1024 * 1024 * 1024):F1} GB/s";
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSec / 1024:F0} KB/s";
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. Deep Process Hunter (tasklist + wmic/CIM commandlines + Parent PID)
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<ProcessHunterItem>> GetRunningProcessesDeepAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var list = new List<ProcessHunterItem>();
            var activePids = new HashSet<int>();

            // 1. Get process list from .NET Process API
            var procs = Process.GetProcesses();
            var dict = new Dictionary<int, Process>();
            foreach (var p in procs)
            {
                dict[p.Id] = p;
                activePids.Add(p.Id);
            }

            // 2. Fetch ParentProcessId and CommandLine via WMIC
            var parentPidMap = new Dictionary<int, int>();
            var commandLineMap = new Dictionary<int, string>();
            var exePathMap = new Dictionary<int, string>();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                var (wmicOut, _, code) = await ProcessHelper.RunProcessAsync("wmic.exe", "process get ProcessId,ParentProcessId,CommandLine,ExecutablePath /format:csv", cancellationToken: timeoutCts.Token);
                if (code == 0 && !string.IsNullOrWhiteSpace(wmicOut))
                {
                    var lines = wmicOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',');
                        // Format: Node, CommandLine, ExecutablePath, ParentProcessId, ProcessId
                        if (parts.Length >= 5)
                        {
                            if (int.TryParse(parts[^1].Trim(), out int pid))
                            {
                                if (int.TryParse(parts[^2].Trim(), out int ppid))
                                {
                                    parentPidMap[pid] = ppid;
                                }
                                var exe = parts[^3].Trim();
                                if (!string.IsNullOrEmpty(exe)) exePathMap[pid] = exe;

                                var cmd = string.Join(",", parts.Skip(1).Take(parts.Length - 4)).Trim();
                                if (!string.IsNullOrEmpty(cmd)) commandLineMap[pid] = cmd;
                            }
                        }
                    }
                }
            }
            catch { }

            foreach (var p in procs)
            {
                if (ct.IsCancellationRequested) break;

                int pid = p.Id;
                string name = p.ProcessName;
                parentPidMap.TryGetValue(pid, out int ppid);
                commandLineMap.TryGetValue(pid, out string? cmd);
                exePathMap.TryGetValue(pid, out string? exePath);

                long mem = 0;
                bool responding = true;
                try
                {
                    mem = p.WorkingSet64;
                    responding = p.Responding;
                    if (string.IsNullOrEmpty(exePath)) exePath = p.MainModule?.FileName ?? "";
                }
                catch { }

                bool isOrphan = (ppid > 0 && !activePids.Contains(ppid) && pid != 4 && pid != 0);
                bool isProtected = ProtectedProcesses.Contains(name) || ProtectedProcesses.Contains(name + ".exe") || pid <= 4;

                string badge = "Normal";
                if (!responding) badge = "Not Responding";
                else if (isOrphan) badge = "Ghost/Orphan";
                else if (isProtected) badge = "Protected";

                list.Add(new ProcessHunterItem
                {
                    Pid = pid,
                    ParentPid = ppid,
                    ProcessName = name,
                    ExecutablePath = exePath ?? "",
                    CommandLine = cmd ?? "",
                    MemoryBytes = mem,
                    DisplayMemory = TopLargeFileItem.FormatBytes(mem),
                    IsResponding = responding,
                    IsOrphan = isOrphan,
                    IsSystemProtected = isProtected,
                    StatusBadge = badge
                });
            }

            return list.OrderByDescending(x => x.MemoryBytes).ToList();
        }, ct);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. Resilient Force Termination (taskkill /F /T + Win32 TerminateProcess)
    // ═════════════════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> KillProcessTreeAsync(int pid)
    {
        if (pid <= 4) return (false, "Cannot terminate critical system process (PID <= 4).");

        // 1. First rail: taskkill /F /PID <pid> /T
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("taskkill.exe", $"/F /PID {pid} /T");
        if (code == 0)
        {
            return (true, $"Process tree for PID {pid} successfully terminated.");
        }

        // 2. Second rail: Win32 TerminateProcess direct API fallback
        IntPtr hProc = OpenProcess(PROCESS_TERMINATE, false, pid);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                bool terminated = TerminateProcess(hProc, 1);
                if (terminated)
                {
                    return (true, $"Terminated PID {pid} via direct Win32 TerminateProcess.");
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        return (false, string.IsNullOrWhiteSpace(errStr) ? outStr : errStr);
    }

    public async Task<(bool Success, string Message)> KillAllNotRespondingAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("taskkill.exe", "/F /FI \"STATUS eq NOT RESPONDING\"");
        return (code == 0, code == 0 ? "All unresponsive processes terminated." : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. SystemInfo Hardware Profile & Installed Hotfixes Viewer
    // ═════════════════════════════════════════════════════════════════════

    public async Task<SystemInfoSummary> GetSystemInfoSummaryAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var summary = new SystemInfoSummary();
            try
            {
                // 1. Host Name via Environment
                summary.HostName = Environment.MachineName;

                // 2. OS Name & Build via Registry
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (key != null)
                    {
                        var prod = key.GetValue("ProductName")?.ToString() ?? "";
                        var displayVer = key.GetValue("DisplayVersion")?.ToString() ?? "";
                        var build = key.GetValue("CurrentBuild")?.ToString() ?? "";
                        if (int.TryParse(build, out int bNum) && bNum >= 22000 && prod.StartsWith("Windows 10"))
                        {
                            prod = "Windows 11" + prod.Substring("Windows 10".Length);
                        }
                        summary.OsName = string.IsNullOrEmpty(displayVer) ? $"{prod} (Build {build})" : $"{prod} {displayVer} (Build {build})";
                        summary.OsVersion = string.IsNullOrEmpty(build) ? "10.0" : $"10.0.{build}";
                    }
                }
                catch
                {
                    summary.OsName = Environment.OSVersion.VersionString;
                }

                // 3. Motherboard Manufacturer, Model & BIOS Version via Registry
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                    if (key != null)
                    {
                        summary.SystemManufacturer = key.GetValue("SystemManufacturer")?.ToString() ?? "Standard PC";
                        summary.SystemModel = key.GetValue("SystemProductName")?.ToString() ?? "";
                        summary.BiosVersion = key.GetValue("BIOSVersion")?.ToString() ?? "";
                    }
                }
                catch { }

                // 4. Boot Time & Uptime
                var bootDate = DateTime.Now.AddMilliseconds(-Environment.TickCount64);
                summary.SystemBootTime = bootDate.ToString("yyyy/MM/dd HH:mm:ss");
                var uptimeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64);
                summary.Uptime = $"{(int)uptimeSpan.TotalHours} 小時 {uptimeSpan.Minutes} 分鐘";

                // 5. Physical RAM via Win32 GlobalMemoryStatusEx
                try
                {
                    var memStatus = new MEMORYSTATUSEX();
                    memStatus.Init();
                    if (GlobalMemoryStatusEx(ref memStatus))
                    {
                        double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024 * 1024);
                        double availGb = memStatus.ullAvailPhys / (1024.0 * 1024 * 1024);
                        summary.TotalPhysicalMemory = $"{totalGb:F1} GB ({memStatus.ullTotalPhys / (1024 * 1024):N0} MB)";
                        summary.AvailablePhysicalMemory = $"{availGb:F1} GB ({memStatus.ullAvailPhys / (1024 * 1024):N0} MB)";
                    }
                }
                catch { }

                // 6. Windows Hotfixes via WMI Win32_QuickFixEngineering (language-independent)
                var hotfixes = new List<string>();
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher("SELECT HotFixID FROM Win32_QuickFixEngineering");
                    using var col = searcher.Get();
                    foreach (var obj in col)
                    {
                        var id = obj["HotFixID"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(id) && id.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
                        {
                            hotfixes.Add(id);
                        }
                    }
                }
                catch { }

                summary.Hotfixes = hotfixes;
                summary.HotfixCount = hotfixes.Count;
            }
            catch { }
            return summary;
        }, ct);
    }
}
