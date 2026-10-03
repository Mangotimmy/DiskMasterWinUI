namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents the real-time installation and configuration status of Microsoft OneDrive.
/// </summary>
public class OneDriveStatusInfo
{
    /// <summary>
    /// Gets or sets whether OneDrive is installed on this system.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Gets or sets whether OneDrive.exe is currently running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets the path to the main OneDrive executable.
    /// </summary>
    public string ExePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the path to the OneDrive setup/uninstaller executable.
    /// </summary>
    public string UninstallerPath { get; set; } = "";

    /// <summary>
    /// Gets or sets whether standard user folders (Desktop, Documents, Pictures) are redirected to OneDrive.
    /// </summary>
    public bool IsFoldersRedirected { get; set; }

    /// <summary>
    /// Gets or sets the list of redirected folder names (e.g. "Desktop", "Documents", "Pictures").
    /// </summary>
    public List<string> RedirectedFolderNames { get; set; } = new();

    /// <summary>
    /// Gets or sets whether there are dehydrated cloud-only files (reparse points) in the OneDrive folder.
    /// </summary>
    public bool HasCloudOnlyFiles { get; set; }

    /// <summary>
    /// Gets or sets the count of cloud-only offline files found.
    /// </summary>
    public int CloudOnlyFileCount { get; set; }

    /// <summary>
    /// Gets or sets whether the OneDrive ghost icon is pinned to File Explorer navigation pane.
    /// </summary>
    public bool IsFileExplorerPinned { get; set; }

    /// <summary>
    /// Gets or sets whether Group Policy currently blocks OneDrive sync and execution.
    /// </summary>
    public bool IsPolicyBlocked { get; set; }

    /// <summary>
    /// Gets or sets the path to the user's local OneDrive sync directory.
    /// </summary>
    public string LocalOneDrivePath { get; set; } = "";
}
