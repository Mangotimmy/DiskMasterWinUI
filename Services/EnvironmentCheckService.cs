using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskMasterWinUI.Helpers;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiskMasterWinUI.Services;

public partial class ToolEnvironmentItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _pathFound = "";
    [ObservableProperty] private string _wingetPackageId = "";

    public bool CanInstallViaWinget => !string.IsNullOrEmpty(WingetPackageId) && !IsAvailable;
    public string StatusGlyph => IsAvailable ? "\uE73E" : "\uE711"; // Checkmark vs X
    public SolidColorBrush StatusBrush => IsAvailable
        ? new SolidColorBrush(Color.FromArgb(255, 76, 175, 80))  // Green
        : new SolidColorBrush(Color.FromArgb(255, 244, 67, 54)); // Red
}

public class EnvironmentCheckService
{
    public async Task<List<ToolEnvironmentItem>> CheckAllToolsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<ToolEnvironmentItem>
            {
                CheckTool("DiskPart CLI", "diskpart.exe", "Core Windows partitioning utility", ""),
                CheckTool("Defrag / SSD TRIM", "defrag.exe", "Volume defragmentation and SSD TRIM optimizer", ""),
                CheckTool("Boot Configuration", "bcdboot.exe", "Windows boot file and BCD generator", ""),
                CheckTool("Boot Recovery", "bootrec.exe", "Windows Recovery Environment boot repair tool", ""),
                CheckTool("WSL2 (EXT4 Support)", "wsl.exe", "Windows Subsystem for Linux (Mount EXT4)", "Microsoft.WSL"),
                CheckTool("Windows File Recovery", "winfr.exe", "Microsoft official deleted file recovery CLI", "9N26S50LN705")
            };
            return list;
        });
    }

    private static ToolEnvironmentItem CheckTool(string name, string exeName, string desc, string wingetId)
    {
        var item = new ToolEnvironmentItem
        {
            Name = name,
            Description = desc,
            WingetPackageId = wingetId
        };

        // Check system32
        var sysPath = Path.Combine(Environment.SystemDirectory, exeName);
        if (File.Exists(sysPath))
        {
            item.IsAvailable = true;
            item.PathFound = sysPath;
            return item;
        }

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(p.Trim(), exeName);
            if (File.Exists(full))
            {
                item.IsAvailable = true;
                item.PathFound = full;
                return item;
            }
        }

        // Check local appdata WindowsApps for Store apps (like WinFR)
        var localApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", exeName);
        if (File.Exists(localApps))
        {
            item.IsAvailable = true;
            item.PathFound = localApps;
            return item;
        }

        item.IsAvailable = false;
        item.PathFound = "Not found in system PATH";
        return item;
    }

    public async Task<string> InstallViaWingetAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var (stdout, stderr, _) = await ProcessHelper.RunProcessAsync(
            "winget.exe",
            $"install --id \"{packageId}\" --accept-source-agreements --accept-package-agreements --silent",
            cancellationToken: cancellationToken);

        return $"{stdout}\n{stderr}";
    }
}
