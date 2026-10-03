using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class DriverService
{
    public async Task<string> EnumDriversAsync()
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("pnputil.exe", "/enum-drivers");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<List<OemDriverItem>> EnumDriversStructuredAsync()
    {
        var output = await EnumDriversAsync();
        return OutputParser.ParseOemDrivers(output);
    }

    public async Task<string> ExportDriversAsync(string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        var (output, err, code) = await ProcessHelper.RunProcessAsync("pnputil.exe", $"/export-driver * \"{destinationDir}\"");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<string> InstallDriverAsync(string infPath)
    {
        var (output, err, code) = await ProcessHelper.RunProcessAsync("pnputil.exe", $"/add-driver \"{infPath}\" /install");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }

    public async Task<string> DeleteDriverAsync(string oemInfName, bool force = true)
    {
        if (string.IsNullOrWhiteSpace(oemInfName)) return "No driver specified.";
        try
        {
            var inf = oemInfName.Trim();
            if (inf.Contains(' ') && !inf.StartsWith("\""))
            {
                inf = $"\"{inf}\"";
            }
            var forceArg = force ? " /force" : "";
            var (output, err, code) = await ProcessHelper.RunProcessAsync("pnputil.exe", $"/delete-driver {inf} /uninstall{forceArg}");
            if (code == 0 || code == 3010)
            {
                var msg = string.IsNullOrWhiteSpace(output) ? "Driver package uninstalled successfully." : output.Trim();
                if (code == 3010) msg += " (Reboot required)";
                return msg;
            }
            return string.IsNullOrWhiteSpace(err) ? (string.IsNullOrWhiteSpace(output) ? $"Exit code: {code}" : output.Trim()) : err.Trim();
        }
        catch (Exception ex)
        {
            return $"Failed to delete driver: {ex.Message}";
        }
    }

    public async Task<BatchDriverDeleteResult> BatchDeleteDriversAsync(IEnumerable<string> oemInfNames, bool force = true)
    {
        var result = new BatchDriverDeleteResult();
        if (oemInfNames == null)
        {
            result.Summary = "No drivers provided.";
            return result;
        }

        var list = oemInfNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.TotalCount = list.Count;
        if (list.Count == 0)
        {
            result.Summary = "No driver packages to delete.";
            return result;
        }

        foreach (var inf in list)
        {
            var res = await DeleteDriverAsync(inf, force);
            result.Details.Add($"{inf}: {res}");
            if (res.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                res.Contains("成功", StringComparison.OrdinalIgnoreCase) ||
                res.Contains("正常", StringComparison.OrdinalIgnoreCase) ||
                res.Contains("順利", StringComparison.OrdinalIgnoreCase) ||
                res.Contains("Reboot required", StringComparison.OrdinalIgnoreCase) ||
                res.Contains("3010", StringComparison.OrdinalIgnoreCase))
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
            }
        }

        result.Summary = $"Processed {result.TotalCount} driver(s): {result.SuccessCount} succeeded, {result.FailureCount} failed.";
        return result;
    }
}

public class BatchDriverDeleteResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Details { get; set; } = new();
    public string Summary { get; set; } = "";
}
