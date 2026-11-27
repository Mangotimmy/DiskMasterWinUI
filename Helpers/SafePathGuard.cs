using System.IO;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Centralized file &amp; directory safety gate invoked by all cleaning/deletion services.
/// Hard-blocks critical Windows system directories, user personal folders, credential/browser profile stores,
/// DiskMaster's own settings/quarantine vault, reparse points/junctions, and recently modified active temp files.
/// </summary>
public static class SafePathGuard
{
    private static readonly string WinDir = NormalizePrefix(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    private static readonly string ProgramFiles = NormalizePrefix(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    private static readonly string ProgramFilesX86 = NormalizePrefix(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    private static readonly string ProgramData = NormalizePrefix(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
    private static readonly string LocalAppData = NormalizePrefix(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static readonly string[] HardBlockedExactOrPrefixPaths = BuildProtectedPrefixes();

    /// <summary>
    /// Path fragments that hold user credentials, browser logins/cookies/bookmarks, or OS security stores.
    /// </summary>
    private static readonly string[] ProtectedSubPathFragments =
    [
        @"\Microsoft\Protect\",
        @"\Microsoft\Credentials\",
        @"\Microsoft\Vault\",
        @"\Microsoft\Crypto\",
        @"\DiskMaster_Portable\",
        @"\DiskMaster\Logs\",
        @"\ProgramData\Package Cache\",
        @"\Windows\System32\",
        @"\Windows\SysWOW64\",
        @"\Windows\WinSxS\",
        @"\Windows\Installer\",
        @"\Windows\Panther\",
        @"\Windows\Boot\",
        @"\Windows\Fonts\",
        @"\Windows\INF\",
        @"\Windows\SystemApps\",
        @"\Login Data",
        @"\Web Data",
        @"\Bookmarks",
        @"\Cookies",
        @"\Network\Cookies",
        @"\History",
        @"\logins.json",
        @"\key4.db",
        @"\places.sqlite",
        @"\cert9.db"
    ];

    private static string[] BuildProtectedPrefixes()
    {
        var list = new List<string>();
        void AddFolder(Environment.SpecialFolder folder)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(p)) list.Add(NormalizePrefix(p));
            }
            catch { }
        }

        AddFolder(Environment.SpecialFolder.DesktopDirectory);
        AddFolder(Environment.SpecialFolder.MyDocuments);
        AddFolder(Environment.SpecialFolder.MyPictures);
        AddFolder(Environment.SpecialFolder.MyVideos);
        AddFolder(Environment.SpecialFolder.MyMusic);
        AddFolder(Environment.SpecialFolder.Favorites);

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                list.Add(NormalizePrefix(Path.Combine(userProfile, "Downloads")));
                list.Add(NormalizePrefix(Path.Combine(userProfile, "OneDrive")));
                list.Add(NormalizePrefix(Path.Combine(userProfile, "Contacts")));
                list.Add(NormalizePrefix(Path.Combine(userProfile, "Saved Games")));
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(ProgramFiles)) list.Add(ProgramFiles);
        if (!string.IsNullOrEmpty(ProgramFilesX86)) list.Add(ProgramFilesX86);
        if (!string.IsNullOrEmpty(ProgramData)) list.Add(NormalizePrefix(Path.Combine(ProgramData, "Package Cache")));
        if (!string.IsNullOrEmpty(LocalAppData)) list.Add(NormalizePrefix(Path.Combine(LocalAppData, "DiskMaster_Portable")));

        if (!string.IsNullOrEmpty(WinDir))
        {
            foreach (var sub in new[] { "System32", "SysWOW64", "WinSxS", "Installer", "Panther", "Boot", "Fonts", "INF", "SystemApps" })
            {
                list.Add(NormalizePrefix(Path.Combine(WinDir, sub)));
            }
        }

        return list.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizePrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full + Path.DirectorySeparatorChar;
        }
        catch
        {
            return path.TrimEnd('\\', '/') + @"\";
        }
    }

    /// <summary>
    /// Returns true if the target file/directory path is safe for automated temp/cache cleanup.
    /// Returns false (with reason) if it points to a drive root, protected system/user directory,
    /// credential/profile database, or DiskMaster's own Quarantine Vault.
    /// </summary>
    public static bool IsSafeToClean(string? path, out string blockReason)
    {
        blockReason = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            blockReason = "路徑為空";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            blockReason = "無效路徑格式";
            return false;
        }

        // Never allow drive roots (e.g. C:\) or C:\Windows itself
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(fullPath.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "禁止清理磁碟根目錄";
            return false;
        }

        if (!string.IsNullOrEmpty(WinDir) &&
            string.Equals(fullPath.TrimEnd('\\') + @"\", WinDir, StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "禁止直接清理 Windows 根目錄";
            return false;
        }

        string withTrailingSlash = fullPath.TrimEnd('\\') + @"\";

        foreach (var protectedPrefix in HardBlockedExactOrPrefixPaths)
        {
            if (withTrailingSlash.StartsWith(protectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                blockReason = $"受保護系統／個人目錄 ({protectedPrefix.TrimEnd('\\')})";
                return false;
            }
        }

        foreach (var fragment in ProtectedSubPathFragments)
        {
            if (fullPath.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                withTrailingSlash.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                blockReason = $"受保護核心／憑證／設定檔案 ({fragment.Trim('\\')})";
                return false;
            }
        }

        // Never follow or delete reparse points / symbolic links / junctions
        try
        {
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                var attrs = File.GetAttributes(fullPath);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    blockReason = "略過符號連結／接點 (ReparsePoint)";
                    return false;
                }
            }
        }
        catch { }

        return true;
    }

    /// <summary>
    /// Returns true if a file was created or modified within the last <paramref name="minAgeHours"/> hours,
    /// protecting active installers, archive extractions, and unsaved user app recovery files in %TEMP%.
    /// </summary>
    public static bool IsProtectedByAgeGuard(FileInfo file, int minAgeHours)
    {
        if (minAgeHours <= 0) return false;
        try
        {
            var cutoffUtc = DateTime.UtcNow.AddHours(-minAgeHours);
            return file.LastWriteTimeUtc > cutoffUtc || file.CreationTimeUtc > cutoffUtc;
        }
        catch
        {
            return true; // Fail safe if metadata cannot be read
        }
    }
}
