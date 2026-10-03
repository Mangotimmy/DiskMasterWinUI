using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class BitLockerService
{
    public async Task<string> GetStatusAsync(string? driveLetter = null)
    {
        var target = string.IsNullOrWhiteSpace(driveLetter) ? "" : $" {driveLetter.TrimEnd('\\', ':')}:";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-status{target}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<List<BitLockerVolumeItem>> GetBitLockerVolumesAsync()
    {
        var items = new List<BitLockerVolumeItem>();
        var (output, _, _) = await ProcessHelper.RunProcessAsync("manage-bde.exe", "-status");
        if (string.IsNullOrWhiteSpace(output)) return items;

        // Split output by volume blocks: "Volume C: []" or "Disk Volumes"
        var volumeBlocks = Regex.Split(output, @"(?=Volume\s+[A-Za-z]:)", RegexOptions.IgnoreCase);

        foreach (var block in volumeBlocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            var mountMatch = Regex.Match(block, @"Volume\s+([A-Za-z]:)\s*(?:\[(.*?)\])?", RegexOptions.IgnoreCase);
            if (!mountMatch.Success) continue;

            var item = new BitLockerVolumeItem
            {
                MountPoint = mountMatch.Groups[1].Value.ToUpperInvariant(),
                VolumeLabel = mountMatch.Groups[2].Value.Trim()
            };

            // Size / Conversion status
            var convMatch = Regex.Match(block, @"Conversion Status:\s*(.+)", RegexOptions.IgnoreCase);
            if (convMatch.Success) item.ConversionStatus = convMatch.Groups[1].Value.Trim();

            // Percentage encrypted
            var pctMatch = Regex.Match(block, @"Percentage Encrypted:\s*(\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase);
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, out var pct))
            {
                item.PercentageEncrypted = pct;
            }

            // Encryption method
            var encMatch = Regex.Match(block, @"Encryption Method:\s*(.+)", RegexOptions.IgnoreCase);
            if (encMatch.Success) item.EncryptionMethod = encMatch.Groups[1].Value.Trim();

            // Protection status
            var protMatch = Regex.Match(block, @"Protection Status:\s*(.+)", RegexOptions.IgnoreCase);
            if (protMatch.Success)
            {
                item.ProtectionStatus = protMatch.Groups[1].Value.Trim();
                item.IsProtected = item.ProtectionStatus.Contains("On", StringComparison.OrdinalIgnoreCase);
            }

            // Lock status
            var lockMatch = Regex.Match(block, @"Lock Status:\s*(.+)", RegexOptions.IgnoreCase);
            if (lockMatch.Success)
            {
                item.LockStatus = lockMatch.Groups[1].Value.Trim();
                item.IsLocked = item.LockStatus.Contains("Locked", StringComparison.OrdinalIgnoreCase);
            }

            // Key protectors
            var keyMatch = Regex.Match(block, @"Key Protectors:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (keyMatch.Success)
            {
                item.KeyProtectorSummary = keyMatch.Groups[1].Value.Trim();
            }

            items.Add(item);
        }

        return items;
    }

    public async Task<string> GetProtectorsAsync(string driveLetter)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var (output, err, _) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-protectors -get {drive}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<string?> GetRecoveryPasswordAsync(string driveLetter)
    {
        var protectorsText = await GetProtectorsAsync(driveLetter);
        // Look for 48-digit numerical password format: xxxxxx-xxxxxx-xxxxxx-xxxxxx-xxxxxx-xxxxxx-xxxxxx-xxxxxx
        var match = Regex.Match(protectorsText, @"(\d{6}-\d{6}-\d{6}-\d{6}-\d{6}-\d{6}-\d{6}-\d{6})");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Instantly locks an unlocked BitLocker drive.
    /// Uses -ForceDismount to immediately close active handles and dismount.
    /// </summary>
    public async Task<(bool Success, string Message)> LockVolumeAsync(string driveLetter, bool forceDismount = true)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var forceArg = forceDismount ? " -ForceDismount" : "";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-lock {drive}{forceArg}");
        var msg = string.IsNullOrWhiteSpace(output) ? err : output.Trim();
        return (code == 0, msg);
    }

    /// <summary>
    /// Unlocks a locked BitLocker drive using the 48-digit numerical recovery key.
    /// </summary>
    public async Task<(bool Success, string Message)> UnlockWithRecoveryKeyAsync(string driveLetter, string recoveryPassword)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var cleanKey = recoveryPassword.Trim();
        var (output, err, code) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-unlock {drive} -recoverypassword \"{cleanKey}\"");
        var msg = string.IsNullOrWhiteSpace(output) ? err : output.Trim();
        return (code == 0, msg);
    }

    /// <summary>
    /// Unlocks a locked BitLocker drive using the user password.
    /// </summary>
    public async Task<(bool Success, string Message)> UnlockWithPasswordAsync(string driveLetter, string password)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var cleanPass = password.Trim();
        // manage-bde supports -password
        var (output, err, code) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-unlock {drive} -password \"{cleanPass}\"");
        var msg = string.IsNullOrWhiteSpace(output) ? err : output.Trim();
        return (code == 0, msg);
    }

    /// <summary>
    /// Backward compatible Unlock method.
    /// </summary>
    public async Task<string> UnlockAsync(string driveLetter, string recoveryPassword)
    {
        var (_, msg) = await UnlockWithRecoveryKeyAsync(driveLetter, recoveryPassword);
        return msg;
    }

    public async Task<string> SuspendProtectionAsync(string driveLetter)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var (output, err, _) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-pause {drive}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<string> ResumeProtectionAsync(string driveLetter)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var (output, err, _) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-resume {drive}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<string> DecryptDriveAsync(string driveLetter)
    {
        var drive = driveLetter.TrimEnd('\\', ':') + ":";
        var (output, err, _) = await ProcessHelper.RunProcessAsync("manage-bde.exe", $"-off {drive}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    /// <summary>
    /// Exports recovery password protectors to a text file for safekeeping.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> ExportRecoveryKeyAsync(string driveLetter, string? targetPath = null)
    {
        try
        {
            var drive = driveLetter.TrimEnd('\\', ':') + ":";
            var protectors = await GetProtectorsAsync(drive);
            var key = await GetRecoveryPasswordAsync(drive);

            if (string.IsNullOrEmpty(key))
            {
                return (false, "", $"未在 {drive} 找到 48 位元修復金鑰 (可能未啟用加密或僅使用 TPM)。");
            }

            var dest = targetPath;
            if (string.IsNullOrEmpty(dest))
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var safeDrive = driveLetter.Replace(":", "");
                dest = Path.Combine(desktop, $"BitLocker_RecoveryKey_{safeDrive}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            }

            var content = $"═══ DiskMaster Pro BitLocker 修復金鑰備份 ═══\r\n" +
                          $"磁碟機代號: {drive}\r\n" +
                          $"備份時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                          $"48 位元修復金鑰: {key}\r\n\r\n" +
                          $"詳細保護者資訊:\r\n{protectors}\r\n";

            await File.WriteAllTextAsync(dest, content);
            return (true, dest, $"修復金鑰已成功備份至: {dest}");
        }
        catch (Exception ex)
        {
            return (false, "", $"備份失敗: {ex.Message}");
        }
    }
}
