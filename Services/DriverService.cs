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

    public async Task<string> DeleteDriverAsync(string oemInfName, bool force = false)
    {
        var forceArg = force ? " /force" : "";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("pnputil.exe", $"/delete-driver {oemInfName}{forceArg}");
        return string.IsNullOrWhiteSpace(output) ? err : output;
    }
}
