using System.Diagnostics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiskMasterWinUI.Helpers;

public static class FilePickerHelper
{
    public static async Task<string?> PickSingleFileAsync(string[] extensions)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowHelper.CurrentHwnd;
            if (hwnd != 0)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            foreach (var ext in extensions)
            {
                var cleanExt = ext.StartsWith('.') ? ext : "." + ext;
                picker.FileTypeFilter.Add(cleanExt);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null) return file.Path;
        }
        catch (Exception)
        {
            // Fallback using PowerShell OpenFileDialog if WinUI picker fails
            return FallbackPickFile(extensions);
        }

        return null;
    }

    public static async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            var hwnd = WindowHelper.CurrentHwnd;
            if (hwnd != 0)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) return folder.Path;
        }
        catch (Exception)
        {
            // Fallback using PowerShell FolderBrowserDialog
            return FallbackPickFolder();
        }

        return null;
    }

    private static string? FallbackPickFile(string[] extensions)
    {
        try
        {
            var filter = string.Join(";", extensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)));
            var psScript = $@"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Filter = 'Files ({filter})|{filter}|All Files (*.*)|*.*'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ $f.FileName }}";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{psScript}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var res = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return string.IsNullOrWhiteSpace(res) ? null : res;
            }
        }
        catch { }
        return null;
    }

    private static string? FallbackPickFolder()
    {
        try
        {
            var psScript = @"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; $f = New-Object System.Windows.Forms.FolderBrowserDialog; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $f.SelectedPath }";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{psScript}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var res = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return string.IsNullOrWhiteSpace(res) ? null : res;
            }
        }
        catch { }
        return null;
    }
}
