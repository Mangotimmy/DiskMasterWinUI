using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public partial class PowerPlanService
{
    [GeneratedRegex(@"GUID:\s+([a-fA-F0-9\-]+)\s+\((.*?)\)(\s*\*?)", RegexOptions.IgnoreCase)]
    private static partial Regex PowerSchemeRegex();

    public async Task<List<PowerPlanItem>> GetPowerPlansAsync()
    {
        var list = new List<PowerPlanItem>();
        try
        {
            // Execute powercfg.exe directly to match the system console OEM encoding (eliminates mojibake)
            var (output, _, _) = await ProcessHelper.RunProcessAsync("powercfg.exe", "/list");

            var matches = PowerSchemeRegex().Matches(output);
            foreach (Match m in matches)
            {
                var guid = m.Groups[1].Value.Trim();
                var name = m.Groups[2].Value.Trim();
                var isActive = m.Groups[3].Value.Contains('*');

                list.Add(new PowerPlanItem
                {
                    Guid = guid,
                    Name = name,
                    IsActive = isActive
                });
            }
        }
        catch { }
        return list;
    }

    public async Task<string> SetActivePlanAsync(string guid)
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setactive {guid}");
        return code == 0 ? "Success" : string.IsNullOrWhiteSpace(err) ? output : err;
    }

    public async Task<(bool Success, string Message, string? NewGuid)> UnlockUltimatePerformanceAsync()
    {
        try
        {
            var (output, err, code) = await ProcessHelper.RunProcessAsync(
                "powercfg.exe",
                "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");

            if (code == 0)
            {
                var match = Regex.Match(output, @"([a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})");
                var newGuid = match.Success ? match.Groups[1].Value : null;
                if (!string.IsNullOrEmpty(newGuid))
                {
                    await SetActivePlanAsync(newGuid);
                    // Also unpark cores on ultimate scheme
                    await ConfigureCoreUnparkingAsync(true);
                    await ConfigureMinProcessorStateAsync(100);
                }
                return (true, "已成功解鎖並套用「終極效能 (Ultimate Performance)」模式！", newGuid);
            }
            return (false, $"解鎖失敗: {err}", null);
        }
        catch (Exception ex)
        {
            return (false, $"例外錯誤: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message, string? NewGuid)> UnlockHighPerformanceAsync()
    {
        try
        {
            var (output, err, code) = await ProcessHelper.RunProcessAsync(
                "powercfg.exe",
                "-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

            if (code == 0)
            {
                var match = Regex.Match(output, @"([a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})");
                var newGuid = match.Success ? match.Groups[1].Value : null;
                if (!string.IsNullOrEmpty(newGuid))
                {
                    await SetActivePlanAsync(newGuid);
                }
                return (true, "已成功解鎖並啟用「高效能 (High Performance)」模式！", newGuid);
            }
            return (false, $"解鎖失敗: {err}", null);
        }
        catch (Exception ex)
        {
            return (false, $"例外錯誤: {ex.Message}", null);
        }
    }

    public async Task<string> ConfigureCoreUnparkingAsync(bool unpark)
    {
        int val = unpark ? 100 : 10;
        await ProcessHelper.RunProcessAsync("powercfg.exe", $"-setacvalueindex scheme_current sub_processor CPMINCORES {val}");
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", "-setactive scheme_current");
        return code == 0 ? "Success" : err;
    }

    public async Task<string> ConfigureMinProcessorStateAsync(int percent)
    {
        await ProcessHelper.RunProcessAsync("powercfg.exe", $"-setacvalueindex scheme_current sub_processor PROCTHROTTLEMIN {percent}");
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", "-setactive scheme_current");
        return code == 0 ? "Success" : err;
    }

    public async Task<string> ExportPowerPlanAsync(string guid, string filePath)
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"-export \"{filePath}\" {guid}");
        return code == 0 ? "Success" : string.IsNullOrWhiteSpace(err) ? output : err;
    }

    public async Task<string> ImportPowerPlanAsync(string filePath)
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"-import \"{filePath}\"");
        return code == 0 ? "Success" : string.IsNullOrWhiteSpace(err) ? output : err;
    }

    public async Task<string> DeletePowerPlanAsync(string guid)
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"-delete {guid}");
        return code == 0 ? "Success" : string.IsNullOrWhiteSpace(err) ? output : err;
    }
}
