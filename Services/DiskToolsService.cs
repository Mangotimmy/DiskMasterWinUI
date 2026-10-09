using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class DiskToolsService
{
    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken);
    }

    public async Task<string> OptimizeSsdTrimAsync(string driveLetter)
    {
        var cleanLetter = driveLetter.TrimEnd(':', '\\');
        return await RunCommandAsync("defrag.exe", $"{cleanLetter}: /L");
    }

    public async Task<string> DefragVolumeAsync(string driveLetter)
    {
        var cleanLetter = driveLetter.TrimEnd(':', '\\');
        return await RunCommandAsync("defrag.exe", $"{cleanLetter}: /U /V");
    }

    public async Task<string> RunWinFrRecoveryAsync(string sourceDrive, string targetFolder, string mode = "/regular", string filter = "*.*")
    {
        var src = sourceDrive.TrimEnd(':', '\\') + ":";
        var args = $"{src} \"{targetFolder}\" {mode} /n \"{filter}\" /y";
        return await RunCommandAsync("winfr.exe", args);
    }

    public async Task<string> MountExt4DiskAsync(int diskNumber, int? partitionNumber = null)
    {
        var partArg = partitionNumber.HasValue ? $" --partition {partitionNumber.Value}" : " --bare";
        var args = $"--mount \\\\.\\PHYSICALDRIVE{diskNumber}{partArg}";
        return await RunCommandAsync("wsl.exe", args);
    }

    public async Task<string> UnmountExt4DiskAsync(int diskNumber)
    {
        var args = $"--unmount \\\\.\\PHYSICALDRIVE{diskNumber}";
        return await RunCommandAsync("wsl.exe", args);
    }

    public async Task<string> MountVirtualDiskAsync(string path)
    {
        if (!File.Exists(path)) return "ERROR: File not found: " + path;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".iso" || ext == ".vhd" || ext == ".vhdx")
        {
            var escapedPath = path.Replace("'", "''");
            var psScript = $"$img = Mount-DiskImage -ImagePath '{escapedPath}' -PassThru; $disk = $img | Get-Disk; $part = $disk | Get-Partition | Where-Object {{ $_.DriveLetter }}; if ($part) {{ ($part | ForEach-Object {{ $_.DriveLetter + ':' }}) -join ', ' }} else {{ 'Mounted (No letter assigned)' }}";
            var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psScript}\"");
            if (code == 0 && !string.IsNullOrWhiteSpace(outStr))
            {
                return $"Successfully mounted {Path.GetFileName(path)} -> {outStr.Trim()}";
            }

            // Fallback for VHD/VHDX: Try DiskPart attach
            if (ext == ".vhd" || ext == ".vhdx")
            {
                var vhdService = new VhdService();
                var attachRes = await vhdService.AttachVhdAsync(path);
                if (attachRes.Success) return $"Successfully mounted {Path.GetFileName(path)} (via DiskPart)";
            }

            if (!string.IsNullOrWhiteSpace(errStr)) return $"[STDERR] {errStr}";
            return string.IsNullOrWhiteSpace(outStr) ? "Mount command completed." : outStr.Trim();
        }
        else
        {
            return "ERROR: Unsupported virtual disk format. Supported: .iso, .vhd, .vhdx";
        }
    }

    public async Task<string> DismountVirtualDiskAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "No image path specified.";
        var escapedPath = path.Replace("'", "''");
        var psScript = $"Dismount-DiskImage -ImagePath '{escapedPath}'";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psScript}\"");
        
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (code != 0 && (ext == ".vhd" || ext == ".vhdx"))
        {
            var vhdService = new VhdService();
            var detachRes = await vhdService.DetachVhdAsync(path);
            if (detachRes.Success) return $"Successfully dismounted {Path.GetFileName(path)} (via DiskPart)";
        }

        if (!string.IsNullOrWhiteSpace(errStr)) return $"[STDERR] {errStr}";
        return $"Successfully dismounted {Path.GetFileName(path)}";
    }

    public async Task<string> RecoverPartitionAsync(int diskNumber, int partitionNumber)
    {
        var script = $"select disk {diskNumber}\r\nselect partition {partitionNumber}\r\nrecover";
        var diskPart = new DiskPartService();
        return await diskPart.RunCustomScriptAsync(script);
    }

    // ── Phase 4: Disk Maintenance & Sanitization ──

    public async Task<string> AnalyzeFragmentationAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("defrag.exe", $"{clean} /A /V");
    }

    public async Task<string> OptimizeAutoAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("defrag.exe", $"{clean} /O");
    }

    public async Task<string> ConsolidateFreeSpaceAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("defrag.exe", $"{clean} /X");
    }

    public async Task<string> SecureWipeFreeSpaceAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("cipher.exe", $"/w:{clean}");
    }

    public async Task<string> QueryTrimStatusAsync()
    {
        return await RunCommandAsync("fsutil.exe", "behavior query DisableDeleteNotify");
    }

    public async Task<string> EnableTrimAsync()
    {
        return await RunCommandAsync("fsutil.exe", "behavior set DisableDeleteNotify 0");
    }

    public async Task<string> DisableTrimAsync()
    {
        return await RunCommandAsync("fsutil.exe", "behavior set DisableDeleteNotify 1");
    }

    public async Task<string> GetVolumeInfoAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("fsutil.exe", $"fsinfo volumeinfo {clean}");
    }

    public async Task<string> ListShadowCopiesAsync()
    {
        return await RunCommandAsync("vssadmin.exe", "list shadows");
    }

    public async Task<string> DeleteAllShadowsAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        return await RunCommandAsync("vssadmin.exe", $"delete shadows /for={clean} /all /quiet");
    }

    public async Task<List<VssShadowItem>> ListShadowCopiesStructuredAsync()
    {
        var output = await ListShadowCopiesAsync();
        return OutputParser.ParseVssShadows(output);
    }

    public async Task<string> DeleteSingleShadowAsync(string shadowId)
    {
        return await RunCommandAsync("vssadmin.exe", $"delete shadows /shadow={shadowId} /quiet");
    }

    public async Task<string> ListRestorePointsAsync()
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powershell.exe", "-NoProfile -Command \"Get-ComputerRestorePoint | Format-Table -AutoSize | Out-String\"");
        return string.IsNullOrWhiteSpace(output) ? (string.IsNullOrWhiteSpace(err) ? "No restore points found." : err) : output;
    }

    public async Task<string> CreateRestorePointAsync(string description = "DiskMaster Checkpoint")
    {
        var psCmd = $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' -RestorePointType 'MODIFY_SETTINGS'";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");
        return code == 0 ? "系統還原點建立成功！(System Restore Point created successfully)" : $"建立失敗: {err}\n{output}";
    }
}
