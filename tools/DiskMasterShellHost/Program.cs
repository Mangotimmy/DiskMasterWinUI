using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DiskMasterShellHost;

/// <summary>
/// Explorer right-click launcher for DiskMaster Pro.
///
/// The shell verb registered by Services/ShellIntegrationService.cs calls this exe with the clicked
/// path as the first argument, e.g.
///     "C:\Program Files\DiskMaster\DiskMasterShellHost.exe" "D:\Photos"
/// and it starts the GUI app with exactly:
///     DiskMasterWinUI.exe --tab Recovery --recover "D:\Photos"
///
/// App exe resolution order (first hit wins):
///   1. --app "&lt;path&gt;"                       (explicit command line override)
///   2. &lt;launcher dir&gt;\DiskMasterShellHost.app.txt  (first non-empty line = app exe or its folder)
///   3. &lt;launcher dir&gt;\DiskMasterWinUI.exe         (portable / side-by-side install)
///   4. %DISKMASTER_APP_PATH% environment variable
///   5. HKCU\Software\DiskMasterPro\InstallLocation  (written by ShellIntegrationService.Install)
///   6. HKLM\Software\DiskMasterPro\InstallLocation  (all-users install variant)
///   7. %ProgramFiles%\DiskMaster Pro\DiskMasterWinUI.exe and %LOCALAPPDATA%\Programs\DiskMaster Pro\DiskMasterWinUI.exe
///
/// Exit codes: 0 = app started, 2 = app exe not found, 3 = launch failed, 64 = bad usage.
/// Full diagnostics are appended to %TEMP%\DiskMasterShellHost.log.
/// </summary>
internal static class Program
{
    private const string AppExeName = "DiskMasterWinUI.exe";
    private const string OverrideFileName = "DiskMasterShellHost.app.txt";
    private const string PerUserInstallKey = @"Software\DiskMasterPro";
    private const string AllUsersInstallKey = @"SOFTWARE\DiskMasterPro";
    private const string InstallLocationValue = "InstallLocation";
    private const int SwHide = 0;

    [STAThread]
    private static int Main(string[] args)
    {
        HideConsoleWhenLaunchedByShell();

        try
        {
            var parsed = ParseArguments(args);

            if (parsed.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            var appExe = ResolveAppExe(parsed.AppOverride, parsed.Strict, out var resolutionNote);
            if (appExe == null)
            {
                var message = "找不到 DiskMasterWinUI.exe。" +
                              "請用 --app \"<完整路徑>\" 指定，或把路徑寫進 " + OverrideFileName +
                              "（與 DiskMasterShellHost.exe 同資料夾），或重新執行 DiskMaster 的「整合至檔案總管右鍵選單」。";
                Log("ERROR " + message + " | " + resolutionNote);
                Console.Error.WriteLine(message);
                Console.Error.WriteLine("解析過程：" + resolutionNote);
                return 2;
            }

            var argumentLine = BuildArgumentLine(parsed.Path);

            if (parsed.PrintTarget)
            {
                Console.WriteLine(appExe);
                Log($"PRINT-TARGET {appExe} | {resolutionNote}");
                return 0;
            }

            if (parsed.DryRun)
            {
                Console.WriteLine($"APP     : {appExe}");
                Console.WriteLine($"ARGS    : {argumentLine}");
                Console.WriteLine($"RESOLVED: {resolutionNote}");
                Log($"DRY-RUN app={appExe} args={argumentLine} | {resolutionNote}");
                return 0;
            }

            var psi = new ProcessStartInfo
            {
                FileName = appExe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appExe) ?? AppContext.BaseDirectory
            };
            psi.ArgumentList.Add("--tab");
            psi.ArgumentList.Add("Recovery");
            if (!string.IsNullOrWhiteSpace(parsed.Path))
            {
                psi.ArgumentList.Add("--recover");
                psi.ArgumentList.Add(parsed.Path);
            }

            Log($"LAUNCH app={appExe} args={argumentLine} | {resolutionNote}");
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("無法啟動 DiskMasterWinUI.exe（Process.Start 傳回 null）。");
                Log("ERROR Process.Start returned null");
                return 3;
            }

            // Never block the Explorer thread: the GUI keeps running after we exit.
            Log($"OK pid={process.Id}");
            return 0;
        }
        catch (Exception ex)
        {
            Log("EXCEPTION " + ex);
            try { Console.Error.WriteLine("DiskMasterShellHost 失敗：" + ex.Message); } catch { }
            return 3;
        }
    }

    private static string BuildArgumentLine(string path)
        => string.IsNullOrWhiteSpace(path)
            ? "--tab Recovery"
            : $"--tab Recovery --recover \"{path}\"";

    private sealed class ParsedArgs
    {
        public string Path = "";
        public string? AppOverride;
        public bool DryRun;
        public bool PrintTarget;
        public bool ShowHelp;
        public bool Strict;
    }

    private static ParsedArgs ParseArguments(string[] args)
    {
        var parsed = new ParsedArgs();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--app":
                case "-a":
                    if (i + 1 < args.Length) parsed.AppOverride = Clean(args[++i]);
                    break;
                case "--dry-run":
                    parsed.DryRun = true;
                    break;
                case "--strict":
                    parsed.Strict = true;
                    break;
                case "--print-target":
                    parsed.PrintTarget = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    parsed.ShowHelp = true;
                    break;
                case "--recover":
                    // Tolerate a pass-through "--recover <path>" invocation as well.
                    if (i + 1 < args.Length && string.IsNullOrEmpty(parsed.Path)) parsed.Path = Clean(args[++i]);
                    break;
                case "--tab":
                    if (i + 1 < args.Length) i++;   // ignore, we always use the Recovery tab
                    break;
                default:
                    if (arg.StartsWith('-') || arg.StartsWith('/')) break;
                    if (string.IsNullOrEmpty(parsed.Path)) parsed.Path = Clean(arg);
                    break;
            }
        }
        return parsed;
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private static string? ResolveAppExe(string? explicitOverride, bool strict, out string note)
    {
        var log = new StringBuilder();
        var launcherDir = AppContext.BaseDirectory;

        // 1. explicit --app
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            var candidate = NormalizeCandidate(explicitOverride);
            log.Append($"1:--app={candidate} ");
            if (candidate != null) { note = log.ToString(); return candidate; }
        }

        // 2. override file next to the launcher
        try
        {
            var overrideFile = Path.Combine(launcherDir, OverrideFileName);
            if (File.Exists(overrideFile))
            {
                foreach (var line in File.ReadAllLines(overrideFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var candidate = NormalizeCandidate(trimmed);
                    log.Append($"2:{overrideFile}->{candidate ?? "invalid"} ");
                    if (candidate != null) { note = log.ToString(); return candidate; }
                    break;
                }
            }
            else
            {
                log.Append($"2:{OverrideFileName} absent ");
            }
        }
        catch (Exception ex) { log.Append($"2:error {ex.Message} "); }

        // 3. side-by-side with the launcher
        var sibling = NormalizeCandidate(Path.Combine(launcherDir, AppExeName));
        log.Append($"3:{sibling ?? "absent"} ");
        if (sibling != null) { note = log.ToString(); return sibling; }

        if (strict)
        {
            // --strict is used by the installer / verification runs: never fall back to a
            // previously installed copy, so a missing side-by-side layout is a hard failure.
            log.Append("strict:stop ");
            note = log.ToString();
            return null;
        }

        // 4. environment override
        try
        {
            var env = Environment.GetEnvironmentVariable("DISKMASTER_APP_PATH");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var candidate = NormalizeCandidate(env);
                log.Append($"4:env={candidate ?? "invalid"} ");
                if (candidate != null) { note = log.ToString(); return candidate; }
            }
            else
            {
                log.Append("4:env unset ");
            }
        }
        catch { }

        // 5/6. InstallLocation written by ShellIntegrationService.Install()
        foreach (var (hive, subKey, label) in new[]
                 {
                     (Registry.CurrentUser, PerUserInstallKey, "HKCU"),
                     (Registry.LocalMachine, AllUsersInstallKey, "HKLM")
                 })
        {
            try
            {
                using var key = hive.OpenSubKey(subKey);
                var value = key?.GetValue(InstallLocationValue) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var candidate = NormalizeCandidate(value);
                    log.Append($"5:{label}\\{subKey}={candidate ?? "invalid"} ");
                    if (candidate != null) { note = log.ToString(); return candidate; }
                }
                else
                {
                    log.Append($"5:{label}\\{subKey} empty ");
                }
            }
            catch (Exception ex) { log.Append($"5:{label} error {ex.Message} "); }
        }

        // 7. well-known install folders
        foreach (var baseDir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                 })
        {
            foreach (var folder in new[] { "DiskMaster Pro", "DiskMaster", "DiskMasterWinUI" })
            {
                var candidate = NormalizeCandidate(Path.Combine(baseDir, folder, AppExeName));
                if (candidate != null)
                {
                    log.Append($"7:{candidate} ");
                    note = log.ToString();
                    return candidate;
                }
            }
        }
        log.Append("7:none ");

        note = log.ToString();
        return null;
    }

    /// <summary>Accepts either an exe path or a folder and returns an existing exe path (or null).</summary>
    private static string? NormalizeCandidate(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var candidate = value.Trim().Trim('"');
            if (Directory.Exists(candidate))
            {
                var inDir = Path.Combine(candidate, AppExeName);
                return File.Exists(inDir) ? Path.GetFullPath(inDir) : null;
            }
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        catch { }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("DiskMasterShellHost — Explorer right-click launcher for DiskMaster Pro");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  DiskMasterShellHost.exe \"<clicked path>\"            launch the Recovery tab for that path");
        Console.WriteLine("  DiskMasterShellHost.exe [--app <exe>] [--strict] [--dry-run] [--print-target] [--help]");
        Console.WriteLine();
        Console.WriteLine("App exe resolution: --app, " + OverrideFileName + " (next to this exe), side-by-side exe,");
        Console.WriteLine("                    %DISKMASTER_APP_PATH%, HKCU\\" + PerUserInstallKey + "\\" + InstallLocationValue + ",");
        Console.WriteLine("                    HKLM\\" + AllUsersInstallKey + "\\" + InstallLocationValue + ", well-known folders.");
        Console.WriteLine();
        Console.WriteLine("Forwarded command line: --tab Recovery --recover \"<path>\"");
        Console.WriteLine("Exit codes: 0 = started, 2 = app not found, 3 = launch failed, 64 = bad usage.");
    }

    /// <summary>Hides the console window when Explorer (not a terminal) started us.</summary>
    private static void HideConsoleWhenLaunchedByShell()
    {
        try
        {
            var processes = new uint[2];
            uint count = GetConsoleProcessList(processes, (uint)processes.Length);
            if (count <= 1)   // this process owns a freshly created console → it is a console flash
            {
                var handle = GetConsoleWindow();
                if (handle != IntPtr.Zero) ShowWindow(handle, SwHide);
            }
        }
        catch { }
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "DiskMasterShellHost.log");
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) File.Delete(path);
            }
            catch { }
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
