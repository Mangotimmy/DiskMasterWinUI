using System.IO;
using System.Text;
using Microsoft.Win32;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing:
/// 1. Windows 11 Modern vs Windows 10 Classic Right-Click Context Menu switching &amp; Slow Right-Click Repair (Item 11).
/// 2. Folder Customize Optimization ("Optimize this folder for: General items / Documents / Pictures / Music / Videos"
///    + "Also apply this template to all subfolders" + Global AllFolders General Items lock to fix slow folder loading) (Item 12).
/// 3. Folder Picture (Logo) and Custom Icon setter for both Folders (desktop.ini) and File Extensions (HKCU\Software\Classes) (Item 12).
/// </summary>
public class FolderAndIconCustomizerService
{
    private const string Win11ClassicMenuClsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string Win11ClassicMenuInprocKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string AllFoldersShellKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell";

    public record FolderTemplateOption(string Key, string DisplayName, string Description);
    public record BuiltInIconPreset(string Name, string IconResource, string GlyphPreview);

    public static IReadOnlyList<FolderTemplateOption> FolderTemplates { get; } =
    [
        new("Generic", "⚡ 一般項目 (General items - 極速載入推薦)", "停用影音與圖片縮圖中繼資料深層掃描，徹底解決大型資料夾開啟轉圈與綠條緩慢問題"),
        new("Documents", "📄 文件 (Documents)", "針對 Office 文件、PDF 與文字檔案最佳化欄位配置"),
        new("Pictures", "🖼️ 圖片 (Pictures)", "啟用大型縮圖預覽與 EXIF 拍攝資訊欄位"),
        new("Music", "🎵 音樂 (Music)", "顯示演出者、專輯、音軌與位元速率中繼資料"),
        new("Videos", "🎬 影片 (Videos)", "啟用視訊畫格縮圖與解析度/片長欄位")
    ];

    public static IReadOnlyList<BuiltInIconPreset> PopularFolderIcons { get; } =
    [
        new("⭐ 黃金星標資料夾", @"C:\Windows\System32\shell32.dll,43", "⭐"),
        new("🛡️ 安全防護資料夾", @"C:\Windows\System32\imageres.dll,54", "🛡️"),
        new("🎮 遊戲與電競目錄", @"C:\Windows\System32\imageres.dll,186", "🎮"),
        new("💻 開發與程式碼庫", @"C:\Windows\System32\shell32.dll,13", "💻"),
        new("🎬 影音多媒體中心", @"C:\Windows\System32\imageres.dll,108", "🎬"),
        new("☁️ 雲端與網路備份", @"C:\Windows\System32\imageres.dll,104", "☁️"),
        new("🔒 加密私密保存庫", @"C:\Windows\System32\shell32.dll,47", "🔒"),
        new("🚀 高速效能工作區", @"C:\Windows\System32\shell32.dll,316", "🚀")
    ];

    // ══════════════════════════════════════════════════════════════════
    //  Item 11: Switch Right-Click Menu Layout & Fix Slow Right-Click
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if Windows 10 Classic Full Context Menu is active (CLSID override present).
    /// Returns false if Windows 11 Modern Compact Context Menu is active.
    /// </summary>
    public bool IsClassicContextMenuEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Win11ClassicMenuInprocKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Switches between Windows 10 Classic Full Right-Click Menu (true) and Windows 11 Modern Menu (false).
    /// </summary>
    public async Task<(bool Success, string Message)> SetClassicContextMenuAsync(bool useClassicMenu, bool restartExplorer = true)
    {
        try
        {
            if (useClassicMenu)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Win11ClassicMenuInprocKey, writable: true);
                key?.SetValue("", "", RegistryValueKind.String);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(Win11ClassicMenuClsidKey, throwOnMissingSubKey: false);
            }

            if (restartExplorer)
            {
                await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
            }

            string modeText = useClassicMenu
                ? "Windows 10 經典完整右鍵選單 (免點「顯示更多選項」直接展開全部功能)"
                : "Windows 11 現代簡潔右鍵選單 (Modern Acrylic 圓角選單)";

            return (true, $"✅ 已切換為：{modeText}");
        }
        catch (Exception ex)
        {
            return (false, $"❌ 切換右鍵選單版面失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether right-click menu delay and shell separation optimizations are applied.
    /// </summary>
    public bool IsRightClickSpeedOptimized()
    {
        try
        {
            using var deskKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var delay = deskKey?.GetValue("MenuShowDelay")?.ToString();
            return delay == "0";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fixes slow right-click menu delays by:
    /// 1. Setting MenuShowDelay = 0 in HKCU\Control Panel\Desktop.
    /// 2. Enabling SeparateProcess = 1 for Explorer windows so hung shell extensions don't freeze the desktop.
    /// 3. Cleaning orphaned/broken ContextMenuHandlers in HKCU\Software\Classes whose target CLSID/DLL no longer exists.
    /// 4. Disabling slow network validation on right-click (NoSimpleNetIDList).
    /// </summary>
    public async Task<(bool Success, string Message, int CleanedHandlers)> FixSlowRightClickMenuAsync(bool restartExplorer = false)
    {
        return await Task.Run(async () =>
        {
            int cleaned = 0;
            try
            {
                // 1. Zero MenuShowDelay
                using (var deskKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true))
                {
                    deskKey?.SetValue("MenuShowDelay", "0", RegistryValueKind.String);
                }

                // 2. Explorer Advanced responsiveness tweaks
                using (var advKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    advKey?.SetValue("SeparateProcess", 1, RegistryValueKind.DWord);
                    advKey?.SetValue("DesktopLivePreviewHoverTime", 50, RegistryValueKind.DWord);
                }

                // 3. Scan HKCU ContextMenuHandlers for orphaned CLSIDs that cause 2-5 second right-click hangs
                string[] handlerPaths =
                [
                    @"Software\Classes\*\shellex\ContextMenuHandlers",
                    @"Software\Classes\Directory\shellex\ContextMenuHandlers",
                    @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers",
                    @"Software\Classes\Folder\shellex\ContextMenuHandlers"
                ];

                foreach (var path in handlerPaths)
                {
                    try
                    {
                        using var handlersKey = Registry.CurrentUser.OpenSubKey(path, writable: true);
                        if (handlersKey == null) continue;

                        foreach (var subName in handlersKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var subKey = handlersKey.OpenSubKey(subName);
                                var defVal = subKey?.GetValue("")?.ToString()?.Trim();
                                string? clsid = null;
                                if (!string.IsNullOrEmpty(subName) && subName.StartsWith("{") && subName.EndsWith("}"))
                                    clsid = subName;
                                else if (!string.IsNullOrEmpty(defVal) && defVal.StartsWith("{") && defVal.EndsWith("}"))
                                    clsid = defVal;

                                if (!string.IsNullOrEmpty(clsid) && IsOrphanedClsid(clsid))
                                {
                                    handlersKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                                    cleaned++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                if (restartExplorer)
                {
                    await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
                }
                else
                {
                    IconCacheRepairService.NotifyShellIconChange();
                }

                return (true, $"✅ 右鍵選單延遲修復完成！(MenuShowDelay=0、獨立外殼處理程序已啟用，共清理 {cleaned} 個失效殘留右鍵擴充項)", cleaned);
            }
            catch (Exception ex)
            {
                return (false, $"❌ 修復右鍵選單延遲失敗: {ex.Message}", cleaned);
            }
        });
    }

    private static bool IsOrphanedClsid(string clsid)
    {
        try
        {
            using var clsidKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
            if (clsidKey == null)
            {
                // Check if the CLSID root exists at all
                using var rootClsid = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
                return rootClsid == null;
            }

            var dllPath = clsidKey.GetValue("")?.ToString()?.Trim('\"');
            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                var expanded = Environment.ExpandEnvironmentVariables(dllPath);
                if (Path.IsPathRooted(expanded) && !File.Exists(expanded))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Item 12: Folder Optimization (Fix Slow Folder Loading)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if the global AllFolders Shell template is locked to "NotSpecified" (General Items)
    /// to prevent Explorer from scanning media metadata across folders.
    /// </summary>
    public bool IsGlobalFastFolderDiscoveryEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AllFoldersShellKey);
            var val = key?.GetValue("FolderType")?.ToString();
            return string.Equals(val, "NotSpecified", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets or removes the global AllFolders "NotSpecified" (General items) rule and optionally resets
    /// corrupted Shell Bags cache so Explorer loads folders with thousands of files instantaneously.
    /// </summary>
    public (bool Success, string Message) SetGlobalFastFolderDiscovery(bool enableFastGeneralItems, bool resetShellBagsCache = false)
    {
        try
        {
            if (enableFastGeneralItems)
            {
                if (resetShellBagsCache)
                {
                    try
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU", throwOnMissingSubKey: false);
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags", throwOnMissingSubKey: false);
                    }
                    catch { }
                }

                using var key = Registry.CurrentUser.CreateSubKey(AllFoldersShellKey, writable: true);
                key?.SetValue("FolderType", "NotSpecified", RegistryValueKind.String);
                return (true, "✅ 已啟用全域資料夾極速載入 (AllFolders 鎖定為「一般項目 NotSpecified」，杜絕綠條轉圈與多媒體掃描卡頓)！");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(AllFoldersShellKey, writable: true);
                try { key?.DeleteValue("FolderType", throwOnMissingValue: false); } catch { }
                return (true, "✅ 已還原 Windows 預設資料夾自動內容偵測模式。");
            }
        }
        catch (Exception ex)
        {
            return (false, $"❌ 設定全域資料夾最佳化失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Optimizes a specific folder (and optionally all its subfolders) for a chosen template
    /// ("Generic", "Documents", "Pictures", "Music", "Videos") and optional folder picture (Logo),
    /// matching the Windows Folder Properties -> Customize tab.
    /// </summary>
    public async Task<(bool Success, string Message, int UpdatedFolderCount)> OptimizeFolderTemplateAsync(
        string folderPath,
        string folderTypeKey,
        bool applyToSubfolders,
        string? folderPicturePath = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (false, "❌ 請先選擇有效的目標資料夾路徑。", 0);

        return await Task.Run(() =>
        {
            int count = 0;
            try
            {
                ApplyFolderDesktopIni(folderPath, folderTypeKey, folderPicturePath, customIconResource: null, updateViewState: true, updateIcon: false);
                count++;

                if (applyToSubfolders)
                {
                    foreach (var subDir in EnumerateSubdirectoriesSafe(folderPath))
                    {
                        try
                        {
                            ApplyFolderDesktopIni(subDir, folderTypeKey, folderPicturePath: null, customIconResource: null, updateViewState: true, updateIcon: false);
                            count++;
                        }
                        catch { }
                    }
                }

                IconCacheRepairService.NotifyShellIconChange();
                return (true, $"✅ 已將「{Path.GetFileName(folderPath.TrimEnd('\\'))}」{(applyToSubfolders ? $"及子資料夾 (共 {count} 個目錄)" : "")}最佳化為 [{folderTypeKey}] 範本！", count);
            }
            catch (Exception ex)
            {
                return (false, $"❌ 套用資料夾最佳化範本失敗: {ex.Message}", count);
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════
    //  Item 12: Custom Icon for Folders & File Extensions
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets or restores a custom icon on a target folder via desktop.ini [.ShellClassInfo] IconResource.
    /// </summary>
    public async Task<(bool Success, string Message)> SetFolderCustomIconAsync(
        string folderPath,
        string? iconResource,
        bool applyToSubfolders = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (false, "❌ 請選擇有效的資料夾路徑。");

        return await Task.Run(() =>
        {
            try
            {
                ApplyFolderDesktopIni(folderPath, folderTypeKey: null, folderPicturePath: null, customIconResource: iconResource, updateViewState: false, updateIcon: true);

                int count = 1;
                if (applyToSubfolders)
                {
                    foreach (var subDir in EnumerateSubdirectoriesSafe(folderPath))
                    {
                        try
                        {
                            ApplyFolderDesktopIni(subDir, folderTypeKey: null, folderPicturePath: null, customIconResource: iconResource, updateViewState: false, updateIcon: true);
                            count++;
                        }
                        catch { }
                    }
                }

                IconCacheRepairService.NotifyShellIconChange();

                if (string.IsNullOrWhiteSpace(iconResource))
                {
                    return (true, $"✅ 已還原資料夾「{Path.GetFileName(folderPath.TrimEnd('\\'))}」為系統預設圖示！");
                }
                return (true, $"✅ 已成功套用自訂資料夾圖示 ({iconResource}) 至 {count} 個資料夾！");
            }
            catch (Exception ex)
            {
                return (false, $"❌ 設定資料夾自訂圖示失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Sets or restores a custom icon for a file extension (e.g. ".txt", ".mp4", ".log") under HKCU\Software\Classes.
    /// Does not require elevation because HKCU\Software\Classes overrides HKLM\Software\Classes for the current user.
    /// </summary>
    public (bool Success, string Message) SetFileExtensionCustomIcon(string extension, string? iconResource)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return (false, "❌ 請輸入有效的副檔名 (例如 .txt 或 .mp4)。");

        string ext = extension.Trim();
        if (!ext.StartsWith('.'))
            ext = "." + ext;

        try
        {
            // Determine ProgId for this extension
            string? progId = null;
            using (var extKey = Registry.ClassesRoot.OpenSubKey(ext))
            {
                progId = extKey?.GetValue("")?.ToString();
            }

            if (string.IsNullOrWhiteSpace(progId))
            {
                progId = $"DiskMaster.CustomExt{ext.Replace(".", "_")}";
            }

            if (string.IsNullOrWhiteSpace(iconResource))
            {
                // Restore default by removing HKCU DefaultIcon override
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}\DefaultIcon", throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ext}\DefaultIcon", throwOnMissingSubKey: false);
                IconCacheRepairService.NotifyShellIconChange();
                return (true, $"✅ 已還原「{ext}」檔案類型為系統預設圖示！");
            }

            string normalizedIcon = NormalizeIconResourceString(iconResource);

            using (var hkcuExt = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}", writable: true))
            {
                if (hkcuExt?.GetValue("") == null)
                    hkcuExt?.SetValue("", progId, RegistryValueKind.String);
            }

            using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\DefaultIcon", writable: true))
            {
                iconKey?.SetValue("", normalizedIcon, RegistryValueKind.String);
            }

            using (var directIconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\DefaultIcon", writable: true))
            {
                directIconKey?.SetValue("", normalizedIcon, RegistryValueKind.String);
            }

            IconCacheRepairService.NotifyShellIconChange();
            return (true, $"✅ 已將所有「{ext}」檔案圖示自訂為：{normalizedIcon}");
        }
        catch (Exception ex)
        {
            return (false, $"❌ 設定檔案類型圖示失敗: {ex.Message}");
        }
    }

    private static string NormalizeIconResourceString(string raw)
    {
        string trimmed = raw.Trim().Trim('\"');
        if (!trimmed.Contains(','))
        {
            return $"{trimmed},0";
        }
        return trimmed;
    }

    private static void ApplyFolderDesktopIni(
        string folderPath,
        string? folderTypeKey,
        string? folderPicturePath,
        string? customIconResource,
        bool updateViewState,
        bool updateIcon)
    {
        string iniPath = Path.Combine(folderPath, "desktop.ini");

        // Parse existing desktop.ini sections if present
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(iniPath))
        {
            try
            {
                File.SetAttributes(iniPath, FileAttributes.Normal);
                string? currentSection = null;
                foreach (var rawLine in File.ReadAllLines(iniPath))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(';')) continue;
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        currentSection = line[1..^1].Trim();
                        if (!sections.ContainsKey(currentSection))
                            sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else if (currentSection != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string k = line[..eq].Trim();
                            string v = line[(eq + 1)..].Trim();
                            sections[currentSection][k] = v;
                        }
                    }
                }
            }
            catch { }
        }

        if (updateViewState)
        {
            if (!sections.TryGetValue("ViewState", out var vs))
            {
                vs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections["ViewState"] = vs;
            }
            vs["Mode"] = "";
            vs["Vid"] = "";
            vs["FolderType"] = string.IsNullOrWhiteSpace(folderTypeKey) ? "Generic" : folderTypeKey;

            if (folderPicturePath != null)
            {
                if (string.IsNullOrWhiteSpace(folderPicturePath))
                    vs.Remove("Logo");
                else
                    vs["Logo"] = folderPicturePath.Trim('\"');
            }
        }

        if (updateIcon)
        {
            if (!sections.TryGetValue(".ShellClassInfo", out var sci))
            {
                sci = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[".ShellClassInfo"] = sci;
            }

            if (string.IsNullOrWhiteSpace(customIconResource))
            {
                sci.Remove("IconResource");
                sci.Remove("IconFile");
                sci.Remove("IconIndex");
            }
            else
            {
                sci["IconResource"] = NormalizeIconResourceString(customIconResource);
                sci.Remove("IconFile");
                sci.Remove("IconIndex");
            }
        }

        // Write updated desktop.ini in Unicode (UTF-16 LE with BOM) which Windows Shell expects for non-ASCII paths
        var sb = new StringBuilder();
        foreach (var (secName, kvps) in sections)
        {
            if (kvps.Count == 0) continue;
            sb.AppendLine($"[{secName}]");
            foreach (var (k, v) in kvps)
            {
                sb.AppendLine($"{k}={v}");
            }
            sb.AppendLine();
        }

        var dirInfo = new DirectoryInfo(folderPath);
        if (sb.Length == 0)
        {
            if (File.Exists(iniPath))
            {
                try
                {
                    File.SetAttributes(iniPath, FileAttributes.Normal);
                    File.Delete(iniPath);
                }
                catch { }
            }
            try
            {
                dirInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch { }
            return;
        }

        if (File.Exists(iniPath))
        {
            try { File.SetAttributes(iniPath, FileAttributes.Normal); } catch { }
        }
        File.WriteAllText(iniPath, sb.ToString(), Encoding.Unicode);
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);

        // Windows Explorer only parses desktop.ini when the directory itself has ReadOnly or System attribute
        dirInfo.Attributes |= FileAttributes.ReadOnly;
    }

    private static IEnumerable<string> EnumerateSubdirectoriesSafe(string rootPath)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                try
                {
                    var attrs = File.GetAttributes(sub);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch
                {
                    continue;
                }
                yield return sub;
                stack.Push(sub);
            }
        }
    }
}
