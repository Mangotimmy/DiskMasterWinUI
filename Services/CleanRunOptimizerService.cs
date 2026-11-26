using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Universal PC Clean-Run &amp; RAM Pressure Optimizer service.
/// Provides real-time RAM/Commit/Pagefile/Pool/WebView2 lag diagnostics, foreground-safe Working Set trimming,
/// Standby List purging (NtSetSystemInformation), Windows 11 Efficiency Mode (EcoQoS) throttling,
/// reversible background service &amp; browser bloat toggles, and 3 Clean-Run presets.
/// </summary>
public class CleanRunOptimizerService
{
    private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss",
        "wininit", "winlogon", "services", "lsass", "lsaiso", "fontdrvhost",
        "dwm", "audiodg", "MsMpEng", "NisSrv", "SecurityHealthService",
        "DiskMasterWinUI", "svchost"
    };

    // ── Win32 P/Invoke Definitions ──

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
        public void Init() => dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    /// <summary>
    /// Fast 0ms native check of current physical RAM usage % and available MB.
    /// </summary>
    public static (double UsagePercent, long AvailableMb, long TotalMb) GetQuickMemoryStatus()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            mem.Init();
            if (GlobalMemoryStatusEx(ref mem))
            {
                long totalMb = (long)(mem.ullTotalPhys / (1024 * 1024));
                long availMb = (long)(mem.ullAvailPhys / (1024 * 1024));
                return (Math.Clamp(mem.dwMemoryLoad, 0, 100), availMb, totalMb);
            }
        }
        catch { }
        return (0, 0, 0);
    }

    /// <summary>
    /// Resolves the PID and ProcessName of the currently focused foreground window.
    /// </summary>
    public static (int Pid, string ProcessName) GetForegroundProcessInfo()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid > 4)
                {
                    using var p = Process.GetProcessById((int)pid);
                    return ((int)pid, p.ProcessName);
                }
            }
        }
        catch { }
        return (-1, "");
    }

    /// <summary>
    /// Parses a comma/semicolon-separated whitelist string into a normalized case-insensitive set.
    /// </summary>
    public static HashSet<string> ParseWhitelist(string? whitelistCsv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(whitelistCsv)) return set;

        foreach (var raw in whitelistCsv.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = raw.Trim();
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean[..^4];
            if (clean.Length > 0) set.Add(clean);
        }
        return set;
    }

    /// <summary>
    /// Evaluates whether a process should be skipped during Working Set trimming.
    /// </summary>
    public static bool ShouldExcludeProcess(int pid, string processName, int foregroundPid, bool excludeForeground, HashSet<string> userWhitelist)
    {
        if (pid <= 4) return true;
        if (excludeForeground && foregroundPid > 0 && pid == foregroundPid) return true;
        if (string.IsNullOrWhiteSpace(processName)) return true;

        var clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        if (CriticalSystemProcesses.Contains(clean)) return true;
        if (userWhitelist.Contains(clean)) return true;
        return false;
    }

    /// <summary>
    /// Runs a comprehensive live RAM Pressure &amp; Lag Doctor analysis:
    /// Physical RAM, Commit Charge, Pagefile usage, Kernel Pools, Pages/sec,
    /// grouped Top RAM hogs, WebView2 owner attribution, and actionable findings.
    /// </summary>
    public Task<RamPressureReport> AnalyzeRamPressureAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var (ramPct, availMb, totalMb) = GetQuickMemoryStatus();
            long usedMb = Math.Max(0, totalMb - availMb);

            long commitUsedMb = 0, commitLimitMb = 0, pagedPoolMb = 0, nonPagedPoolMb = 0;
            try
            {
                if (GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>()))
                {
                    ulong pageBytes = pi.PageSize.ToUInt64();
                    commitUsedMb = (long)(pi.CommitTotal.ToUInt64() * pageBytes / (1024 * 1024));
                    commitLimitMb = (long)(pi.CommitLimit.ToUInt64() * pageBytes / (1024 * 1024));
                    pagedPoolMb = (long)(pi.KernelPaged.ToUInt64() * pageBytes / (1024 * 1024));
                    nonPagedPoolMb = (long)(pi.KernelNonpaged.ToUInt64() * pageBytes / (1024 * 1024));
                }
            }
            catch { }

            long pfAllocMb = 0, pfCurMb = 0, pfPeakMb = 0;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage");
                using var results = searcher.Get();
                foreach (var obj in results)
                {
                    pfAllocMb += Convert.ToInt64(obj["AllocatedBaseSize"] ?? 0);
                    pfCurMb += Convert.ToInt64(obj["CurrentUsage"] ?? 0);
                    pfPeakMb += Convert.ToInt64(obj["PeakUsage"] ?? 0);
                }
            }
            catch { }

            double pagesPerSec = 0;
            try
            {
                using var memPerf = new ManagementObjectSearcher("SELECT PagesPersec FROM Win32_PerfFormattedData_PerfOS_Memory");
                using var memRes = memPerf.Get();
                foreach (var obj in memRes)
                {
                    pagesPerSec = Math.Round(Convert.ToDouble(obj["PagesPersec"] ?? 0), 1);
                    break;
                }
            }
            catch { }

            // Group running processes by ProcessName
            var groups = new Dictionary<string, (int Count, double WsMb, double PmMb, int Handles)>(StringComparer.OrdinalIgnoreCase);
            var runningProcs = Process.GetProcesses();
            foreach (var proc in runningProcs)
            {
                using (proc)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (proc.Id <= 4) continue;
                        string name = proc.ProcessName;
                        double ws = proc.WorkingSet64 / (1024.0 * 1024.0);
                        double pm = proc.PrivateMemorySize64 / (1024.0 * 1024.0);
                        int handles = proc.HandleCount;

                        if (groups.TryGetValue(name, out var agg))
                            groups[name] = (agg.Count + 1, agg.WsMb + ws, agg.PmMb + pm, agg.Handles + handles);
                        else
                            groups[name] = (1, ws, pm, handles);
                    }
                    catch { }
                }
            }

            var topGroups = groups
                .OrderByDescending(kv => kv.Value.PmMb)
                .Take(15)
                .Select(kv => new ProcessGroupMemoryItem
                {
                    ProcessName = kv.Key,
                    InstanceCount = kv.Value.Count,
                    WorkingSetMb = Math.Round(kv.Value.WsMb, 1),
                    PrivateMemoryMb = Math.Round(kv.Value.PmMb, 1),
                    HandleCount = kv.Value.Handles,
                    CategoryBadge = ClassifyProcessGroup(kv.Key),
                    IsSystemProtected = CriticalSystemProcesses.Contains(kv.Key),
                    IsHighUsage = kv.Value.PmMb >= 500
                })
                .ToList();

            // Attribute msedgewebview2.exe child processes back to their owner app
            var webViewOwners = ResolveWebView2Owners();

            // Generate plain-language root-cause lag findings
            var findings = new List<string>();
            double commitPct = commitLimitMb > 0 ? Math.Round((double)commitUsedMb * 100.0 / commitLimitMb, 1) : 0;

            if (totalMb > 0 && commitUsedMb > totalMb * 1.15)
            {
                findings.Add($"⚠️ Commit 虛擬提交量 ({commitUsedMb / 1024.0:F1} GB) 已超過實體 RAM ({totalMb / 1024.0:F1} GB)，系統被迫大量使用磁碟分頁檔 ({pfCurMb / 1024.0:F1} GB)，這是畫面卡頓的頭號主因。");
            }
            if (pagesPerSec >= 500)
            {
                findings.Add($"⚠️ 分頁交換率偏高 ({pagesPerSec:F0} pages/s)：記憶體正在與 SSD/硬碟頻繁換頁。");
            }
            if (groups.TryGetValue("TiWorker", out var ti) && ti.PmMb >= 800)
            {
                findings.Add($"🔥 Windows 更新模組安裝員 (TiWorker) 正在背景吃掉 {ti.PmMb / 1024.0:F2} GB RAM！建議至「系統健康 → Windows Update」檢查更新錯誤並還原 TrustedInstaller 為手動啟動。");
            }
            if (groups.TryGetValue("msedgewebview2", out var wv) && (wv.Count >= 10 || wv.PmMb >= 800))
            {
                var topOwner = webViewOwners.FirstOrDefault();
                string ownerHint = topOwner != null ? $"（最大來源：{topOwner.OwnerProcessName} 佔 {topOwner.TotalPrivateMb:F0} MB）" : "";
                findings.Add($"🌐 偵測到 {wv.Count} 個 WebView2 常駐行程合計消耗 {wv.PmMb / 1024.0:F2} GB RAM{ownerHint}。");
            }
            if (groups.ContainsKey("Discord") && groups.ContainsKey("DiscordPTB"))
            {
                double bothMb = groups["Discord"].PmMb + groups["DiscordPTB"].PmMb;
                findings.Add($"🔁 同時開啟了 Discord 與 DiscordPTB 兩套版本，合計佔用 {bothMb / 1024.0:F2} GB RAM，建議在開機啟動管理中停用其中一個。");
            }
            if (groups.TryGetValue("SearchIndexer", out var si) && si.PmMb >= 350)
            {
                findings.Add($"🔎 Windows 搜尋索引 (SearchIndexer) 正在佔用 {si.PmMb:F0} MB RAM，可切換為手動或對其套用節能節流。");
            }
            if (pagedPoolMb + nonPagedPoolMb >= 1800)
            {
                findings.Add($"🧩 核心記憶體集區偏高 (Paged {pagedPoolMb} MB + Non-Paged {nonPagedPoolMb} MB)，建議定期重開機釋放驅動程式集區。");
            }
            if (findings.Count == 0)
            {
                findings.Add("✅ 目前未偵測到明顯的分頁抖動或異常背景記憶體黑洞。");
            }

            RamPressureSeverity severity;
            string severityBadge;
            if (ramPct >= 82 || (totalMb > 0 && commitUsedMb > totalMb * 1.35) || pagesPerSec >= 1000)
            {
                severity = RamPressureSeverity.CriticalThrashing;
                severityBadge = "🔴 高度記憶體壓力 / 分頁卡頓";
            }
            else if (ramPct >= 70 || (totalMb > 0 && commitUsedMb > totalMb * 1.05))
            {
                severity = RamPressureSeverity.Elevated;
                severityBadge = "🟡 記憶體負載偏高";
            }
            else
            {
                severity = RamPressureSeverity.Healthy;
                severityBadge = "🟢 記憶體狀態健康順暢";
            }

            return new RamPressureReport
            {
                PhysicalUsedMb = usedMb,
                PhysicalTotalMb = totalMb,
                PhysicalUsagePercent = ramPct,
                CommitUsedMb = commitUsedMb,
                CommitLimitMb = commitLimitMb,
                CommitUsagePercent = commitPct,
                PagefileAllocatedMb = pfAllocMb,
                PagefileCurrentMb = pfCurMb,
                PagefilePeakMb = pfPeakMb,
                PagedPoolMb = pagedPoolMb,
                NonPagedPoolMb = nonPagedPoolMb,
                PagesPerSec = pagesPerSec,
                Severity = severity,
                SeverityBadge = severityBadge,
                SummaryText = findings[0],
                TopProcessGroups = topGroups,
                WebView2Owners = webViewOwners,
                DiagnosticFindings = findings
            };
        }, ct);
    }

    /// <summary>
    /// Safely releases RAM by trimming background working sets (excluding foreground active app,
    /// critical system processes, and user whitelist) and optionally purging the Standby List.
    /// </summary>
    public Task<MemoryReleaseResult> ReleaseMemorySafelyAsync(
        bool purgeStandbyList,
        bool excludeForeground,
        string? whitelistCsv,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var (_, availBeforeMb, _) = GetQuickMemoryStatus();
            var (fgPid, fgName) = excludeForeground ? GetForegroundProcessInfo() : (-1, "");
            var whitelist = ParseWhitelist(whitelistCsv);

            int trimmed = 0;
            int skipped = 0;

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    using (proc)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            if (ShouldExcludeProcess(proc.Id, proc.ProcessName, fgPid, excludeForeground, whitelist))
                            {
                                skipped++;
                                continue;
                            }

                            IntPtr hProc = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                            if (hProc != IntPtr.Zero)
                            {
                                try
                                {
                                    if (EmptyWorkingSet(hProc)) trimmed++;
                                }
                                finally
                                {
                                    CloseHandle(hProc);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            bool standbyPurged = false;
            if (purgeStandbyList && !ct.IsCancellationRequested)
            {
                standbyPurged = TryPurgeStandbyList();
            }

            // Short settle pause so OS updates GlobalMemoryStatusEx counters
            Thread.Sleep(250);
            var (_, availAfterMb, _) = GetQuickMemoryStatus();
            long reclaimed = Math.Max(0, availAfterMb - availBeforeMb);

            string standbyNote = purgeStandbyList
                ? (standbyPurged ? "＋已清除 Standby 快取" : "（Standby 清除需管理員權限）")
                : "";
            string fgNote = excludeForeground && !string.IsNullOrWhiteSpace(fgName)
                ? $"，已保護前景程式 [{fgName}]"
                : "";

            return new MemoryReleaseResult
            {
                Success = true,
                TrimmedProcessCount = trimmed,
                SkippedProtectedCount = skipped,
                ForegroundExcludedApp = fgName,
                StandbyListPurged = standbyPurged,
                AvailableBeforeMb = availBeforeMb,
                AvailableAfterMb = availAfterMb,
                Message = $"✅ 已安全修剪 {trimmed} 個背景行程{standbyNote}，可用 RAM 增加 +{reclaimed:N0} MB ({availBeforeMb:N0} → {availAfterMb:N0} MB){fgNote}。"
            };
        }, ct);
    }

    /// <summary>
    /// Applies or clears Windows 11 Efficiency Mode (EcoQoS) + BelowNormal priority on a process group.
    /// </summary>
    public Task<(bool Success, int AffectedCount, string Message)> SetProcessGroupEfficiencyModeAsync(string processName, bool enableEcoQos)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(processName))
                return (false, 0, "處理程序名稱不可為空。");

            var clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;

            if (CriticalSystemProcesses.Contains(clean))
                return (false, 0, $"基於系統穩定性，禁止對核心系統行程 [{clean}] 套用節能節流。");

            int affected = 0;
            var procs = Process.GetProcessesByName(clean);
            foreach (var p in procs)
            {
                using (p)
                {
                    try
                    {
                        IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                        if (hProc != IntPtr.Zero)
                        {
                            try
                            {
                                var state = new PROCESS_POWER_THROTTLING_STATE
                                {
                                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                                    StateMask = enableEcoQos ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u
                                };
                                SetProcessInformation(hProc, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                            }
                            finally
                            {
                                CloseHandle(hProc);
                            }
                        }

                        try { p.PriorityClass = enableEcoQos ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; } catch { }
                        affected++;
                    }
                    catch { }
                }
            }

            if (affected == 0)
                return (false, 0, $"找不到可調整權限的 {clean} 行程（系統保護或已結束）。");

            string mode = enableEcoQos ? "已套用 Windows 節能節流模式 (EcoQoS + BelowNormal)" : "已恢復標準效能排程";
            return (true, affected, $"✅ {clean} ({affected} 個行程): {mode}");
        });
    }

    /// <summary>
    /// Reads current status of all reversible background service &amp; browser bloat reduction tweaks.
    /// </summary>
    public Task<List<BackgroundBloatTweakItem>> GetBackgroundBloatTweaksAsync()
    {
        return Task.Run(async () =>
        {
            var list = new List<BackgroundBloatTweakItem>
            {
                new()
                {
                    Id = "TrustedInstallerManual",
                    Title = "TrustedInstaller 還原為手動啟動 (防止 TiWorker 背景暴吃 2~3 GB RAM)",
                    Description = "Windows 預設應為手動 (Manual)；若異常變成自動啟動，背景會反覆喚醒 TiWorker 吃光記憶體與磁碟。",
                    TargetBadge = "Service: TrustedInstaller",
                    SafeNote = "完全安全，Windows Update 需要時仍會自動按需喚醒。",
                    IsOptimized = await IsServiceStartTypeAsync("TrustedInstaller", "DEMAND_START"),
                    CurrentStateDisplay = await GetServiceStartDisplayAsync("TrustedInstaller")
                },
                new()
                {
                    Id = "EdgeBackgroundOff",
                    Title = "關閉 Microsoft Edge 啟動加速與背景常駐 (StartupBoost / BackgroundMode)",
                    Description = "禁止 Edge 在關閉視窗後持續留駐 10+ 個背景行程消耗 500MB+ RAM。",
                    TargetBadge = "Policy: Edge",
                    SafeNote = "不影響正常開啟瀏覽器，隨時可還原。",
                    IsOptimized = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled") == 0
                               && ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled") == 0,
                    CurrentStateDisplay = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled") == 0 ? "已禁止背景常駐" : "預設允許背景常駐"
                },
                new()
                {
                    Id = "ChromeBackgroundOff",
                    Title = "關閉 Google Chrome 關閉視窗後背景常駐 (BackgroundModeEnabled = 0)",
                    Description = "防止 Chrome 擴充功能與背景同步在關閉後持續佔用數百 MB 記憶體。",
                    TargetBadge = "Policy: Chrome",
                    SafeNote = "完全安全，開啟 Chrome 時一切正常。",
                    IsOptimized = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled") == 0,
                    CurrentStateDisplay = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled") == 0 ? "已禁止背景常駐" : "預設允許背景常駐"
                },
                new()
                {
                    Id = "DeliveryOptP2pOff",
                    Title = "關閉 Windows 更新傳遞最佳化 P2P 頻寬與背景快取 (DODownloadMode = 0)",
                    Description = "停用 DoSvc 對外 P2P 上傳共享，保留正常官方 HTTP 更新下載並降低背景記憶體與磁碟開銷。",
                    TargetBadge = "Policy: DeliveryOptimization",
                    SafeNote = "Windows Update 與 Store 下載皆維持正常。",
                    IsOptimized = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode") == 0,
                    CurrentStateDisplay = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode") == 0 ? "已設為純 HTTP (停用 P2P)" : "預設允許 P2P"
                },
                new()
                {
                    Id = "WidgetsOff",
                    Title = "停用 Windows 小工具 / 新聞與興趣背景 WebView2 常駐",
                    Description = "消除系統列小工具在背景啟動多個 msedgewebview2 行程吃掉數百 MB 記憶體。",
                    TargetBadge = "Policy: Dsh (AllowNewsAndInterests)",
                    SafeNote = "隨時可重新開啟。",
                    IsOptimized = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests") == 0,
                    CurrentStateDisplay = ReadRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests") == 0 ? "已停用小工具常駐" : "預設啟用"
                },
                new()
                {
                    Id = "BackgroundUwpAppsOff",
                    Title = "限制非使用中 UWP / Microsoft Store 應用程式背景執行",
                    Description = "寫入 GlobalUserDisabled = 1，減少新聞、天氣、Xbox 等內建應用於背景偷偷佔用 RAM。",
                    TargetBadge = "HKCU: BackgroundAccessApplications",
                    SafeNote = "前景開啟應用完全不受影響。",
                    RequiresAdmin = false,
                    IsOptimized = ReadRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == 1,
                    CurrentStateDisplay = ReadRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == 1 ? "已限制背景 UWP" : "預設允許背景 UWP"
                },
                new()
                {
                    Id = "WSearchManual",
                    Title = "將 Windows 搜尋索引 (WSearch / SearchIndexer) 改為手動啟動",
                    Description = "SearchIndexer 常在背景吃掉 500MB~1GB RAM 與磁碟 I/O；改為手動可大幅減少 16GB 筆電卡頓。",
                    TargetBadge = "Service: WSearch",
                    SafeNote = "開始功能表基本搜尋仍可用，僅減少全硬碟即時索引負載。",
                    IsOptimized = await IsServiceStartTypeAsync("WSearch", "DEMAND_START") || await IsServiceStartTypeAsync("WSearch", "DISABLED"),
                    CurrentStateDisplay = await GetServiceStartDisplayAsync("WSearch")
                },
                new()
                {
                    Id = "SysMainDisabled",
                    Title = "停用 SysMain (Superfetch) 預先載入快取 (SSD 省 RAM 與磁碟讀寫)",
                    Description = "在 NVMe/SSD 電腦上，SysMain 的預讀優勢極低卻會額外佔用記憶體與背景磁碟佇列。",
                    TargetBadge = "Service: SysMain",
                    SafeNote = "純 SSD 電腦建議開啟此優化；隨時可一鍵恢復自動啟動。",
                    IsOptimized = await IsServiceStartTypeAsync("SysMain", "DISABLED"),
                    CurrentStateDisplay = await GetServiceStartDisplayAsync("SysMain")
                }
            };

            return list;
        });
    }

    /// <summary>
    /// Applies or reverts a single reversible background bloat tweak.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyBackgroundBloatTweakAsync(string tweakId, bool optimize)
    {
        try
        {
            switch (tweakId)
            {
                case "TrustedInstallerManual":
                    var (_, errTi, codeTi) = await ProcessHelper.RunProcessAsync("sc.exe", optimize ? "config TrustedInstaller start= demand" : "config TrustedInstaller start= demand");
                    return (codeTi == 0, codeTi == 0 ? "✅ TrustedInstaller 已設為 Windows 預設手動啟動 (DEMAND_START)。" : $"❌ 設定失敗: {errTi}");

                case "EdgeBackgroundOff":
                    if (optimize)
                    {
                        WriteRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                        WriteRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
                        return (true, "✅ 已關閉 Edge 啟動加速與背景常駐（重開 Edge 後生效）。");
                    }
                    DeleteRegValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled");
                    DeleteRegValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled");
                    return (true, "✅ 已還原 Edge 背景設定為預設值。");

                case "ChromeBackgroundOff":
                    if (optimize)
                    {
                        WriteRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0);
                        return (true, "✅ 已關閉 Chrome 背景常駐模式（重開 Chrome 後生效）。");
                    }
                    DeleteRegValue(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled");
                    return (true, "✅ 已還原 Chrome 背景設定為預設值。");

                case "DeliveryOptP2pOff":
                    if (optimize)
                    {
                        WriteRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0);
                        return (true, "✅ 已停用 Windows 更新 P2P 傳遞最佳化（保留純 HTTP 官方下載）。");
                    }
                    DeleteRegValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
                    return (true, "✅ 已還原傳遞最佳化原則為預設值。");

                case "WidgetsOff":
                    if (optimize)
                    {
                        WriteRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
                        return (true, "✅ 已停用 Windows 小工具背景常駐。");
                    }
                    DeleteRegValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests");
                    return (true, "✅ 已還原 Windows 小工具設定。");

                case "BackgroundUwpAppsOff":
                    WriteRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", optimize ? 1 : 0);
                    return (true, optimize ? "✅ 已限制 UWP 應用程式在背景偷跑。" : "✅ 已還原 UWP 背景執行為預設值。");

                case "WSearchManual":
                    if (optimize)
                    {
                        var (_, errWs, codeWs) = await ProcessHelper.RunProcessAsync("sc.exe", "config WSearch start= demand");
                        await ProcessHelper.RunProcessAsync("sc.exe", "stop WSearch");
                        return (codeWs == 0, codeWs == 0 ? "✅ 已將 Windows Search (WSearch) 調整為手動並暫停高耗索引。" : $"❌ 設定 WSearch 失敗: {errWs}");
                    }
                    else
                    {
                        var (_, errWs, codeWs) = await ProcessHelper.RunProcessAsync("sc.exe", "config WSearch start= delayed-auto");
                        await ProcessHelper.RunProcessAsync("sc.exe", "start WSearch");
                        return (codeWs == 0, codeWs == 0 ? "✅ 已還原 Windows Search (WSearch) 為自動延遲啟動。" : $"❌ 還原 WSearch 失敗: {errWs}");
                    }

                case "SysMainDisabled":
                    if (optimize)
                    {
                        var (_, errSm, codeSm) = await ProcessHelper.RunProcessAsync("sc.exe", "config SysMain start= disabled");
                        await ProcessHelper.RunProcessAsync("sc.exe", "stop SysMain");
                        return (codeSm == 0, codeSm == 0 ? "✅ 已停用 SysMain (Superfetch) 減少背景預讀記憶體開銷。" : $"❌ 停用 SysMain 失敗: {errSm}");
                    }
                    else
                    {
                        var (_, errSm, codeSm) = await ProcessHelper.RunProcessAsync("sc.exe", "config SysMain start= auto");
                        await ProcessHelper.RunProcessAsync("sc.exe", "start SysMain");
                        return (codeSm == 0, codeSm == 0 ? "✅ 已還原 SysMain 為自動啟動。" : $"❌ 還原 SysMain 失敗: {errSm}");
                    }

                default:
                    return (false, $"未知的減負項目: {tweakId}");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "❌ 權限不足：請以系統管理員身分執行 DiskMaster Pro。");
        }
        catch (Exception ex)
        {
            return (false, $"❌ 套用設定失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies one of the 3 universal PC Clean-Run presets cleanly and reversibly.
    /// </summary>
    public async Task<(bool Success, string Summary)> ApplyCleanRunPresetAsync(CleanRunPresetKind preset, Action<string>? onLog = null)
    {
        var sb = new StringBuilder();
        void Log(string m) { sb.AppendLine(m); onLog?.Invoke(m); }

        var optimizer = new SystemOptimizerService();
        Log($"[{DateTime.Now:HH:mm:ss}] 建立登錄檔安全快照...");
        await optimizer.CreatePreTweakBackupAsync($"Before_CleanRun_{preset}");

        var s = SettingsService.Instance.Current;
        bool allOk = true;

        async Task RunTweak(string id, bool opt)
        {
            var (ok, msg) = await ApplyBackgroundBloatTweakAsync(id, opt);
            Log(msg);
            if (!ok) allOk = false;
        }

        if (preset == CleanRunPresetKind.SafeDaily)
        {
            Log("══ 套用【安全日常省資源 (Safe Clean-Run)】 ══");
            await RunTweak("TrustedInstallerManual", true);
            await RunTweak("EdgeBackgroundOff", true);
            await RunTweak("ChromeBackgroundOff", true);
            await RunTweak("DeliveryOptP2pOff", true);
            await RunTweak("BackgroundUwpAppsOff", true);

            s.AutoRamGuardEnabled = true;
            s.AutoRamGuardThresholdPercent = 85;
            s.AutoRamGuardCooldownMinutes = 10;
            s.AutoRamGuardExcludeForeground = true;
            s.AutoRamGuardPurgeStandbyList = false;
            SettingsService.Instance.Save();
            Log("✅ 已開啟安全自動記憶體守衛 (門檻 85%、保護前景視窗)。");
        }
        else if (preset == CleanRunPresetKind.Balanced16Gb)
        {
            Log("══ 套用【16GB 低延遲順跑 (Balanced 16GB Smooth)】 ══");
            await RunTweak("TrustedInstallerManual", true);
            await RunTweak("EdgeBackgroundOff", true);
            await RunTweak("ChromeBackgroundOff", true);
            await RunTweak("DeliveryOptP2pOff", true);
            await RunTweak("WidgetsOff", true);
            await RunTweak("BackgroundUwpAppsOff", true);
            await RunTweak("WSearchManual", true);
            await RunTweak("SysMainDisabled", true);
            var (diagOk, diagMsg) = await optimizer.SetDiagTrackServiceDisabledAsync(true);
            Log(diagMsg);
            if (!diagOk) allOk = false;

            s.AutoRamGuardEnabled = true;
            s.AutoRamGuardThresholdPercent = 80;
            s.AutoRamGuardCooldownMinutes = 10;
            s.AutoRamGuardExcludeForeground = true;
            s.AutoRamGuardPurgeStandbyList = true;
            SettingsService.Instance.Save();
            Log("✅ 已開啟自動記憶體守衛 (門檻 80%、含 Standby 清理、保護前景視窗)。");

            var release = await ReleaseMemorySafelyAsync(true, true, s.AutoRamGuardWhitelistCsv);
            Log(release.Message);
        }
        else
        {
            Log("══ 還原【Windows 系統預設設定 (Restore Defaults)】 ══");
            await RunTweak("TrustedInstallerManual", true);
            await RunTweak("EdgeBackgroundOff", false);
            await RunTweak("ChromeBackgroundOff", false);
            await RunTweak("DeliveryOptP2pOff", false);
            await RunTweak("WidgetsOff", false);
            await RunTweak("BackgroundUwpAppsOff", false);
            await RunTweak("WSearchManual", false);
            await RunTweak("SysMainDisabled", false);
            var (diagOk, diagMsg) = await optimizer.SetDiagTrackServiceDisabledAsync(false);
            Log(diagMsg);
            if (!diagOk) allOk = false;

            s.AutoRamGuardEnabled = false;
            SettingsService.Instance.Save();
            Log("✅ 已關閉自動記憶體守衛並還原所有背景服務與瀏覽器原則。");
        }

        string summary = allOk
            ? $"✅ 已完成套用預設檔 [{preset}]！"
            : $"⚠️ 預設檔 [{preset}] 部分項目因權限不足未套用，請確認以系統管理員身分執行。";
        return (allOk, summary);
    }

    private static bool TryPurgeStandbyList()
    {
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var hToken))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    return false;

                int command = MemoryPurgeStandbyList;
                int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
                return status == 0;
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        catch
        {
            return false;
        }
    }

    private static List<WebView2OwnerItem> ResolveWebView2Owners()
    {
        var results = new List<WebView2OwnerItem>();
        try
        {
            var pidToInfo = new Dictionary<uint, (string Name, uint ParentPid, double PrivateMb)>();
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, PrivatePageCount FROM Win32_Process");
            using var col = searcher.Get();
            foreach (var obj in col)
            {
                uint pid = Convert.ToUInt32(obj["ProcessId"] ?? 0);
                uint ppid = Convert.ToUInt32(obj["ParentProcessId"] ?? 0);
                string name = obj["Name"]?.ToString() ?? "";
                double pmMb = Convert.ToDouble(obj["PrivatePageCount"] ?? 0) / (1024.0 * 1024.0);
                pidToInfo[pid] = (name, ppid, pmMb);
            }

            var byOwner = new Dictionary<string, (int Count, double TotalMb)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pidToInfo)
            {
                if (!kv.Value.Name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)) continue;

                string owner = "WebView2 (背景孤立)";
                uint cursor = kv.Value.ParentPid;
                for (int hop = 0; hop < 4 && pidToInfo.TryGetValue(cursor, out var parent); hop++)
                {
                    if (!parent.Name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        owner = parent.Name;
                        break;
                    }
                    cursor = parent.ParentPid;
                }

                if (byOwner.TryGetValue(owner, out var agg))
                    byOwner[owner] = (agg.Count + 1, agg.TotalMb + kv.Value.PrivateMb);
                else
                    byOwner[owner] = (1, kv.Value.PrivateMb);
            }

            results = byOwner
                .OrderByDescending(x => x.Value.TotalMb)
                .Take(8)
                .Select(x => new WebView2OwnerItem
                {
                    OwnerProcessName = x.Key,
                    WebView2Count = x.Value.Count,
                    TotalPrivateMb = Math.Round(x.Value.TotalMb, 1)
                })
                .ToList();
        }
        catch { }

        return results;
    }

    private static string ClassifyProcessGroup(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("webview2")) return "WebView2";
        if (n is "chrome" or "msedge" or "firefox" or "brave" or "opera") return "瀏覽器";
        if (n.Contains("discord") || n.Contains("teams") || n.Contains("whatsapp") || n.Contains("slack") || n.Contains("copilot")) return "通訊/AI 常駐";
        if (n is "tiworker" or "trustedinstaller" or "usoclient") return "Windows Update";
        if (n is "searchindexer" or "searchhost") return "搜尋索引";
        if (n.StartsWith("vmmem")) return "虛擬機/WSL";
        if (CriticalSystemProcesses.Contains(name)) return "系統核心";
        return "應用程式";
    }

    private static async Task<bool> IsServiceStartTypeAsync(string serviceName, string expectedToken)
    {
        var (stdout, _, code) = await ProcessHelper.RunProcessAsync("sc.exe", $"qc {serviceName}");
        return code == 0 && stdout.Contains(expectedToken, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetServiceStartDisplayAsync(string serviceName)
    {
        var (stdout, _, code) = await ProcessHelper.RunProcessAsync("sc.exe", $"qc {serviceName}");
        if (code != 0) return "未安裝 / 無法讀取";
        if (stdout.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "已停用 (Disabled)";
        if (stdout.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "手動 (Manual)";
        if (stdout.Contains("DELAYED", StringComparison.OrdinalIgnoreCase)) return "自動延遲 (Delayed-Auto)";
        if (stdout.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase)) return "自動 (Automatic)";
        return "未知";
    }

    private static int? ReadRegDword(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var k = root.OpenSubKey(subKey);
            return k?.GetValue(valueName) is int v ? v : null;
        }
        catch { return null; }
    }

    private static void WriteRegDword(RegistryKey root, string subKey, string valueName, int value)
    {
        using var k = root.CreateSubKey(subKey, writable: true);
        k?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static void DeleteRegValue(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var k = root.OpenSubKey(subKey, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
