// Real-HKCU verification harness for DiskMasterWinUI.Services.ShellIntegrationService.
//
//   snapshot HKCU -> Install() -> verify values -> Uninstall() -> verify HKCU is byte-for-byte unchanged
//
// Usage:
//   dotnet run --project tools\DiskMasterShellHost\verify
//   dotnet run --project tools\DiskMasterShellHost\verify -- --app "C:\Program Files\DiskMaster Pro\DiskMasterWinUI.exe"
//
// Exit code 0 = HKCU was restored exactly and the registration verified; 1 = problem; 2 = refused to run.
// The harness never leaves the registration installed.
using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Services;

internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string label, string? detail = null)
    {
        Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + label + (detail == null ? "" : " — " + detail));
        if (!condition) _failures++;
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("== Real HKCU shell-integration verification ==");
        Console.WriteLine("   (must be run outside the DSH agent sandbox: that sandbox denies all HKCU writes)");

        var appExe = ResolveAppExe(args);
        var launcher = ResolveLauncher(args);
        Console.WriteLine($"  launcher = {launcher}");
        Console.WriteLine($"  app exe  = {appExe}");

        var service = new ShellIntegrationService();
        Console.WriteLine($"  manifest  = {service.ManifestPath}");
        Console.WriteLine("  registry paths used by Install():");
        foreach (var path in ShellIntegrationService.DescribeRegistryPaths()) Console.WriteLine("    " + path);

        if (service.IsInstalled())
        {
            Console.WriteLine("  ⚠ DiskMaster right-click entries are already installed; uninstall them first (Settings page or this harness with --uninstall-only).");
            if (args.Contains("--uninstall-only"))
            {
                var removed = service.Uninstall();
                Console.WriteLine("  " + removed.Headline);
                return removed.Success ? 0 : 1;
            }
            return 2;
        }

        var before = SnapshotScoped();
        var backupCountBefore = CountBackups();

        var install = service.Install(allUsers: false, launcherPath: launcher, appExePath: appExe);
        Console.WriteLine();
        Console.WriteLine("  " + install.Headline);
        foreach (var line in install.Log) Console.WriteLine("    " + line);

        Check(install.Success, "Install() reported success", install.Message);
        Check(service.IsInstalled(), "IsInstalled() == true");
        Check(install.KeysWritten.Count == 3 * 5 + 1, "15 verb values + InstallLocation written",
            install.KeysWritten.Count.ToString());

        for (int i = 0; i < ShellIntegrationService.ClassPaths.Length; i++)
        {
            var classPath = @"Software\Classes\" + ShellIntegrationService.ClassPaths[i];
            var expected = $"\"{launcher}\" \"{ShellIntegrationService.ClassPathArguments[i]}\"";
            Check(Query($@"HKCU\{classPath}\command", "") == expected, $"command [{classPath}]");
            Check(Query($@"HKCU\{classPath}", "MUIVerb") == ShellIntegrationService.MenuTextDefault, $"MUIVerb [{classPath}]");
        }
        Check(Query(@"HKCU\Software\DiskMasterPro", "InstallLocation") == launcher, "InstallLocation written");

        Console.WriteLine();
        Console.WriteLine("  -- uninstall --");
        var uninstall = service.Uninstall(allUsers: false);
        Console.WriteLine("  " + uninstall.Headline);
        foreach (var line in uninstall.Log) Console.WriteLine("    " + line);

        Check(uninstall.Success, "Uninstall() reported success", uninstall.Message);
        Check(!service.IsInstalled(), "IsInstalled() == false after uninstall");
        Check(!File.Exists(service.ManifestPath), "manifest removed");
        var after = SnapshotScoped();
        Check(before == after, "HKCU scoped state is byte-for-byte identical to before the test",
            Diff(before, after));
        Console.WriteLine($"  .reg backups created: {CountBackups() - backupCountBefore} (left in {service.GetBackupDirectory()} by design)");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "RESULT: verified — registration works and HKCU was restored exactly (nothing left installed)."
            : $"RESULT: {_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static string ResolveAppExe(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--app" or "-a") return Path.GetFullPath(args[i + 1]);
        }
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "DiskMasterWinUI.exe"),
                     @"C:\Program Files\DiskMaster Pro\DiskMasterWinUI.exe"
                 })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        Console.Error.WriteLine("找不到 DiskMasterWinUI.exe；請用 --app \"<路徑>\" 指定。");
        Environment.Exit(2);
        return "";
    }

    private static string ResolveLauncher(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--launcher" or "-l") return Path.GetFullPath(args[i + 1]);
        }
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, ShellIntegrationService.LauncherExeName),
                     Path.Combine(Directory.GetCurrentDirectory(), "tools", "DiskMasterShellHost", "bin", "Release", "net9.0-windows", ShellIntegrationService.LauncherExeName),
                     @"C:\Program Files\DiskMaster Pro\DiskMasterShellHost.exe"
                 })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        Console.Error.WriteLine("找不到 DiskMasterShellHost.exe；請用 --launcher \"<路徑>\" 指定。");
        Environment.Exit(2);
        return "";
    }

    private static readonly string[] ScopedPaths =
    {
        @"Software\Classes\Drive\shell\DiskMasterRecover",
        @"Software\Classes\Directory\shell\DiskMasterRecover",
        @"Software\Classes\Directory\Background\shell\DiskMasterRecover",
        @"Software\Classes\Drive\shell",
        @"Software\Classes\Drive",
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Directory\Background\shell",
        @"Software\Classes\Directory\Background",
        @"Software\DiskMasterPro"
    };

    private static string SnapshotScoped()
    {
        var sb = new StringBuilder();
        foreach (var path in ScopedPaths)
        {
            sb.Append("PATH ").Append(path).Append('\n');
            var (output, _, code) = RunReg($"query \"HKCU\\{path}\" /s");
            sb.Append(code == 0 ? output : "(absent)").Append('\n');
        }
        return sb.ToString();
    }

    private static string Diff(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var diffs = new List<string>();
        for (int i = 0; i < Math.Max(a.Length, b.Length) && diffs.Count < 6; i++)
        {
            var left = i < a.Length ? a[i] : "<missing>";
            var right = i < b.Length ? b[i] : "<missing>";
            if (left != right) diffs.Add($"'{left}' vs '{right}'");
        }
        return diffs.Count == 0 ? "identical" : string.Join(" ; ", diffs);
    }

    private static string? Query(string keyPath, string valueName)
    {
        var (output, _, code) = RunReg(valueName.Length == 0
            ? $"query \"{keyPath}\" /ve"
            : $"query \"{keyPath}\" /v \"{valueName}\"");
        if (code != 0) return null;
        var line = output.Split('\n').FirstOrDefault(l => l.Contains("REG_SZ", StringComparison.OrdinalIgnoreCase));
        if (line == null) return null;
        var index = line.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
        return line[(index + "REG_SZ".Length)..].Trim();
    }

    private static int CountBackups()
    {
        try
        {
            var dir = ShellIntegrationService.DefaultBackupDirectory;
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "shell-integration-*.reg").Length : 0;
        }
        catch { return 0; }
    }

    private static (string Output, string Error, int ExitCode) RunReg(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return ("", "reg.exe not started", -1);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return (stdout.Trim(), stderr.Trim(), process.HasExited ? process.ExitCode : -1);
    }
}
