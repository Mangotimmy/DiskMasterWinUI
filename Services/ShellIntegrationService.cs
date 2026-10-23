using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>Outcome of an Install/Uninstall call, with every registry path touched (for UI display and audit).</summary>
public sealed class ShellIntegrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool AllUsers { get; set; }
    public List<string> Log { get; set; } = new();
    public List<string> KeysWritten { get; set; } = new();
    public List<string> KeysRemoved { get; set; } = new();
    public List<string> Backups { get; set; } = new();

    public string Headline => (Success ? "✅ " : "⛔ ") + Message;
}

/// <summary>
/// Minimal registry backend used by <see cref="ShellIntegrationService"/>.
/// It exists so the install/uninstall logic can be exercised in a sandboxed or test environment
/// without touching the real hive; the shipped default is <see cref="WindowsRegistryStore"/>.
/// </summary>
public interface IShellRegistryStore
{
    string HiveName { get; }
    bool KeyExists(string path);
    bool IsKeyEmpty(string path);
    string? GetValue(string path, string? name);
    void SetValue(string path, string? name, string value);
    void DeleteValue(string path, string? name);
    void DeleteKeyTree(string path);

    /// <summary>Exports a key to a .reg file and returns its path (null when the export failed).</summary>
    string? ExportKey(string path, string tag);

    /// <summary>Merges a .reg file back (same semantics as <c>reg import</c>).</summary>
    void ImportKey(string backupFile);
}

/// <summary>Real per-user (HKCU) or all-users (HKLM) registry access.</summary>
public sealed class WindowsRegistryStore : IShellRegistryStore
{
    private readonly bool _allUsers;
    private readonly string _backupDirectory;

    public WindowsRegistryStore(bool allUsers, string backupDirectory)
    {
        _allUsers = allUsers;
        _backupDirectory = backupDirectory;
    }

    private RegistryKey BaseKey => _allUsers ? Registry.LocalMachine : Registry.CurrentUser;

    public string HiveName => _allUsers ? "HKLM" : "HKCU";

    public bool KeyExists(string path)
    {
        try
        {
            using var key = BaseKey.OpenSubKey(path);
            return key != null;
        }
        catch { return false; }
    }

    public bool IsKeyEmpty(string path)
    {
        try
        {
            using var key = BaseKey.OpenSubKey(path);
            if (key == null) return true;
            return key.SubKeyCount == 0 && key.ValueCount == 0;
        }
        catch { return false; }
    }

    public string? GetValue(string path, string? name)
    {
        try
        {
            using var key = BaseKey.OpenSubKey(path);
            return key?.GetValue(name ?? "") as string;
        }
        catch { return null; }
    }

    public void SetValue(string path, string? name, string value)
    {
        using var key = BaseKey.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"無法建立登錄機碼 {HiveName}\\{path}");
        key.SetValue(name ?? "", value, RegistryValueKind.String);
    }

    public void DeleteValue(string path, string? name)
    {
        using var key = BaseKey.OpenSubKey(path, writable: true);
        key?.DeleteValue(name ?? "", throwOnMissingValue: false);
    }

    public void DeleteKeyTree(string path)
    {
        try { BaseKey.DeleteSubKeyTree(path, throwOnMissingSubKey: false); } catch { }
    }

    public string? ExportKey(string path, string tag)
    {
        try
        {
            Directory.CreateDirectory(_backupDirectory);
            var safeTag = string.Concat(tag.Where(char.IsLetterOrDigit));
            var file = Path.Combine(_backupDirectory, $"shell-integration-{DateTime.Now:yyyyMMdd-HHmmss}-{safeTag}.reg");
            var (_, error, code) = RunReg($"export \"{HiveName}\\{path}\" \"{file}\" /y");
            if (code == 0 && File.Exists(file)) return file;
            Debug.WriteLine($"[ShellIntegration] reg export failed ({code}): {error}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellIntegration] reg export threw: {ex.Message}");
        }
        return null;
    }

    public void ImportKey(string backupFile)
    {
        try
        {
            var (_, error, code) = RunReg($"import \"{backupFile}\"");
            if (code != 0) Debug.WriteLine($"[ShellIntegration] reg import failed ({code}): {error}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellIntegration] reg import threw: {ex.Message}");
        }
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
        if (process == null) return ("", "reg.exe could not be started", -1);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return (stdout.Trim(), stderr.Trim(), process.HasExited ? process.ExitCode : -1);
    }
}

/// <summary>
/// Explorer right-click ("shell ex") integration for DiskMaster Pro.
///
/// The app is unpackaged (WindowsPackageType=None) and runs asInvoker, so MSIX shell extensions are
/// unavailable. Instead this registers per-user static verbs under HKCU\Software\Classes — no elevation,
/// and no files are installed anywhere:
///   HKCU\Software\Classes\Drive\shell\DiskMasterRecover\command                (clicked drive)
///   HKCU\Software\Classes\Directory\shell\DiskMasterRecover\command           (clicked folder)
///   HKCU\Software\Classes\Directory\Background\shell\DiskMasterRecover\command (folder background)
/// The command launches DiskMasterShellHost.exe with the clicked path; the launcher finds the app exe
/// through InstallLocation written here:
///   HKCU\Software\DiskMasterPro\InstallLocation   (all-users variant: HKLM\SOFTWARE\DiskMasterPro\InstallLocation)
///
/// Safety contract:
///  • Every key that already existed is exported to %LOCALAPPDATA%\DiskMaster_Portable\RegistryBackups
///    (same folder convention as SystemOptimizerService) BEFORE anything is written.
///  • A manifest next to the backup records exactly which keys/values Install() created and where the
///    backups are.
///  • Uninstall() removes exactly what the manifest says Install() added: created subtrees are deleted,
///    pre-existing subtrees are restored from their .reg backup, and only InstallLocation's own value is
///    removed when the key already existed. Empty ancestor keys that we created are cleaned up too.
/// </summary>
public sealed class ShellIntegrationService
{
    public const string MenuTextDefault = "用 DiskMaster 復原檔案 / Recover with DiskMaster";
    public const string VerbKeyName = "DiskMasterRecover";
    public const string InstallLocationValue = "InstallLocation";
    public const string PerUserProductKeyPath = @"Software\DiskMasterPro";
    public const string AllUsersProductKeyPath = @"SOFTWARE\DiskMasterPro";
    public const string LauncherExeName = "DiskMasterShellHost.exe";
    public const string AppExeName = "DiskMasterWinUI.exe";
    public const string ManifestFileName = "shell-integration-manifest.txt";

    /// <summary>The three Explorer classes we register (relative to Software\Classes).</summary>
    public static readonly string[] ClassPaths =
    {
        @"Drive\shell\" + VerbKeyName,
        @"Directory\shell\" + VerbKeyName,
        @"Directory\Background\shell\" + VerbKeyName
    };

    /// <summary>Explorer substitution variable per class: %V = drive root / folder, %1 = clicked item.</summary>
    public static readonly string[] ClassPathArguments = { "%V", "%1", "%V" };

    /// <summary>Default backup folder (same convention as SystemOptimizerService).</summary>
    public static string DefaultBackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskMaster_Portable",
        "RegistryBackups");

    private readonly Func<bool, IShellRegistryStore> _storeFactory;

    /// <summary>Folder that holds the manifest and the .reg backups of pre-existing keys.</summary>
    public string BackupDirectory { get; }

    public string ManifestPath => Path.Combine(BackupDirectory, ManifestFileName);

    public ShellIntegrationService(string? backupDirectory = null)
    {
        BackupDirectory = string.IsNullOrWhiteSpace(backupDirectory) ? DefaultBackupDirectory : backupDirectory!;
        _storeFactory = allUsers => new WindowsRegistryStore(allUsers, BackupDirectory);
        try { Directory.CreateDirectory(BackupDirectory); } catch { }
    }

    /// <summary>Testing / portable seam: run the same logic against a different registry backend and folder.</summary>
    public ShellIntegrationService(Func<bool, IShellRegistryStore> storeFactory, string backupDirectory)
    {
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        BackupDirectory = backupDirectory;
        try { Directory.CreateDirectory(BackupDirectory); } catch { }
    }

    public string GetBackupDirectory() => BackupDirectory;

    // ══════════════════════════════════════════════════════════════════════
    //  Status
    // ══════════════════════════════════════════════════════════════════════

    public bool IsInstalled(bool allUsers = false)
    {
        try
        {
            var store = _storeFactory(allUsers);
            int found = 0;
            foreach (var classPath in ClassPaths)
            {
                var value = store.GetValue(@"Software\Classes\" + classPath + @"\command", null);
                if (!string.IsNullOrWhiteSpace(value)) found++;
            }
            return found == ClassPaths.Length;
        }
        catch { return false; }
    }

    public string GetStatusText()
    {
        var sb = new StringBuilder();
        sb.Append(IsInstalled() ? "已安裝（目前使用者）" : "未安裝（目前使用者）");
        sb.Append(IsInstalled(true) ? "；已安裝（所有使用者）" : "；未安裝（所有使用者）");
        if (NeedsPathCascadeRepair()) sb.Append("；⚠ 目前使用者的「加至 PATH」子選單需重新套用");
        if (NeedsPathCascadeRepair(true)) sb.Append("；⚠ 所有使用者的「加至 PATH」子選單需重新套用");
        sb.Append(File.Exists(ManifestPath) ? $"；備份清單：{ManifestPath}" : "；無備份清單");
        return sb.ToString();
    }

    /// <summary>
    /// True when an Add-to-PATH verb exists but was written by the legacy build that stored an
    /// empty SubCommands value (Explorer then shows a chevron with no items).
    /// </summary>
    public bool NeedsPathCascadeRepair(bool allUsers = false)
    {
        try
        {
            var store = _storeFactory(allUsers);
            foreach (var classPath in ExtendedAddToPathClassPaths)
            {
                var rel = @"Software\Classes\" + classPath;
                if (!store.KeyExists(rel)) continue;
                if (string.IsNullOrWhiteSpace(store.GetValue(rel, "ExtendedSubCommandsKey"))) return true;
                if (string.IsNullOrWhiteSpace(store.GetValue(rel + @"\shell\UserPath\command", null))) return true;
                if (string.IsNullOrWhiteSpace(store.GetValue(rel + @"\shell\SystemPath\command", null))) return true;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>Registry paths this service uses — for documentation and installer scripts.</summary>
    public static IReadOnlyList<string> DescribeRegistryPaths(bool allUsers = false)
    {
        var hive = allUsers ? "HKLM" : "HKCU";
        var list = new List<string>();
        foreach (var classPath in ClassPaths)
        {
            list.Add($@"{hive}\Software\Classes\{classPath}");
            list.Add($@"{hive}\Software\Classes\{classPath}\command");
        }
        list.Add($@"{hive}\{(allUsers ? AllUsersProductKeyPath : PerUserProductKeyPath)}\{InstallLocationValue}");
        return list;
    }

    public const string VerbNtfsKeyName = "DiskMasterNtfs";
    public const string VerbShareKeyName = "DiskMasterShare";
    public const string VerbImageKeyName = "DiskMasterImage";
    public const string VerbDiskCheckKeyName = "DiskMasterDiskCheck";

    public static readonly string[] ExtendedNtfsClassPaths =
    {
        @"Directory\shell\" + VerbNtfsKeyName,
        @"Directory\Background\shell\" + VerbNtfsKeyName,
        @"*\shell\" + VerbNtfsKeyName
    };

    public static readonly string[] ExtendedShareClassPaths =
    {
        @"Directory\shell\" + VerbShareKeyName,
        @"Directory\Background\shell\" + VerbShareKeyName
    };

    public static readonly string[] ExtendedImageClassPaths =
    {
        @"SystemFileAssociations\.wim\shell\" + VerbImageKeyName,
        @"SystemFileAssociations\.esd\shell\" + VerbImageKeyName,
        @"SystemFileAssociations\.iso\shell\" + VerbImageKeyName,
        @"SystemFileAssociations\.vhd\shell\" + VerbImageKeyName,
        @"SystemFileAssociations\.vhdx\shell\" + VerbImageKeyName
    };

    public static readonly string[] ExtendedDiskCheckClassPaths =
    {
        @"Drive\shell\" + VerbDiskCheckKeyName
    };

    public const string VerbAddToPathKeyName = "DiskMasterAddToPath";

    public static readonly string[] ExtendedAddToPathClassPaths =
    {
        @"Directory\shell\" + VerbAddToPathKeyName,
        @"Directory\Background\shell\" + VerbAddToPathKeyName,
        @"Drive\shell\" + VerbAddToPathKeyName
    };

    public bool IsExtendedVerbInstalled(string firstClassPath, bool allUsers = false)
    {
        try
        {
            var store = _storeFactory(allUsers);
            var val = store.GetValue(@"Software\Classes\" + firstClassPath + @"\command", null)
                   ?? store.GetValue(@"Software\Classes\" + firstClassPath + @"\shell\UserPath\command", null)
                   ?? store.GetValue(@"Software\Classes\" + firstClassPath, "ExtendedSubCommandsKey");
            return !string.IsNullOrWhiteSpace(val);
        }
        catch { return false; }
    }

    public ShellIntegrationResult InstallExtended(
        bool allUsers = false,
        bool enableRecovery = true,
        bool enableNtfs = true,
        bool enableLanShare = true,
        bool enableImageMount = true,
        bool enableDiskCheck = true,
        bool enableAddToPath = true)
    {
        var result = new ShellIntegrationResult { AllUsers = allUsers };
        if (allUsers && !IsElevated())
        {
            result.Success = false;
            result.Message = "註冊「所有使用者 (HKLM)」右鍵選單需要系統管理員權限；請改選「目前使用者 (HKCU 免提權)」或以管理員身分重啟。";
            return result;
        }

        var appExe = ResolveExistingFile(Environment.ProcessPath)
                     ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, AppExeName));
        if (appExe == null)
        {
            result.Success = false;
            result.Message = $"找不到 {AppExeName}，無法註冊右鍵選單。";
            return result;
        }

        var launcher = ResolveLauncherOrApp(null, appExe);
        var store = _storeFactory(allUsers);

        try
        {
            // 1. Recovery verb (uses standard Install or removes if unchecked)
            if (enableRecovery)
            {
                var recRes = Install(allUsers, launcherPath: launcher, appExePath: appExe);
                result.KeysWritten.AddRange(recRes.KeysWritten);
                result.Log.AddRange(recRes.Log);
            }
            else
            {
                foreach (var cp in ClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            // 2. NTFS Permissions & Ownership verb
            if (enableNtfs)
            {
                var args = new[] { "%1", "%V", "%1" };
                for (int i = 0; i < ExtendedNtfsClassPaths.Length; i++)
                {
                    var rel = @"Software\Classes\" + ExtendedNtfsClassPaths[i];
                    WriteValue(store, rel, null, "DiskMaster NTFS Permissions", result);
                    WriteValue(store, rel, "MUIVerb", "🛡️ DiskMaster: NTFS 權限與取得所有權", result);
                    WriteValue(store, rel, "Icon", $"{appExe},0", result);
                    WriteValue(store, rel, "Position", "Bottom", result);
                    WriteValue(store, rel + @"\command", null, $"\"{appExe}\" --tab NtfsPermissions --path \"{args[i]}\"", result);
                }
            }
            else
            {
                foreach (var cp in ExtendedNtfsClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            // 3. Quick LAN Share verb
            if (enableLanShare)
            {
                var args = new[] { "%1", "%V" };
                for (int i = 0; i < ExtendedShareClassPaths.Length; i++)
                {
                    var rel = @"Software\Classes\" + ExtendedShareClassPaths[i];
                    WriteValue(store, rel, null, "DiskMaster Quick LAN Share", result);
                    WriteValue(store, rel, "MUIVerb", "🖧 DiskMaster: 快速建立區網共用資料夾", result);
                    WriteValue(store, rel, "Icon", $"{appExe},0", result);
                    WriteValue(store, rel, "Position", "Bottom", result);
                    WriteValue(store, rel + @"\command", null, $"\"{appExe}\" --tab NetworkTools --share \"{args[i]}\"", result);
                }
            }
            else
            {
                foreach (var cp in ExtendedShareClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            // 4. WIM / ESD / ISO / VHD / VHDX Mount & Deploy verb
            if (enableImageMount)
            {
                foreach (var cp in ExtendedImageClassPaths)
                {
                    var rel = @"Software\Classes\" + cp;
                    WriteValue(store, rel, null, "DiskMaster Mount or Deploy Image", result);
                    WriteValue(store, rel, "MUIVerb", "💿 DiskMaster: 掛載 / 部署系統與虛擬磁碟映像", result);
                    WriteValue(store, rel, "Icon", $"{appExe},0", result);
                    WriteValue(store, rel, "Position", "Bottom", result);
                    WriteValue(store, rel + @"\command", null, $"\"{appExe}\" --tab WimDeploy --image \"%1\"", result);
                }
            }
            else
            {
                foreach (var cp in ExtendedImageClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            // 5. Drive S.M.A.R.T. & Disk Check verb
            if (enableDiskCheck)
            {
                foreach (var cp in ExtendedDiskCheckClassPaths)
                {
                    var rel = @"Software\Classes\" + cp;
                    WriteValue(store, rel, null, "DiskMaster Disk Health Check", result);
                    WriteValue(store, rel, "MUIVerb", "💾 DiskMaster: 磁碟健康與環境檢測", result);
                    WriteValue(store, rel, "Icon", $"{appExe},0", result);
                    WriteValue(store, rel, "Position", "Bottom", result);
                    WriteValue(store, rel + @"\command", null, $"\"{appExe}\" --tab DiskTools --drive \"%V\"", result);
                }
            }
            else
            {
                foreach (var cp in ExtendedDiskCheckClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            // 6. Add to PATH Environment verb (Cascaded submenu: User PATH & System PATH)
            if (enableAddToPath)
            {
                var args = new[] { "%1", "%V", "%V" };
                for (int i = 0; i < ExtendedAddToPathClassPaths.Length; i++)
                {
                    var rel = @"Software\Classes\" + ExtendedAddToPathClassPaths[i];

                    // Cascade contract (Windows 7+): ExtendedSubCommandsKey points at this verb's own
                    // key and the submenu is built from its shell\* children. An empty SubCommands
                    // value renders an empty cascade, and a non-empty (Default) value turns the parent
                    // into a command. Clear both so re-applying repairs a 1.6.3 registration.
                    RemoveValue(store, rel, "", result);
                    RemoveValue(store, rel, "SubCommands", result);
                    WriteValue(store, rel, "MUIVerb", "🌐 DiskMaster: 加至 PATH 環境變數", result);
                    WriteValue(store, rel, "Icon", $"{appExe},0", result);
                    WriteValue(store, rel, "Position", "Bottom", result);
                    WriteValue(store, rel, "ExtendedSubCommandsKey", ExtendedAddToPathClassPaths[i], result);

                    // Subcommand 1: User PATH
                    var userSub = rel + @"\shell\UserPath";
                    WriteValue(store, userSub, null, "Add to User PATH", result);
                    WriteValue(store, userSub, "MUIVerb", "👤 加入使用者 PATH (User PATH)", result);
                    WriteValue(store, userSub, "Icon", $"{appExe},0", result);
                    WriteValue(store, userSub + @"\command", null, $"\"{appExe}\" --add-path-user \"{args[i]}\"", result);

                    // Subcommand 2: System PATH
                    var sysSub = rel + @"\shell\SystemPath";
                    WriteValue(store, sysSub, null, "Add to System PATH", result);
                    WriteValue(store, sysSub, "MUIVerb", "🛡️ 加入系統 PATH (System PATH, 需管理員)", result);
                    WriteValue(store, sysSub, "Icon", $"{appExe},0", result);
                    WriteValue(store, sysSub, "HasLUAShield", "", result);
                    WriteValue(store, sysSub + @"\command", null, $"\"{appExe}\" --add-path-system \"{args[i]}\"", result);
                }
            }
            else
            {
                foreach (var cp in ExtendedAddToPathClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, result);
            }

            result.Success = true;
            result.Message = $"已成功套用 Windows 檔案總管右鍵選單整合（{(allUsers ? "所有使用者 HKLM" : "目前使用者 HKCU")}，共寫入 {result.KeysWritten.Count} 項設定）。";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"套用右鍵選單失敗：{ex.Message}";
            return result;
        }
    }

    public ShellIntegrationResult UninstallAllExtended(bool allUsers = false)
    {
        var res = Uninstall(allUsers);
        try
        {
            var store = _storeFactory(allUsers);
            foreach (var cp in ExtendedNtfsClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, res);
            foreach (var cp in ExtendedShareClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, res);
            foreach (var cp in ExtendedImageClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, res);
            foreach (var cp in ExtendedDiskCheckClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, res);
            foreach (var cp in ExtendedAddToPathClassPaths) DeleteKeyTree(store, @"Software\Classes\" + cp, res);
        }
        catch { }
        return res;
    }

    private static string ResolveLauncherOrApp(string? launcherPath, string? appExeFallback)
    {
        return ResolveExistingFile(launcherPath)
               ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, LauncherExeName))
               ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, "tools", "DiskMasterShellHost", "bin", "Release", "net9.0-windows", LauncherExeName))
               ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, "tools", "DiskMasterShellHost", "bin", "Debug", "net9.0-windows", LauncherExeName))
               ?? appExeFallback
               ?? "";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Install
    // ══════════════════════════════════════════════════════════════════════

    public ShellIntegrationResult Install(bool allUsers = false, string? launcherPath = null, string? appExePath = null, string? menuText = null)
    {
        var result = new ShellIntegrationResult { AllUsers = allUsers };

        if (allUsers && !IsElevated())
        {
            result.Success = false;
            result.Message = "註冊「所有使用者」右鍵選單需要系統管理員權限；請改用以目前使用者安裝（免提權），或以管理員身分重新啟動。";
            return result;
        }

        var appExe = ResolveExistingFile(appExePath)
                     ?? ResolveExistingFile(Environment.ProcessPath)
                     ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, AppExeName));

        var launcher = ResolveExistingFile(launcherPath)
                       ?? ResolveExistingFile(Path.Combine(AppContext.BaseDirectory, LauncherExeName))
                       ?? (launcherPath == null ? appExe : null);
        if (launcher == null)
        {
            result.Success = false;
            result.Message = $"找不到 {LauncherExeName}。請將它與 {AppExeName} 放在同一個資料夾（或傳入 launcherPath），再重新註冊。";
            return result;
        }

        if (appExe == null)
        {
            result.Success = false;
            result.Message = $"找不到 {AppExeName}，無法設定圖示與安裝位置。";
            return result;
        }

        var label = string.IsNullOrWhiteSpace(menuText) ? MenuTextDefault : menuText!;
        var store = _storeFactory(allUsers);
        var productKeyPath = allUsers ? AllUsersProductKeyPath : PerUserProductKeyPath;

        var manifest = new List<string>
        {
            "version=1",
            $"scope={(allUsers ? "allusers" : "peruser")}",
            $"created={DateTime.Now:O}",
            $"launcher={Esc(launcher)}",
            $"app={Esc(appExe)}",
            $"menutext={Esc(label)}"
        };

        try
        {
            Directory.CreateDirectory(BackupDirectory);

            // 1. InstallLocation so the launcher can find the app (backed up when it already exists).
            var productExisting = store.KeyExists(productKeyPath);
            var productBackup = productExisting ? store.ExportKey(productKeyPath, "install-location") : null;
            if (productBackup != null)
            {
                result.Backups.Add($@"{store.HiveName}\{productKeyPath} -> {productBackup}");
                result.Log.Add($@"BACKUP {store.HiveName}\{productKeyPath} -> {productBackup}");
            }
            manifest.Add($"installkey={Esc(productKeyPath)}|pre={(productExisting ? 1 : 0)}|backup={Esc(productBackup ?? "-")}");
            WriteValue(store, productKeyPath, InstallLocationValue, launcher, result);

            // 2. The three verbs.
            for (int i = 0; i < ClassPaths.Length; i++)
            {
                var relative = @"Software\Classes\" + ClassPaths[i];

                var preExisting = store.KeyExists(relative);
                var backup = preExisting ? store.ExportKey(relative, "verb-" + i) : null;
                if (backup != null)
                {
                    result.Backups.Add($@"{store.HiveName}\{relative} -> {backup}");
                    result.Log.Add($@"BACKUP {store.HiveName}\{relative} -> {backup}");
                }
                manifest.Add($"key={Esc(relative)}|pre={(preExisting ? 1 : 0)}|backup={Esc(backup ?? "-")}");

                foreach (var ancestor in AncestorChain(relative))
                {
                    manifest.Add($"ancestor={Esc(ancestor)}|pre={(store.KeyExists(ancestor) ? 1 : 0)}");
                }

                // (Default) is the label fallback for shells that ignore MUIVerb.
                WriteValue(store, relative, null, "Recover with DiskMaster", result);
                WriteValue(store, relative, "MUIVerb", label, result);
                WriteValue(store, relative, "Icon", $"{appExe},0", result);
                WriteValue(store, relative, "Position", "Bottom", result);
                WriteValue(store, relative + @"\command", null, $"\"{launcher}\" \"{ClassPathArguments[i]}\"", result);
            }

            File.WriteAllLines(ManifestPath, manifest, new UTF8Encoding(false));
            result.Log.Add($"MANIFEST {ManifestPath}");
            result.Success = true;
            result.Message = $"已註冊檔案總管右鍵選單（{(allUsers ? "所有使用者" : "目前使用者")}）：磁碟機、資料夾、資料夾背景。" +
                             $" 啟動器：{launcher}；備份：{BackupDirectory}";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"註冊失敗：{ex.Message}";
            result.Log.Add("ERROR " + ex);
            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Uninstall
    // ══════════════════════════════════════════════════════════════════════

    public ShellIntegrationResult Uninstall(bool allUsers = false)
    {
        var result = new ShellIntegrationResult { AllUsers = allUsers };

        if (allUsers && !IsElevated())
        {
            result.Success = false;
            result.Message = "移除「所有使用者」右鍵選單需要系統管理員權限。";
            return result;
        }

        var store = _storeFactory(allUsers);

        try
        {
            if (!File.Exists(ManifestPath))
            {
                // No manifest: remove only the keys this app owns, and say so.
                result.Log.Add("MANIFEST missing — removing only the known DiskMaster verb keys.");
                foreach (var classPath in ClassPaths) DeleteKeyTree(store, @"Software\Classes\" + classPath, result);
                RemoveValue(store, allUsers ? AllUsersProductKeyPath : PerUserProductKeyPath, InstallLocationValue, result);
                result.Success = true;
                result.Message = "找不到備份清單（可能不是由本程式安裝），已僅移除 DiskMaster 自己的右鍵選單項目。";
                return result;
            }

            var keys = new List<(string Path, bool PreExisting, string? Backup)>();
            var installKeys = new List<(string Path, bool PreExisting, string? Backup)>();
            var ancestors = new List<(string Path, bool PreExisting)>();

            foreach (var line in File.ReadAllLines(ManifestPath))
            {
                if (line.StartsWith("key=", StringComparison.Ordinal))
                {
                    var (path, pre, backup) = ParseEntry(line[4..]);
                    if (!string.IsNullOrEmpty(path)) keys.Add((path, pre, backup));
                }
                else if (line.StartsWith("ancestor=", StringComparison.Ordinal))
                {
                    var (path, pre, _) = ParseEntry(line[9..]);
                    if (!string.IsNullOrEmpty(path)) ancestors.Add((path, pre));
                }
                else if (line.StartsWith("installkey=", StringComparison.Ordinal))
                {
                    var (path, pre, backup) = ParseEntry(line[11..]);
                    if (!string.IsNullOrEmpty(path)) installKeys.Add((path, pre, backup));
                }
            }

            foreach (var (path, preExisting, backup) in keys)
            {
                DeleteKeyTree(store, path, result);
                if (preExisting && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    store.ImportKey(backup);
                    result.Log.Add($"RESTORE {backup}");
                }
            }

            foreach (var (path, preExisting, backup) in installKeys)
            {
                if (preExisting)
                {
                    RemoveValue(store, path, InstallLocationValue, result);
                    if (!string.IsNullOrEmpty(backup) && File.Exists(backup))
                    {
                        store.ImportKey(backup);
                        result.Log.Add($"RESTORE {backup}");
                    }
                }
                else
                {
                    DeleteKeyTree(store, path, result);
                }
            }

            // Remove ancestor keys that Install() created and that are now empty (deepest first).
            foreach (var (path, preExisting) in ancestors.OrderByDescending(a => a.Path.Length))
            {
                if (preExisting) continue;
                if (!store.KeyExists(path)) continue;
                if (!store.IsKeyEmpty(path)) continue;
                DeleteKeyTree(store, path, result);
            }

            try { File.Delete(ManifestPath); result.Log.Add($"DELETE manifest {ManifestPath}"); } catch { }

            result.Success = true;
            result.Message = $"已移除檔案總管右鍵選單（{(allUsers ? "所有使用者" : "目前使用者")}）；共移除 {result.KeysRemoved.Count} 個登錄機碼，registry 已還原為安裝前狀態。";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"移除失敗：{ex.Message}";
            result.Log.Add("ERROR " + ex);
            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Registry plumbing
    // ══════════════════════════════════════════════════════════════════════

    private static void WriteValue(IShellRegistryStore store, string path, string? name, string value, ShellIntegrationResult result)
    {
        store.SetValue(path, name, value);
        var display = $@"{store.HiveName}\{path} :: {(name ?? "(Default)")} = {value}";
        result.KeysWritten.Add(display);
        result.Log.Add("VALUE  " + display);
    }

    private static void RemoveValue(IShellRegistryStore store, string path, string name, ShellIntegrationResult result)
    {
        try
        {
            if (store.GetValue(path, name) == null) return;
            store.DeleteValue(path, name);
            var display = $@"{store.HiveName}\{path} :: {name}";
            result.KeysRemoved.Add(display);
            result.Log.Add("UNSET  " + display);
        }
        catch (Exception ex)
        {
            result.Log.Add($"WARN   could not remove value {name} at {path}: {ex.Message}");
        }
    }

    private static void DeleteKeyTree(IShellRegistryStore store, string path, ShellIntegrationResult result)
    {
        try
        {
            if (!store.KeyExists(path)) return;
            store.DeleteKeyTree(path);
            var display = $@"{store.HiveName}\{path}";
            result.KeysRemoved.Add(display);
            result.Log.Add("DELETE " + display);
        }
        catch (Exception ex)
        {
            result.Log.Add($"WARN   could not delete key {path}: {ex.Message}");
        }
    }

    /// <summary>Ancestor keys that CreateSubKey may have created implicitly (excluding Software\Classes itself).</summary>
    private static IEnumerable<string> AncestorChain(string keyPath)
    {
        var parts = keyPath.Split('\\');
        for (int i = 1; i < parts.Length; i++)
        {
            var candidate = string.Join('\\', parts.Take(i));
            if (candidate.Equals(@"Software", StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.Equals(@"Software\Classes", StringComparison.OrdinalIgnoreCase)) continue;
            yield return candidate;
        }
    }

    private static string? ResolveExistingFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var candidate = path.Trim().Trim('"');
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
        catch { return null; }
    }

    private static string Esc(string value) => value.Replace("|", "%7C").Replace("\r", " ").Replace("\n", " ");

    private static string Unesc(string value) => value.Replace("%7C", "|");

    private static (string Path, bool PreExisting, string? Backup) ParseEntry(string entry)
    {
        var parts = entry.Split('|');
        var path = Unesc(parts.Length > 0 ? parts[0] : "");
        bool pre = false;
        string? backup = null;
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("pre=", StringComparison.Ordinal)) pre = part[4..] == "1";
            else if (part.StartsWith("backup=", StringComparison.Ordinal))
            {
                var value = Unesc(part[7..]);
                backup = value == "-" ? null : value;
            }
        }
        return (path, pre, backup);
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
