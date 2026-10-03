using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class BcdManagerService
{
    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken);
    }

    public async Task<List<string>> ScanInstalledWindowsAsync()
    {
        var foundList = new List<string>();

        // 1. Scan all local drive letters
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var winDir = Path.Combine(drive.RootDirectory.FullName, "Windows");
                var kernel = Path.Combine(winDir, "System32", "ntoskrnl.exe");
                if (File.Exists(kernel))
                {
                    foundList.Add(winDir);
                }
            }
            catch { }
        }

        // 2. Try bootrec /scanos if available
        try
        {
            var bootrecPath = Path.Combine(Environment.SystemDirectory, "bootrec.exe");
            if (File.Exists(bootrecPath))
            {
                var scanOut = await RunCommandAsync(bootrecPath, "/scanos");
                var matches = Regex.Matches(scanOut, @"([A-Za-z]:\\Windows)", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    var path = m.Groups[1].Value;
                    if (!foundList.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        foundList.Add(path);
                    }
                }
            }
        }
        catch { }

        // 3. Deep Probe: Detect unlettered volumes and mount temporarily to search for Windows
        try
        {
            var unletteredFound = await DeepScanAndMountUnletteredPartitionsAsync();
            foreach (var p in unletteredFound)
            {
                if (!foundList.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    foundList.Add(p);
                }
            }
        }
        catch { }

        return foundList;
    }

    public async Task<List<string>> DeepScanAndMountUnletteredPartitionsAsync()
    {
        var discovered = new List<string>();
        var diskPart = new DiskPartService();
        var (vols, raw) = await diskPart.ListVolumesAsync();

        // Get used drive letters
        var usedLetters = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var candidateLetters = "WTVSRQPONMLKJ".ToCharArray();

        foreach (var v in vols)
        {
            if (!string.IsNullOrWhiteSpace(v.Letter)) continue; // Already has letter
            if (v.Type.Contains("CD", StringComparison.OrdinalIgnoreCase)) continue;

            // Find an unused letter
            char freeLetter = '\0';
            foreach (var c in candidateLetters)
            {
                if (!usedLetters.Contains(c))
                {
                    freeLetter = c;
                    break;
                }
            }
            if (freeLetter == '\0') break;

            // Temporarily assign letter
            var assignScript = $"select volume {v.Number}\r\nassign letter={freeLetter}";
            await diskPart.RunCustomScriptAsync(assignScript);
            usedLetters.Add(freeLetter);

            var rootPath = $"{freeLetter}:\\";
            var winDir = Path.Combine(rootPath, "Windows");
            var kernel = Path.Combine(winDir, "System32", "ntoskrnl.exe");
            var espBoot = Path.Combine(rootPath, "EFI", "Microsoft", "Boot", "bootmgfw.efi");

            if (File.Exists(kernel))
            {
                // Keep this letter mounted because it has Windows!
                discovered.Add($"{winDir} (Mounted Vol {v.Number} as {freeLetter}:)");
            }
            else if (File.Exists(espBoot))
            {
                discovered.Add($"[ESP Bootloader] {freeLetter}:\\EFI\\Microsoft\\Boot (Vol {v.Number})");
            }
            else
            {
                // Remove letter if neither Windows nor ESP
                await diskPart.RunCustomScriptAsync($"select volume {v.Number}\r\nremove");
                usedLetters.Remove(freeLetter);
            }
        }

        return discovered;
    }

    public async Task<(List<BootEntryDetail> Entries, string RawOutput)> EnumBootEntriesAsync()
    {
        var raw = await RunCommandAsync("bcdedit.exe", "/enum all");
        var entries = new List<BootEntryDetail>();

        if (string.IsNullOrWhiteSpace(raw) || raw.Contains("ERROR"))
        {
            return (entries, raw);
        }

        // Split blocks by dashed lines or headers
        var blocks = Regex.Split(raw, @"(?=^[\w\s\(\)]+\r?\n-+)|\r?\n\r?\n", RegexOptions.Multiline);

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            var idMatch = Regex.Match(block, @"identifier\s+(.+)", RegexOptions.IgnoreCase);
            if (!idMatch.Success) continue;

            var id = idMatch.Groups[1].Value.Trim();
            var entry = new BootEntryDetail
            {
                Identifier = id,
                RawText = block.Trim(),
                IsDefault = id.Equals("{default}", StringComparison.OrdinalIgnoreCase),
                IsCurrent = id.Equals("{current}", StringComparison.OrdinalIgnoreCase)
            };

            var descMatch = Regex.Match(block, @"description\s+(.+)", RegexOptions.IgnoreCase);
            if (descMatch.Success) entry.Description = descMatch.Groups[1].Value.Trim();

            var devMatch = Regex.Match(block, @"device\s+(.+)", RegexOptions.IgnoreCase);
            if (devMatch.Success) entry.Device = devMatch.Groups[1].Value.Trim();

            var pathMatch = Regex.Match(block, @"path\s+(.+)", RegexOptions.IgnoreCase);
            if (pathMatch.Success) entry.Path = pathMatch.Groups[1].Value.Trim();

            entries.Add(entry);
        }

        return (entries, raw);
    }

    public async Task<string> SetDefaultBootAsync(string identifier) =>
        await RunCommandAsync("bcdedit.exe", $"/default {identifier}");

    public async Task<string> SetDefaultEntryAsync(string identifier) =>
        await SetDefaultBootAsync(identifier);

    public async Task<string> SetDescriptionAsync(string identifier, string newDescription) =>
        await RunCommandAsync("bcdedit.exe", $"/set {identifier} description \"{newDescription}\"");

    public async Task<string> SetTimeoutAsync(int timeoutSeconds) =>
        await RunCommandAsync("bcdedit.exe", $"/timeout {timeoutSeconds}");

    public async Task<string> SetBootMenuPolicyAsync(bool modern) =>
        await RunCommandAsync("bcdedit.exe", $"/set {{current}} bootmenupolicy {(modern ? "Standard" : "Legacy")}");

    public async Task<string> CloneBootEntryAsync(string identifier, string newDescription) =>
        await RunCommandAsync("bcdedit.exe", $"/copy {identifier} /d \"{newDescription}\"");

    public async Task<string> CloneEntryAsync(string identifier, string newDescription) =>
        await CloneBootEntryAsync(identifier, newDescription);

    public async Task<string> DeleteBootEntryAsync(string identifier) =>
        await RunCommandAsync("bcdedit.exe", $"/delete {identifier} /cleanup");

    public async Task<string> DeleteEntryAsync(string identifier) =>
        await DeleteBootEntryAsync(identifier);

    public async Task<string> AddWindowsToBcdAsync(string windowsDir, string firmware = "UEFI") =>
        await RunCommandAsync("bcdboot.exe", $"\"{windowsDir}\" /f {firmware} /addlast");

    public async Task<string> BuildNewBootPartitionAsync(NewBootConfig config, Action<string>? logCallback = null) =>
        await CreateBootPartitionAndBcdAsync(config, logCallback);

    public async Task<string> CreateBootPartitionAndBcdAsync(NewBootConfig config, Action<string>? logCallback = null)
    {
        var sb = new StringBuilder();
        void Log(string msg)
        {
            sb.AppendLine(msg);
            logCallback?.Invoke(msg);
        }

        Log($"[1/4] Preparing target boot partition on Disk {config.TargetDiskNumber}...");

        var targetLetter = config.TargetPartitionLetter.TrimEnd(':', '\\') + ":";
        var letterChar = targetLetter[0];

        if (config.CreateNewPartition)
        {
            var diskPart = new DiskPartService();
            var dpScript = new StringBuilder();
            dpScript.AppendLine($"select disk {config.TargetDiskNumber}");

            if (config.FirmwareType.Equals("BIOS", StringComparison.OrdinalIgnoreCase))
            {
                dpScript.AppendLine($"create partition primary size={config.PartitionSizeMB}");
                dpScript.AppendLine("active");
                dpScript.AppendLine($"format fs=ntfs quick label=\"System\"");
            }
            else
            {
                dpScript.AppendLine($"create partition efi size={config.PartitionSizeMB}");
                dpScript.AppendLine($"format fs=fat32 quick label=\"System\"");
            }
            dpScript.AppendLine($"assign letter={letterChar}");

            Log($"▸ Running DiskPart creation script:\n{dpScript}");
            var dpResult = await diskPart.RunCustomScriptAsync(dpScript.ToString());
            Log(dpResult);
        }

        // Step 2: Write boot sector if requested/needed
        Log($"[2/4] Writing bootcode via bootsect on {targetLetter}...");
        var bsResult = await RunCommandAsync("bootsect.exe", $"/nt60 {targetLetter} /force /mbr");
        Log(bsResult);

        // Step 3: Run bcdboot to populate BCD
        Log($"[3/4] Creating new BCD boot files from {config.SourceWindowsPath} onto {targetLetter} (Mode: {config.FirmwareType})...");
        var bcdBootArgs = $"\"{config.SourceWindowsPath}\" /s {targetLetter} /f {config.FirmwareType} /v";
        var bbResult = await RunCommandAsync("bcdboot.exe", bcdBootArgs);
        Log(bbResult);

        // Step 4: Verification
        Log("[4/4] Verifying generated boot files...");
        var uefiBcd = Path.Combine($"{letterChar}:\\", "EFI", "Microsoft", "Boot", "BCD");
        var biosBcd = Path.Combine($"{letterChar}:\\", "Boot", "BCD");

        var uefiOk = File.Exists(uefiBcd);
        var biosOk = File.Exists(biosBcd);

        if (uefiOk || biosOk)
        {
            Log($"SUCCESS: Boot files verified! (UEFI BCD: {uefiOk}, BIOS BCD: {biosOk})");
        }
        else
        {
            Log("WARNING: BCD files not directly detected via file system (partition may require letter remount). bcdboot output should be checked.");
        }

        if (config.RemoveLetterAfterBuild)
        {
            Log($"▸ Unmounting temporary letter {targetLetter}...");
            await RunCommandAsync("mountvol.exe", $"{targetLetter} /d");
        }

        return sb.ToString();
    }
}
