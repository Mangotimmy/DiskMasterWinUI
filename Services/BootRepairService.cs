using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class BootRepairService
{
    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken, includeExitCode: true);
    }

    // ─── bootrec.exe (WinRE/WinPE only) ───
    public async Task<string> BootrecFixMbrAsync() =>
        await RunCommandAsync("bootrec.exe", "/fixmbr");

    public async Task<string> BootrecFixBootAsync() =>
        await RunCommandAsync("bootrec.exe", "/fixboot");

    public async Task<string> BootrecScanOsAsync() =>
        await RunCommandAsync("bootrec.exe", "/scanos");

    public async Task<string> BootrecRebuildBcdAsync() =>
        await RunCommandAsync("bootrec.exe", "/rebuildbcd");

    // ─── bcdboot.exe ───
    public async Task<string> BcdBootAsync(string windowsDir = "C:\\Windows", string firmware = "UEFI") =>
        await RunCommandAsync("bcdboot.exe", $"\"{windowsDir}\" /s S: /f {firmware}");

    public async Task<string> BcdBootCustomAsync(string arguments) =>
        await RunCommandAsync("bcdboot.exe", arguments);

    // ─── bcdedit.exe ───
    public async Task<(List<BcdEntry> Entries, string RawOutput)> BcdEditEnumAsync()
    {
        var output = await RunCommandAsync("bcdedit.exe", "/enum all");
        return (OutputParser.ParseBcdEdit(output), output);
    }

    public async Task<string> BcdEditSetDefaultAsync(string identifier) =>
        await RunCommandAsync("bcdedit.exe", $"/default {identifier}");

    public async Task<string> BcdEditSetTimeoutAsync(int seconds) =>
        await RunCommandAsync("bcdedit.exe", $"/timeout {seconds}");

    public async Task<string> BcdEditDeleteEntryAsync(string identifier) =>
        await RunCommandAsync("bcdedit.exe", $"/delete {identifier}");

    public async Task<string> BcdEditCustomAsync(string arguments) =>
        await RunCommandAsync("bcdedit.exe", arguments);

    public async Task<string> BcdEditSetSafeModeAsync(string mode)
    {
        if (string.Equals(mode, "clear", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "normal", StringComparison.OrdinalIgnoreCase))
        {
            return await RunCommandAsync("bcdedit.exe", "/deletevalue {default} safeboot");
        }
        return await RunCommandAsync("bcdedit.exe", $"/set {{default}} safeboot {mode}");
    }

    public async Task<string> BcdEditSetHypervisorAsync(bool enable)
    {
        var val = enable ? "auto" : "off";
        return await RunCommandAsync("bcdedit.exe", $"/set {{default}} hypervisorlaunchtype {val}");
    }

    public async Task<string> BcdEditSetTestSigningAsync(bool enable)
    {
        var val = enable ? "on" : "off";
        return await RunCommandAsync("bcdedit.exe", $"/set testsigning {val}");
    }

    // ─── bootsect.exe ───
    public async Task<string> BootSectNt60Async(string drive = "S:") =>
        await RunCommandAsync("bootsect.exe", $"/nt60 {drive}");

    public async Task<string> BootSectNt52Async(string drive = "S:") =>
        await RunCommandAsync("bootsect.exe", $"/nt52 {drive}");

    // ─── Full Auto Repair ───
    public async Task<string> AutoRepairBootAsync(string windowsDir = "C:\\Windows")
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ Auto Boot Repair ═══");
        sb.AppendLine();

        sb.AppendLine("▸ Step 1: bootrec /fixmbr");
        sb.AppendLine(await BootrecFixMbrAsync());

        sb.AppendLine("▸ Step 2: bootrec /fixboot");
        sb.AppendLine(await BootrecFixBootAsync());

        sb.AppendLine("▸ Step 3: bootrec /scanos");
        sb.AppendLine(await BootrecScanOsAsync());

        sb.AppendLine("▸ Step 4: bootrec /rebuildbcd");
        sb.AppendLine(await BootrecRebuildBcdAsync());

        sb.AppendLine("▸ Step 5: bcdboot");
        sb.AppendLine(await BcdBootAsync(windowsDir));

        sb.AppendLine("═══ Auto Boot Repair Complete ═══");
        return sb.ToString();
    }

    public static bool IsBootrecAvailable()
    {
        return File.Exists(Path.Combine(Environment.SystemDirectory, "bootrec.exe"));
    }

    public static bool IsBcdBootAvailable()
    {
        return File.Exists(Path.Combine(Environment.SystemDirectory, "bcdboot.exe"));
    }

    // ─── Phase 5: WinRE (reagentc.exe) & EFI (mountvol.exe) ───

    public async Task<string> ReagentcInfoAsync() =>
        await RunCommandAsync("reagentc.exe", "/info");

    public async Task<string> ReagentcEnableAsync() =>
        await RunCommandAsync("reagentc.exe", "/enable");

    public async Task<string> ReagentcDisableAsync() =>
        await RunCommandAsync("reagentc.exe", "/disable");

    public async Task<string> ReagentcSetReimageAsync(string path, string target) =>
        await RunCommandAsync("reagentc.exe", $"/setreimage /path \"{path}\" /target \"{target}\"");

    public async Task<string> ReagentcBootToReAsync() =>
        await RunCommandAsync("reagentc.exe", "/boottoore");

    public async Task<string> MountEspAsync(string driveLetter = "S:")
    {
        var clean = driveLetter.TrimEnd('\\', ':') + ":";
        return await RunCommandAsync("mountvol.exe", $"{clean} /s");
    }

    public async Task<string> UnmountEspAsync(string driveLetter = "S:")
    {
        var clean = driveLetter.TrimEnd('\\', ':') + ":";
        return await RunCommandAsync("mountvol.exe", $"{clean} /d");
    }

    public async Task<string> BcdEditExportAsync(string filePath) =>
        await RunCommandAsync("bcdedit.exe", $"/export \"{filePath}\"");

    public async Task<string> BcdEditImportAsync(string filePath) =>
        await RunCommandAsync("bcdedit.exe", $"/import \"{filePath}\" /clean");

    public async Task<string> BcdBootCleanAsync(string windowsDir = "C:\\Windows", string espDrive = "S:", string firmware = "UEFI") =>
        await RunCommandAsync("bcdboot.exe", $"\"{windowsDir}\" /s {espDrive} /f {firmware} /c");

    public async Task<string> BcdEditFwBootMgrAsync() =>
        await RunCommandAsync("bcdedit.exe", "/enum {fwbootmgr}");
}
