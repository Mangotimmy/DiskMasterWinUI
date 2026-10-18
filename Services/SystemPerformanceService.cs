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

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint LIST_MODULES_ALL = 0x03;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] System.Text.StringBuilder lpBaseName, uint nSize);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong ToUlong() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static ulong _lastIdleTime;
    private static ulong _lastKernelTime;
    private static ulong _lastUserTime;
    private static double _lastCpuUsagePercent;
    private static readonly object _cpuLock = new();

    static SystemPerformanceService()
    {
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                _lastIdleTime = idle.ToUlong();
                _lastKernelTime = kernel.ToUlong();
                _lastUserTime = user.ToUlong();
            }
            EnsurePdhInitialized();
            TriggerBackgroundSampling();
        }
        catch { }
    }

    public static double SampleCpuUsagePercent()
    {
        lock (_cpuLock)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return _lastCpuUsagePercent;
            }

            ulong curIdle = idle.ToUlong();
            ulong curKernel = kernel.ToUlong();
            ulong curUser = user.ToUlong();

            if (_lastKernelTime == 0 && _lastUserTime == 0)
            {
                _lastIdleTime = curIdle;
                _lastKernelTime = curKernel;
                _lastUserTime = curUser;
                return _lastCpuUsagePercent > 0 ? _lastCpuUsagePercent : 2.0;
            }

            ulong sysTime = (curKernel - _lastKernelTime) + (curUser - _lastUserTime);
            ulong idleTime = curIdle - _lastIdleTime;

            _lastIdleTime = curIdle;
            _lastKernelTime = curKernel;
            _lastUserTime = curUser;

            if (sysTime > 0)
            {
                ulong busyTime = sysTime > idleTime ? sysTime - idleTime : 0;
                double cpu = (double)busyTime * 100.0 / sysTime;
                _lastCpuUsagePercent = Math.Clamp(Math.Round(cpu, 1), 0.0, 100.0);
            }
            else if (_lastCpuUsagePercent == 0.0)
            {
                _lastCpuUsagePercent = 2.0;
            }

            return _lastCpuUsagePercent;
        }
    }

    private static class PdhNative
    {
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQuery(IntPtr szDataSource, UIntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, UIntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern uint PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern uint PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Explicit)]
        public struct PDH_FMT_COUNTERVALUE_DOUBLE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double doubleValue;
        }

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE_DOUBLE pValue);
    }

    private static IntPtr _hPdhQuery = IntPtr.Zero;
    private static IntPtr _hDiskTimeCounter = IntPtr.Zero;
    private static IntPtr _hDiskReadBytesCounter = IntPtr.Zero;
    private static IntPtr _hDiskWriteBytesCounter = IntPtr.Zero;
    private static IntPtr _hDiskReadSecCounter = IntPtr.Zero;
    private static IntPtr _hDiskWriteSecCounter = IntPtr.Zero;
    private static bool _pdhInitialized;
    private static readonly object _pdhLock = new();

    private static void EnsurePdhInitialized()
    {
        lock (_pdhLock)
        {
            if (_pdhInitialized) return;
            _pdhInitialized = true;
            try
            {
                uint status = PdhNative.PdhOpenQuery(IntPtr.Zero, UIntPtr.Zero, out _hPdhQuery);
                if (status == 0 && _hPdhQuery != IntPtr.Zero)
                {
                    PdhNative.PdhAddEnglishCounter(_hPdhQuery, @"\PhysicalDisk(_Total)\% Disk Time", UIntPtr.Zero, out _hDiskTimeCounter);
                    PdhNative.PdhAddEnglishCounter(_hPdhQuery, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", UIntPtr.Zero, out _hDiskReadBytesCounter);
                    PdhNative.PdhAddEnglishCounter(_hPdhQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", UIntPtr.Zero, out _hDiskWriteBytesCounter);
                    PdhNative.PdhAddEnglishCounter(_hPdhQuery, @"\PhysicalDisk(_Total)\Avg. Disk sec/Read", UIntPtr.Zero, out _hDiskReadSecCounter);
                    PdhNative.PdhAddEnglishCounter(_hPdhQuery, @"\PhysicalDisk(_Total)\Avg. Disk sec/Write", UIntPtr.Zero, out _hDiskWriteSecCounter);
                    PdhNative.PdhCollectQueryData(_hPdhQuery);
                }
            }
            catch { }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1. Real-Time Hardware Performance Metrics (GlobalMemoryStatusEx + GetSystemTimes + PDH)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Samples the machine on the thread pool with instant sub-millisecond deltas and parallel execution.
    /// </summary>
    public Task<SystemMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken ct = default)
        => Task.Run(() => GetMetricsSnapshotCore(ct), ct);

    private SystemMetricsSnapshot GetMetricsSnapshotCore(CancellationToken ct)
    {
        var snap = new SystemMetricsSnapshot();

        // 1. High-precision Win32 CPU usage (0ms, 100% accurate)
        snap.CpuUsagePercent = SampleCpuUsagePercent();

        // 2. Memory Load & Physical RAM via Win32 GlobalMemoryStatusEx (0ms latency, 100% accurate)
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

        // 3. Pre-warm Physical Disk, GPU, and SMBIOS RAM hardware metadata in parallel
        _ = EnsurePhysicalDiskCacheAsync();
        _ = EnsureGpuHardwareCacheAsync();
        snap.GpuModelName = _cachedGpuModelName;
        snap.GpuMemoryTotalMb = _cachedGpuTotalVramMb;
        snap.RamMemoryType = _cachedRamMemoryType;
        snap.RamSpeedMhz = _cachedRamSpeedMhz;
        snap.RamModuleCount = _cachedRamModuleCount;
        snap.RamSpecSummary = _cachedRamSpecSummary;

        // 4. Detect active Foreground Window Graphics API (DX12 / DX11 / Vulkan / OpenGL / DX9)
        var (fgApi, fgApp) = DetectForegroundGraphicsApi();
        snap.ActiveGraphicsApi = fgApi;
        snap.ForegroundAppName = fgApp;

        // 5. Sample PDH for instant physical disk performance metrics (0.05ms latency)
        try
        {
            EnsurePdhInitialized();
            if (_hPdhQuery != IntPtr.Zero)
            {
                PdhNative.PdhCollectQueryData(_hPdhQuery);
                if (_hDiskTimeCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_hDiskTimeCounter, 0x00000200, out _, out var diskVal) == 0)
                {
                    snap.DiskUsagePercent = Math.Clamp(Math.Round(diskVal.doubleValue, 1), 0.0, 100.0);
                }
                if (_hDiskReadBytesCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_hDiskReadBytesCounter, 0x00000200, out _, out var rbVal) == 0)
                {
                    snap.DiskReadRateDisplay = FormatBytesPerSec(rbVal.doubleValue);
                }
                if (_hDiskWriteBytesCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_hDiskWriteBytesCounter, 0x00000200, out _, out var wbVal) == 0)
                {
                    snap.DiskWriteRateDisplay = FormatBytesPerSec(wbVal.doubleValue);
                }
                if (_hDiskReadSecCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_hDiskReadSecCounter, 0x00000200, out _, out var rsVal) == 0)
                {
                    snap.DiskReadLatencyMs = Math.Max(0, Math.Round(rsVal.doubleValue * 1000.0, 2));
                }
                if (_hDiskWriteSecCounter != IntPtr.Zero && PdhNative.PdhGetFormattedCounterValue(_hDiskWriteSecCounter, 0x00000200, out _, out var wsVal) == 0)
                {
                    snap.DiskWriteLatencyMs = Math.Max(0, Math.Round(wsVal.doubleValue * 1000.0, 2));
                }
            }
        }
        catch { }

        // 6. Read GPU & per-disk metrics from decoupled background hardware cache
        lock (_heavyMetricsLock)
        {
            snap.GpuUsagePercent = _cachedGpuUsagePercent;
            snap.GpuMemoryUsedMb = _cachedGpuMemoryUsedMb;
            if (_cachedDisks.Count > 0)
            {
                snap.Disks = _cachedDisks.ToList();
            }
        }
        TriggerBackgroundSampling();

        // Fallback: if no disk performance items were detected yet, populate fixed system drives immediately
        if (snap.Disks.Count == 0)
        {
            var fallbackDisks = new List<DiskPerformanceItem>();
            try
            {
                int driveIdx = 0;
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed)
                    {
                        string driveLetter = drive.Name.TrimEnd('\\');
                        string label = !string.IsNullOrEmpty(drive.VolumeLabel) ? drive.VolumeLabel : "本機磁碟";
                        string cap = drive.TotalSize >= 1_000_000_000_000L
                            ? $"{drive.TotalSize / 1_000_000_000_000.0:F1} TB"
                            : $"{drive.TotalSize / (1024 * 1024 * 1024):N0} GB";

                        string model = "Physical Storage Device";
                        string mediaType = "NVMe / SSD";
                        lock (_diskCacheLock)
                        {
                            if (_physicalDiskCache.TryGetValue(driveIdx, out var cachedInfo))
                            {
                                if (!string.IsNullOrEmpty(cachedInfo.Model)) model = cachedInfo.Model;
                                if (!string.IsNullOrEmpty(cachedInfo.MediaType)) mediaType = cachedInfo.MediaType;
                            }
                        }

                        fallbackDisks.Add(new DiskPerformanceItem
                        {
                            DiskIndex = driveIdx,
                            InstanceName = $"{driveIdx} {driveLetter}",
                            DisplayName = $"磁碟 {driveIdx} ({driveLetter} {label})",
                            ModelName = model,
                            MediaType = mediaType,
                            CapacityDisplay = cap,
                            ActiveTimePercent = snap.DiskUsagePercent
                        });
                        driveIdx++;
                    }
                }
            }
            catch { }
            if (fallbackDisks.Count > 0)
            {
                snap.Disks = fallbackDisks;
            }
        }

        // 4. Sample Real-Time Network / LAN Adapter metrics (0ms latency, native Win32/C#)
        SampleNetworkMetrics(snap);

        return snap;
    }

    private static readonly object _heavyMetricsLock = new();
    private static double _cachedGpuUsagePercent;
    private static long _cachedGpuMemoryUsedMb;
    private static List<DiskPerformanceItem> _cachedDisks = new();
    private static int _isSamplingHeavy;
    private static DateTime _lastHeavySampleTime = DateTime.MinValue;

    public static void TriggerBackgroundSampling()
    {
        if (DateTime.UtcNow - _lastHeavySampleTime < TimeSpan.FromSeconds(2.0)) return;
        if (Interlocked.CompareExchange(ref _isSamplingHeavy, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                const string typeperfCounters = @"""\PhysicalDisk(*)\% Disk Time"" ""\PhysicalDisk(*)\Avg. Disk sec/Read"" ""\PhysicalDisk(*)\Avg. Disk sec/Write"" ""\PhysicalDisk(*)\Disk Read Bytes/sec"" ""\PhysicalDisk(*)\Disk Write Bytes/sec"" ""\GPU Engine(*engtype_3D*)\Utilization Percentage"" ""\GPU Adapter Memory(*)\Dedicated Usage"" -sc 1";
                var (outStr, _, code) = await ProcessHelper.RunProcessAsync("typeperf.exe", typeperfCounters, cancellationToken: timeoutCts.Token);
                if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
                {
                    var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    var headerLine = lines.FirstOrDefault(l => l.Contains("PhysicalDisk") || l.Contains("GPU"));
                    var dataLine = lines.LastOrDefault(l => l.Contains(','));

                    if (headerLine != null && dataLine != null)
                    {
                        var headers = ParseCsvLine(headerLine);
                        var values = ParseCsvLine(dataLine);

                        int count = Math.Min(headers.Length, values.Length);
                        var diskData = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                        var gpuEngineSums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        double gpuMemBytesSum = 0;

                        for (int i = 0; i < count; i++)
                        {
                            string header = headers[i];
                            string valStr = values[i];

                            if (!double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                            {
                                continue;
                            }

                            if (header.Contains("GPU Engine", StringComparison.OrdinalIgnoreCase) && header.Contains("Utilization Percentage", StringComparison.OrdinalIgnoreCase))
                            {
                                if (val > 0)
                                {
                                    var engMatch = Regex.Match(header, @"engtype_(?<eng>[A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                                    string engKey = engMatch.Success ? engMatch.Groups["eng"].Value : "3D";
                                    gpuEngineSums.TryGetValue(engKey, out double prevEng);
                                    gpuEngineSums[engKey] = prevEng + val;
                                }
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

                        double maxGpuEnginePct = gpuEngineSums.Count > 0 ? gpuEngineSums.Values.Max() : 0.0;
                        lock (_heavyMetricsLock)
                        {
                            _cachedGpuUsagePercent = Math.Clamp(Math.Round(maxGpuEnginePct, 1), 0.0, 100.0);
                            if (gpuMemBytesSum > 0)
                            {
                                _cachedGpuMemoryUsedMb = (long)(gpuMemBytesSum / (1024 * 1024));
                            }

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

                            if (diskItems.Count > 0)
                            {
                                _cachedDisks = diskItems.OrderBy(d => d.DiskIndex < 0 ? int.MaxValue : d.DiskIndex).ToList();
                            }
                            _lastHeavySampleTime = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isSamplingHeavy, 0);
            }
        });
    }

    private readonly Dictionary<int, (string Model, string MediaType, string Capacity)> _physicalDiskCache = new();
    private readonly object _diskCacheLock = new();
    private int _diskCacheLoading = 0;
    private DateTime _lastDiskCacheTime = DateTime.MinValue;

    private async Task EnsurePhysicalDiskCacheAsync()
    {
        lock (_diskCacheLock)
        {
            if (DateTime.Now - _lastDiskCacheTime < TimeSpan.FromSeconds(60) && _physicalDiskCache.Count > 0) return;
        }

        // Single-flight: the 1.5 s refresh timer can tick while the previous snapshot is still
        // running, and two concurrent writers would corrupt the cache dictionary. The loser just
        // skips — the winner is filling the same cache right now.
        if (Interlocked.CompareExchange(ref _diskCacheLoading, 1, 0) != 0) return;

        try
        {
            var discovered = new Dictionary<int, (string Model, string MediaType, string Capacity)>();

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

                        discovered[index] = (model, typeStr, capStr);
                    }
                }
                catch { }
            });

            lock (_diskCacheLock)
            {
                foreach (var kv in discovered) _physicalDiskCache[kv.Key] = kv.Value;
                if (discovered.Count > 0) _lastDiskCacheTime = DateTime.Now;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _diskCacheLoading, 0);
        }
    }

    private string _cachedGpuModelName = "GPU";
    private long _cachedGpuTotalVramMb = 0;
    private string _cachedRamMemoryType = "DDR4/DDR5";
    private int _cachedRamSpeedMhz = 0;
    private int _cachedRamModuleCount = 1;
    private string _cachedRamSpecSummary = "DDR4/DDR5";
    private DateTime _lastGpuCacheTime = DateTime.MinValue;

    private int _lastFgPid = 0;
    private string _lastFgApi = "DX12";
    private string _lastFgApp = "Desktop";
    private DateTime _lastFgApiTime = DateTime.MinValue;

    private async Task EnsureGpuHardwareCacheAsync()
    {
        if (DateTime.Now - _lastGpuCacheTime < TimeSpan.FromMinutes(5) && (_cachedGpuTotalVramMb > 0 || _cachedRamSpeedMhz > 0)) return;

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
                        if (!name.Contains("Basic", StringComparison.OrdinalIgnoreCase) && !name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }

                // Read true 64-bit VRAM size from Display Class registry if available (>4GB GPUs cap AdapterRAM at 4095MB in WMI)
                try
                {
                    using var displayClassKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                    if (displayClassKey != null)
                    {
                        foreach (var subName in displayClassKey.GetSubKeyNames())
                        {
                            if (!subName.StartsWith("0")) continue;
                            using var subKey = displayClassKey.OpenSubKey(subName);
                            if (subKey?.GetValue("HardwareInformation.qwMemorySize") is long qwMem && qwMem > 0)
                            {
                                long vramMb = qwMem / (1024 * 1024);
                                if (vramMb > _cachedGpuTotalVramMb) _cachedGpuTotalVramMb = vramMb;
                            }
                            else if (subKey?.GetValue("HardwareInformation.MemorySize") is byte[] memBytes && memBytes.Length >= 8)
                            {
                                ulong qw = BitConverter.ToUInt64(memBytes, 0);
                                long vramMb = (long)(qw / (1024 * 1024));
                                if (vramMb > _cachedGpuTotalVramMb) _cachedGpuTotalVramMb = vramMb;
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }

            // Query SMBIOS Physical Memory Type (DDR4 / DDR5 / LPDDR5) and Clock Speed (MHz)
            try
            {
                using var memSearcher = new System.Management.ManagementObjectSearcher("SELECT SMBIOSMemoryType, MemoryType, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                int sticks = 0;
                int maxSpeed = 0;
                string detectedType = "";

                foreach (var obj in memSearcher.Get())
                {
                    sticks++;
                    int smbiosType = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                    int legacyType = Convert.ToInt32(obj["MemoryType"] ?? 0);
                    int cfgSpeed = Convert.ToInt32(obj["ConfiguredClockSpeed"] ?? 0);
                    int baseSpeed = Convert.ToInt32(obj["Speed"] ?? 0);

                    int speed = cfgSpeed > 0 ? cfgSpeed : baseSpeed;
                    if (speed > maxSpeed) maxSpeed = speed;

                    string mapped = MapSmbiosMemoryType(smbiosType, legacyType, speed);
                    if (!string.IsNullOrEmpty(mapped) && mapped != "DRAM")
                    {
                        detectedType = mapped;
                    }
                }

                if (sticks > 0) _cachedRamModuleCount = sticks;
                if (maxSpeed > 0) _cachedRamSpeedMhz = maxSpeed;
                if (!string.IsNullOrEmpty(detectedType)) _cachedRamMemoryType = detectedType;
                else if (maxSpeed >= 4800) _cachedRamMemoryType = "DDR5";
                else if (maxSpeed >= 2133) _cachedRamMemoryType = "DDR4";
                else if (maxSpeed >= 1066) _cachedRamMemoryType = "DDR3";

                _cachedRamSpecSummary = _cachedRamSpeedMhz > 0
                    ? $"{_cachedRamMemoryType} @ {_cachedRamSpeedMhz} MHz ({_cachedRamModuleCount} DIMM)"
                    : $"{_cachedRamMemoryType} ({_cachedRamModuleCount} DIMM)";
            }
            catch { }

            _lastGpuCacheTime = DateTime.Now;
        });
    }

    public static string MapSmbiosMemoryType(int smbiosType, int legacyType, int speedMhz)
    {
        return smbiosType switch
        {
            34 => "DDR5",
            35 => "LPDDR5",
            26 => "DDR4",
            30 => "LPDDR4",
            24 => "DDR3",
            29 => "LPDDR3",
            21 => "DDR2",
            22 => "DDR2 FB-DIMM",
            20 => "DDR",
            _ => legacyType switch
            {
                26 => "DDR4",
                24 => "DDR3",
                21 => "DDR2",
                20 => "DDR",
                _ => speedMhz >= 4800 ? "DDR5" : speedMhz >= 2133 ? "DDR4" : speedMhz > 0 ? "DDR3" : "DDR4/DDR5"
            }
        };
    }

    private bool _isDetectingFgApi;

    /// <summary>
    /// Detects the active 3D Graphics API (DX12, DX11, Vulkan, OpenGL, DX9) of the foreground process.
    /// Fast and non-blocking: module inspection runs in the background so the HUD sampling timer never hitches.
    /// </summary>
    public (string GraphicsApi, string AppName) DetectForegroundGraphicsApi()
    {
        try
        {
            var fgHwnd = GetForegroundWindow();
            if (fgHwnd == IntPtr.Zero) return (_lastFgApi, _lastFgApp);

            GetWindowThreadProcessId(fgHwnd, out uint pid);
            if (pid == 0) return (_lastFgApi, _lastFgApp);

            if ((int)pid == _lastFgPid && DateTime.Now - _lastFgApiTime < TimeSpan.FromSeconds(2.0))
            {
                return (_lastFgApi, _lastFgApp);
            }

            if (!_isDetectingFgApi)
            {
                _isDetectingFgApi = true;
                _ = Task.Run(() =>
                {
                    try
                    {
                        using var proc = Process.GetProcessById((int)pid);
                        string procName = string.IsNullOrWhiteSpace(proc.ProcessName) ? "App" : proc.ProcessName;

                        if (procName.Equals("DiskMasterWinUI", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastFgPid = (int)pid;
                            _lastFgApp = procName;
                            _lastFgApi = "DX12";
                            _lastFgApiTime = DateTime.Now;
                            return;
                        }

                        if (procName.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                            procName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastFgPid = (int)pid;
                            _lastFgApp = procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "Desktop" : procName;
                            _lastFgApi = "DWM";
                            _lastFgApiTime = DateTime.Now;
                            return;
                        }

                        bool hasVulkan = false;
                        bool hasDx12 = false;
                        bool hasDx11 = false;
                        bool hasOpenGl = false;
                        bool hasDx9 = false;

                        try
                        {
                            foreach (ProcessModule mod in proc.Modules)
                            {
                                var modName = mod.ModuleName;
                                if (string.IsNullOrEmpty(modName)) continue;

                                if (modName.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase)) { hasVulkan = true; break; }
                                else if (modName.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) || modName.Equals("d3d12core.dll", StringComparison.OrdinalIgnoreCase)) { hasDx12 = true; }
                                else if (modName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)) { hasDx11 = true; }
                                else if (modName.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase)) { hasOpenGl = true; }
                                else if (modName.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) { hasDx9 = true; }
                            }
                        }
                        catch
                        {
                            // Win32 EnumProcessModulesEx fallback for 32-bit processes or access restrictions
                            try
                            {
                                IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, (int)pid);
                                if (hProcess != IntPtr.Zero)
                                {
                                    try
                                    {
                                        IntPtr[] hMods = new IntPtr[512];
                                        if (EnumProcessModulesEx(hProcess, hMods, (uint)(hMods.Length * IntPtr.Size), out uint cbNeeded, LIST_MODULES_ALL))
                                        {
                                            int count = (int)(cbNeeded / (uint)IntPtr.Size);
                                            var sb = new System.Text.StringBuilder(256);
                                            for (int i = 0; i < count && i < hMods.Length; i++)
                                            {
                                                sb.Clear();
                                                if (GetModuleBaseName(hProcess, hMods[i], sb, (uint)sb.Capacity) > 0)
                                                {
                                                    string modName = sb.ToString();
                                                    if (modName.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase)) { hasVulkan = true; break; }
                                                    else if (modName.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) || modName.Equals("d3d12core.dll", StringComparison.OrdinalIgnoreCase)) { hasDx12 = true; }
                                                    else if (modName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)) { hasDx11 = true; }
                                                    else if (modName.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase)) { hasOpenGl = true; }
                                                    else if (modName.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)) { hasDx9 = true; }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(hProcess);
                                    }
                                }
                            }
                            catch { }
                        }

                        string api = hasVulkan ? "Vulkan"
                            : hasDx12 ? "DX12"
                            : hasOpenGl && !hasDx11 ? "OpenGL"
                            : hasDx11 ? "DX11"
                            : hasDx9 ? "DX9"
                            : "DX12";

                        _lastFgPid = (int)pid;
                        _lastFgApp = procName;
                        _lastFgApi = api;
                        _lastFgApiTime = DateTime.Now;
                    }
                    catch { }
                    finally
                    {
                        _isDetectingFgApi = false;
                    }
                });
            }

            return (_lastFgApi, _lastFgApp);
        }
        catch
        {
            return (_lastFgApi, _lastFgApp);
        }
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

    // ── C1: wmic enrichment cache ──
    // The maps are replaced wholesale (never mutated in place), so a reader that grabbed a reference
    // keeps a consistent snapshot while a refresh is in flight. One spawn per WmicMinRefreshInterval
    // at most, and no spawn at all while the TTL is valid and every live PID is already covered.
    private static readonly object _wmicCacheLock = new();
    private static Dictionary<int, int> _wmicParentPid = new();
    private static Dictionary<int, string> _wmicCommandLine = new();
    private static Dictionary<int, string> _wmicExePath = new();
    private static DateTime _wmicCacheUtc = DateTime.MinValue;
    private static DateTime _wmicLastAttemptUtc = DateTime.MinValue;
    private static readonly TimeSpan WmicCacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WmicMinRefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>Returns the cached wmic maps, re-spawning wmic.exe only when the cache is actually stale.</summary>
    private static async Task<(Dictionary<int, int> ParentPid, Dictionary<int, string> CommandLine, Dictionary<int, string> ExePath)>
        GetWmicProcessMapsAsync(HashSet<int> activePids, bool forceRefresh, CancellationToken ct)
    {
        lock (_wmicCacheLock)
        {
            var now = DateTime.UtcNow;
            var age = _wmicCacheUtc == DateTime.MinValue ? TimeSpan.MaxValue : now - _wmicCacheUtc;
            var attemptAge = _wmicLastAttemptUtc == DateTime.MinValue ? TimeSpan.MaxValue : now - _wmicLastAttemptUtc;
            var coversAllLivePids = activePids.All(pid => _wmicParentPid.ContainsKey(pid));

            var needSpawn = forceRefresh || age > WmicCacheTtl || !coversAllLivePids;
            // Even when new PIDs appeared, never re-spawn more often than the minimum interval.
            if (needSpawn && !forceRefresh && attemptAge < WmicMinRefreshInterval) needSpawn = false;

            if (!needSpawn)
                return (_wmicParentPid, _wmicCommandLine, _wmicExePath);

            _wmicLastAttemptUtc = now;
        }

        var parentPidMap = new Dictionary<int, int>();
        var commandLineMap = new Dictionary<int, string>();
        var exePathMap = new Dictionary<int, string>();

        try
        {
            var wmicPath = Path.Combine(Environment.SystemDirectory, "wbem", "wmic.exe");
            var altWmicPath = Path.Combine(Environment.SystemDirectory, "wmic.exe");
            if (File.Exists(wmicPath) || File.Exists(altWmicPath))
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                var (wmicOut, _, code) = await ProcessHelper.RunProcessAsync("wmic.exe", "process get ProcessId,ParentProcessId,CommandLine,ExecutablePath /format:csv", cancellationToken: timeoutCts.Token);
                if (code == 0 && !string.IsNullOrWhiteSpace(wmicOut))
                {
                    var lines = wmicOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',');
                        // Format: Node, CommandLine, ExecutablePath, ParentProcessId, ProcessId
                        if (parts.Length >= 5 && int.TryParse(parts[^1].Trim(), out int pid))
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

        lock (_wmicCacheLock)
        {
            // Publish only a non-empty snapshot; a failed spawn leaves the previous cache in place.
            if (parentPidMap.Count > 0 || commandLineMap.Count > 0 || exePathMap.Count > 0)
            {
                _wmicParentPid = parentPidMap;
                _wmicCommandLine = commandLineMap;
                _wmicExePath = exePathMap;
                _wmicCacheUtc = DateTime.UtcNow;
            }

            return (_wmicParentPid, _wmicCommandLine, _wmicExePath);
        }
    }

    public async Task<List<ProcessHunterItem>> GetRunningProcessesDeepAsync(CancellationToken ct = default, bool forceWmicRefresh = false)
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

            // 2. Fetch ParentProcessId and CommandLine via WMIC.
            //    C1: served from a short-lived cache - the command-line/parent maps change slowly, so we
            //    spawn wmic at most once per WmicMinRefreshInterval and skip the spawn entirely while the
            //    cache is inside its TTL and already covers every live PID.
            var (parentPidMap, commandLineMap, exePathMap) = await GetWmicProcessMapsAsync(activePids, forceWmicRefresh, ct);

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

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr hProcess);

    public Task<string> TrimProcessWorkingSetsAsync()
    {
        return Task.Run(() =>
        {
            int trimmed = 0;
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                foreach (var proc in Process.GetProcesses())
                {
                    using (proc)
                    {
                        try
                        {
                            if (proc.Id <= 4) continue;
                            if (EmptyWorkingSet(proc.Handle))
                            {
                                trimmed++;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return $"已對 {trimmed} 個處理程序執行 Working Set 記憶體修剪。";
        });
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
