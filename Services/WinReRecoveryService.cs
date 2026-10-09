using System.Diagnostics;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

public class WinReStatusInfo
{
    public bool IsEnabled { get; set; }
    public string Location { get; set; } = "";
    public string BcdGuid { get; set; } = "";
    public string CustomImageLocation { get; set; } = "";
    public string RawOutput { get; set; } = "";
}

/// <summary>
/// Windows Recovery Environment (WinRE) Lifecycle & Dedicated Recovery Partition Manager.
/// Manages reagentc commands, custom reimage path configuration, and one-click WinRE boot.
/// </summary>
public class WinReRecoveryService
{
    public async Task<WinReStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        var info = new WinReStatusInfo();
        try
        {
            var (outStr, _, _) = await ProcessHelper.RunProcessAsync("reagentc.exe", "/info", cancellationToken: ct);
            info.RawOutput = outStr;

            var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("Windows RE status:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("Windows RE 狀態:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("Windows RE 状态:", StringComparison.OrdinalIgnoreCase))
                {
                    info.IsEnabled = trimmed.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ||
                                     trimmed.Contains("啟用", StringComparison.OrdinalIgnoreCase) ||
                                     trimmed.Contains("启用", StringComparison.OrdinalIgnoreCase);
                }
                else if (trimmed.Contains("Windows RE location:", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("Windows RE 位置:", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx > 0) info.Location = trimmed.Substring(idx + 1).Trim();
                }
                else if (trimmed.Contains("Boot Configuration Data (BCD) identifier:", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("開機設定資料 (BCD) 識別碼:", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("启动配置数据 (BCD) 标识符:", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx > 0) info.BcdGuid = trimmed.Substring(idx + 1).Trim();
                }
            }
        }
        catch { }

        return info;
    }

    public async Task<(bool Success, string Message)> EnableWinReAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("reagentc.exe", "/enable");
        return (code == 0, code == 0 ? "Windows RE (修復環境) 已成功啟用！" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    public async Task<(bool Success, string Message)> DisableWinReAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("reagentc.exe", "/disable");
        return (code == 0, code == 0 ? "Windows RE 已成功停用。" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    public async Task<(bool Success, string Message)> SetReimagePathAsync(string customPath)
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("reagentc.exe", $"/setreimage /path \"{customPath}\"");
        return (code == 0, code == 0 ? $"WinRE 映像路徑已成功重新綁定至: {customPath}" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    public async Task<(bool Success, string Message)> BootToRecoveryEnvironmentAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("reagentc.exe", "/boottore");
        return (code == 0, code == 0 ? "已設定下次開機自動導引至 Windows RE 修復環境！" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    public async Task<(bool Success, string Message)> CreateDedicatedRecoveryPartitionAsync(int diskNumber, int sizeMb = 1000)
    {
        // DiskPart script to create standard GPT WinRE partition
        var script = $"select disk {diskNumber}\r\ncreate partition primary size={sizeMb}\r\nformat quick fs=ntfs label=\"Recovery\"\r\nset id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"\r\ngpt attributes=0x8000000000000001\r\n";
        var diskpart = new DiskPartService();
        var dpRes = await diskpart.RunCustomScriptAsync(script);

        // Re-enable WinRE to auto-populate the new recovery partition
        await EnableWinReAsync();

        return (true, $"已成功在磁碟 {diskNumber} 建立標準 GPT WinRE 修復磁區 (1000 MB)。\r\n{dpRes}");
    }
}
