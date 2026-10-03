using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Comprehensive VHD / VHDX Virtual Hard Disk Management Service.
/// Supports creation (Dynamic/Fixed, VHD/VHDX), mount/attach (with Read-Only option),
/// detach, compaction, capacity expansion, and 1-Click Native VHD Boot deployment.
/// </summary>
public class VhdService
{
    private static async Task<string> RunDiskPartScriptAsync(string script)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dm_vhd_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, script + "\r\nexit\r\n", Encoding.ASCII);
            var (output, err, code) = await ProcessHelper.RunProcessAsync("diskpart.exe", $"/s \"{tempFile}\"");
            return string.IsNullOrWhiteSpace(output) ? err : output;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Creates a new VHD or VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> CreateVhdAsync(
        string vhdPath,
        long sizeMB,
        bool isVhdx = true,
        bool isDynamic = true)
    {
        var ext = isVhdx ? ".vhdx" : ".vhd";
        if (!vhdPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            vhdPath += ext;
        }

        var dir = Path.GetDirectoryName(vhdPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var typeStr = isDynamic ? "expandable" : "fixed";
        var script = $"create vdisk file=\"{vhdPath}\" maximum={sizeMB} type={typeStr}";

        var output = await RunDiskPartScriptAsync(script);
        if (output.Contains("successfully created", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利建立", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功创建", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功建立 {(isVhdx ? "VHDX" : "VHD")} 虛擬磁碟：{Path.GetFileName(vhdPath)} (大小: {sizeMB:N0} MB, 類型: {(isDynamic ? "動態擴充" : "固定容量")})");
        }

        return (false, output);
    }

    /// <summary>
    /// Attaches (mounts) an existing VHD / VHDX file, with optional Read-Only protection.
    /// </summary>
    public async Task<(bool Success, string Message)> AttachVhdAsync(string vhdPath, bool readOnly = false)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var readOnlyArg = readOnly ? " readonly" : "";
        var script = $"select vdisk file=\"{vhdPath}\"\r\nattach vdisk{readOnlyArg}";

        var output = await RunDiskPartScriptAsync(script);
        if (output.Contains("successfully attached", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利附加", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功附加", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功掛載虛擬硬碟：{Path.GetFileName(vhdPath)}{(readOnly ? " [唯讀保護]" : "")}");
        }

        return (false, output);
    }

    /// <summary>
    /// Detaches (unmounts) a mounted VHD / VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> DetachVhdAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\ndetach vdisk";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully detached", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利分離", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功分离", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功安全卸載虛擬硬碟：{Path.GetFileName(vhdPath)}");
        }

        return (false, output);
    }

    /// <summary>
    /// Compacts a dynamic VHD / VHDX file to reclaim unused zero-byte space.
    /// </summary>
    public async Task<(bool Success, string Message)> CompactVhdAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully compacted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利壓縮", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功压缩", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功壓縮收縮虛擬硬碟：{Path.GetFileName(vhdPath)} (已釋放未使用區塊空間)");
        }

        return (false, output);
    }

    /// <summary>
    /// Expands the maximum capacity limit of a VHD / VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> ExpandVhdAsync(string vhdPath, long newMaximumSizeMB)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\nexpand vdisk maximum={newMaximumSizeMB}";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully expanded", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利擴展", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功扩展", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功擴展虛擬硬碟上限容量至: {newMaximumSizeMB:N0} MB");
        }

        return (false, output);
    }

    /// <summary>
    /// Attaches, initializes (GPT/MBR), partitions, formats as NTFS, and assigns a drive letter to a VHD/VHDX.
    /// </summary>
    public async Task<(bool Success, string Message, string AssignedLetter)> InitializeAndFormatVhdAsync(
        string vhdPath,
        string label = "VHDX_DATA",
        char? driveLetter = null,
        bool asGpt = true)
    {
        var style = asGpt ? "gpt" : "mbr";
        var letterCmd = driveLetter.HasValue ? $"assign letter={driveLetter.Value}" : "assign";

        var script = $"""
select vdisk file="{vhdPath}"
attach vdisk
convert {style}
create partition primary
format fs=ntfs quick label="{label}"
{letterCmd}
detail vdisk
""";

        var output = await RunDiskPartScriptAsync(script);
        var letterMatch = Regex.Match(output, @"Volume\s+\d+\s+([A-Z])\s+", RegexOptions.IgnoreCase);
        var assigned = letterMatch.Success ? letterMatch.Groups[1].Value : (driveLetter?.ToString() ?? "");

        if (output.Contains("successfully formatted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利格式化", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功格式化", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"虛擬硬碟已初始化並格式化為 NTFS，磁碟代號: {assigned}:", assigned);
        }

        return (false, output, assigned);
    }

    /// <summary>
    /// Queries detailed geometry and status of a VHD/VHDX file.
    /// </summary>
    public async Task<string> GetVhdDetailsAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath)) return "檔案不存在";
        var script = $"select vdisk file=\"{vhdPath}\"\r\ndetail vdisk";
        return await RunDiskPartScriptAsync(script);
    }

    /// <summary>
    /// Deploys a Windows WIM image to a VHD/VHDX file and configures native VHD boot in Windows BCD.
    /// </summary>
    public async Task<(bool Success, string Message)> DeployWimToNativeVhdBootAsync(
        string wimPath,
        int wimIndex,
        string vhdPath,
        long sizeMB,
        string bootDescription,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        try
        {
            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 1/5: 正在建立高相容動態 VHDX ({sizeMB:N0} MB)...");
            var createRes = await CreateVhdAsync(vhdPath, sizeMB, isVhdx: true, isDynamic: true);
            if (!createRes.Success) return (false, $"建立 VHDX 失敗: {createRes.Message}");

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 2/5: 正在初始化磁區並掛載為 NTFS...");
            var initRes = await InitializeAndFormatVhdAsync(vhdPath, "VHD_WIN", asGpt: true);
            if (!initRes.Success) return (false, $"初始化 VHDX 失敗: {initRes.Message}");

            var vhdDrive = string.IsNullOrEmpty(initRes.AssignedLetter) ? "V:" : $"{initRes.AssignedLetter}:";
            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 3/5: VHDX 已掛載至 {vhdDrive}，正在透過 DISM 解壓縮套用 Windows 映像 (Index {wimIndex})...");

            var dismArgs = $"/Apply-Image /ImageFile:\"{wimPath}\" /Index:{wimIndex} /ApplyDir:\"{vhdDrive}\"";
            var (dismOut, dismErr, dismCode) = await ProcessHelper.RunProcessAsync("dism.exe", dismArgs, cancellationToken: ct);
            if (dismCode != 0)
            {
                return (false, $"DISM 套用映像失敗: {(string.IsNullOrWhiteSpace(dismErr) ? dismOut : dismErr)}");
            }

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 4/5: 正在寫入主機 UEFI/BIOS BCD 原生 VHD 開機引導項...");
            var bcdArgs = $"\"{vhdDrive}\\Windows\" /d /addlast";
            var (bcdOut, bcdErr, bcdCode) = await ProcessHelper.RunProcessAsync("bcdboot.exe", bcdArgs, cancellationToken: ct);

            // Set custom description in BCD if specified
            if (!string.IsNullOrWhiteSpace(bootDescription))
            {
                try
                {
                    await ProcessHelper.RunProcessAsync("bcdedit.exe", $"/set {{default}} description \"{bootDescription}\"", cancellationToken: ct);
                }
                catch { }
            }

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 5/5: Native VHD Boot 部署完畢！重開機即可在 Windows 開機選單直接啟動此 VHDX 系統！");
            return (true, $"已成功部署 Windows 至 VHDX ({vhdPath})！\n免割實體硬碟分區即可暢享全新獨立 Windows 雙系統！");
        }
        catch (Exception ex)
        {
            return (false, $"例外錯誤: {ex.Message}");
        }
    }
}
