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

        // 2. CPU & Real-Time PhysicalDisk Metrics via typeperf sample
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
            const string typeperfCounters = "\"\\Processor(_Total)\\% Processor Time\" \"\\PhysicalDisk(_Total)\\% Disk Time\" \"\\PhysicalDisk(_Total)\\Avg. Disk sec/Read\" \"\\PhysicalDisk(_Total)\\Avg. Disk sec/Write\" \"\\PhysicalDisk(_Total)\\Disk Read Bytes/sec\" \"\\PhysicalDisk(_Total)\\Disk Write Bytes/sec\" -sc 1";
            var (outStr, _, code) = await ProcessHelper.RunProcessAsync("typeperf.exe", typeperfCounters, cancellationToken: timeoutCts.Token);
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var dataLine = lines.LastOrDefault(l => l.Contains(','));
                if (dataLine != null)
                {
                    var parts = dataLine.Split('"').Where(p => p != "," && !string.IsNullOrWhiteSpace(p)).ToArray();
                    if (parts.Length >= 7)
                    {
                        if (double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cpu))
                        {
                            snap.CpuUsagePercent = Math.Clamp(Math.Round(cpu, 1), 0.0, 100.0);
                        }
                        if (double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double diskPct))
                        {
                            snap.DiskUsagePercent = Math.Clamp(Math.Round(diskPct, 1), 0.0, 100.0);
                        }
                        if (double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double readSec))
                        {
                            snap.DiskReadLatencyMs = Math.Max(0, Math.Round(readSec * 1000.0, 2));
                        }
                        if (double.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double writeSec))
                        {
                            snap.DiskWriteLatencyMs = Math.Max(0, Math.Round(writeSec * 1000.0, 2));
                        }
                        if (double.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double readBytes))
                        {
                            snap.DiskReadRateDisplay = FormatBytesPerSec(readBytes);
                        }
                        if (double.TryParse(parts[6], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double writeBytes))
                        {
                            snap.DiskWriteRateDisplay = FormatBytesPerSec(writeBytes);
                        }
                    }
                }
            }
        }
        catch { }

        return snap;
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
        return await Task.Run(async () =>
        {
            var summary = new SystemInfoSummary();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
                var (outStr, _, code) = await ProcessHelper.RunProcessAsync("systeminfo.exe", "", cancellationToken: timeoutCts.Token);
                if (string.IsNullOrWhiteSpace(outStr)) return summary;

                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var hotfixes = new List<string>();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Host Name:", StringComparison.OrdinalIgnoreCase))
                        summary.HostName = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("OS Name:", StringComparison.OrdinalIgnoreCase))
                        summary.OsName = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("OS Version:", StringComparison.OrdinalIgnoreCase))
                        summary.OsVersion = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("System Manufacturer:", StringComparison.OrdinalIgnoreCase))
                        summary.SystemManufacturer = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("System Model:", StringComparison.OrdinalIgnoreCase))
                        summary.SystemModel = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("BIOS Version:", StringComparison.OrdinalIgnoreCase))
                        summary.BiosVersion = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("System Boot Time:", StringComparison.OrdinalIgnoreCase))
                        summary.SystemBootTime = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("Total Physical Memory:", StringComparison.OrdinalIgnoreCase))
                        summary.TotalPhysicalMemory = ExtractValue(trimmed);
                    else if (trimmed.StartsWith("Available Physical Memory:", StringComparison.OrdinalIgnoreCase))
                        summary.AvailablePhysicalMemory = ExtractValue(trimmed);
                    else if (Regex.IsMatch(trimmed, @"\[\d+\]:\s*(KB\d+)"))
                    {
                        var m = Regex.Match(trimmed, @"(KB\d+)");
                        if (m.Success) hotfixes.Add(m.Value);
                    }
                }

                summary.Hotfixes = hotfixes;
                summary.HotfixCount = hotfixes.Count;
            }
            catch { }
            return summary;
        }, ct);
    }

    private static string ExtractValue(string line)
    {
        var idx = line.IndexOf(':');
        return idx > 0 && idx < line.Length - 1 ? line.Substring(idx + 1).Trim() : "";
    }
}
