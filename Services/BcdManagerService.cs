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

    public async Task<List<string>> ScanInstalledWindowsAsync(CancellationToken cancellationToken = default)
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
                var scanOut = await RunCommandAsync(bootrecPath, "/scanos", cancellationToken);
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
            var unletteredFound = await DeepScanAndMountUnletteredPartitionsAsync(cancellationToken);
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

    /// <summary>
    /// B10: a "read-only" deep scan temporarily mounts unlettered volumes, so every assigned drive letter is
    /// tracked in a rollback list and released in a finally - on success, on exception and on cancellation.
    /// diskpart output is inspected (and the letter verified against the mounted drives) before an
    /// assignment is trusted, so a failed assign no longer silently counts as a mounted volume.
    /// </summary>
    public async Task<List<string>> DeepScanAndMountUnletteredPartitionsAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<string>();
        var diskPart = new DiskPartService();
        var (vols, raw) = await diskPart.ListVolumesAsync(cancellationToken);

        // Get used drive letters
        var usedLetters = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var candidateLetters = "WTVSRQPONMLKJ".ToCharArray();

        // Every letter this scan mounts is registered here and always released in the finally below.
        var mountedLetters = new List<(int VolumeNumber, char Letter)>();

        try
        {
            foreach (var v in vols)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                // Temporarily assign letter - the result is verified before the volume is treated as mounted.
                var assignScript = $"select volume {v.Number}\r\nassign letter={freeLetter}";
                var assignOutput = await diskPart.RunCustomScriptAsync(assignScript, cancellationToken);
                var assigned = !DiskPartReportedError(assignOutput) &&
                               await WaitForDriveLetterAsync(freeLetter, present: true, cancellationToken);
                if (!assigned)
                {
                    discovered.Add($"[掛載失敗] Volume {v.Number} 無法指派暫時代號 {freeLetter}: — {SummarizeDiskPartOutput(assignOutput)}");
                    continue;
                }

                usedLetters.Add(freeLetter);
                mountedLetters.Add((v.Number, freeLetter));

                var rootPath = $"{freeLetter}:\\";
                var winDir = Path.Combine(rootPath, "Windows");
                var kernel = Path.Combine(winDir, "System32", "ntoskrnl.exe");
                var espBoot = Path.Combine(rootPath, "EFI", "Microsoft", "Boot", "bootmgfw.efi");

                if (File.Exists(kernel))
                {
                    // Report the finding; the temporary letter is released again at the end of the scan.
                    discovered.Add($"{winDir} (Windows 安裝於 Vol {v.Number}，掃描暫用代號 {freeLetter}:，掃描結束後已自動釋放)");
                }
                else if (File.Exists(espBoot))
                {
                    discovered.Add($"[ESP Bootloader] {freeLetter}:\\EFI\\Microsoft\\Boot (Vol {v.Number}，掃描暫用代號已自動釋放)");
                }
                else
                {
                    // Remove letter if neither Windows nor ESP - verify the removal and report failures.
                    var removeOutput = await diskPart.RunCustomScriptAsync($"select volume {v.Number}\r\nremove", cancellationToken);
                    var removed = !DiskPartReportedError(removeOutput) &&
                                  await WaitForDriveLetterAsync(freeLetter, present: false, cancellationToken);
                    if (removed)
                    {
                        mountedLetters.RemoveAll(m => m.VolumeNumber == v.Number && m.Letter == freeLetter);
                        usedLetters.Remove(freeLetter);
                    }
                    else
                    {
                        discovered.Add($"[卸載失敗] Volume {v.Number} 的暫時代號 {freeLetter}: 可能仍存在 — {SummarizeDiskPartOutput(removeOutput)}");
                    }
                }
            }
        }
        finally
        {
            // Rollback runs with CancellationToken.None: a cancelled or failed scan must still release
            // every letter it mounted, otherwise the machine is left with unexplained drive letters.
            foreach (var (volumeNumber, letter) in mountedLetters.ToList())
            {
                try
                {
                    if (!DriveLetterPresent(letter)) continue;

                    var rollback = await diskPart.RunCustomScriptAsync($"select volume {volumeNumber}\r\nremove", CancellationToken.None);
                    var released = !DiskPartReportedError(rollback) &&
                                   await WaitForDriveLetterAsync(letter, present: false, CancellationToken.None, timeoutMs: 2000);
                    discovered.Add(released
                        ? $"[已還原] Volume {volumeNumber} 的掃描暫用代號 {letter}: 已釋放。"
                        : $"[還原失敗] 無法釋放 Volume {volumeNumber} 的掃描暫用代號 {letter}:，請手動執行 diskpart remove — {SummarizeDiskPartOutput(rollback)}");
                }
                catch (Exception ex)
                {
                    discovered.Add($"[還原失敗] 釋放 Volume {volumeNumber} 的掃描暫用代號 {letter}: 時發生例外：{ex.Message}");
                }
            }
        }

        return discovered;
    }

    /// <summary>True when diskpart text contains any known failure marker (localized + English).</summary>
    private static bool DiskPartReportedError(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        var markers = new[]
        {
            "Virtual Disk Service error", "DiskPart has encountered an error", "DiskPart 發生錯誤",
            "發生錯誤", "錯誤", "failed", "failure", "失敗", "Access is denied", "拒絕存取", "找不到", "not found"
        };
        return markers.Any(m => output.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static string SummarizeDiskPartOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "(no output)";
        var line = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                         .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "(no output)";
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private static bool DriveLetterPresent(char letter)
    {
        try
        {
            return DriveInfo.GetDrives().Any(d => char.ToUpperInvariant(d.Name[0]) == char.ToUpperInvariant(letter));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Polls until the drive letter appears (present=true) or disappears (present=false).</summary>
    private static async Task<bool> WaitForDriveLetterAsync(char letter, bool present, CancellationToken cancellationToken, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (DriveLetterPresent(letter) == present) return true;
            if (sw.ElapsedMilliseconds >= timeoutMs) return DriveLetterPresent(letter) == present;
            await Task.Delay(250, cancellationToken);
        }
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

    public async Task<string> BuildNewBootPartitionAsync(NewBootConfig config, Action<string>? logCallback = null, CancellationToken cancellationToken = default) =>
        await CreateBootPartitionAndBcdAsync(config, logCallback, cancellationToken);

    /// <summary>
    /// B11: every external tool on the boot-repair path is now checked by exit code (diskpart by its output
    /// text, bootsect/bcdboot/mountvol by their process exit code) and the SUCCESS verdict requires all of
    /// them to have succeeded AND the BCD to exist. A failed bcdboot can no longer print SUCCESS.
    /// </summary>
    public async Task<string> CreateBootPartitionAndBcdAsync(NewBootConfig config, Action<string>? logCallback = null, CancellationToken cancellationToken = default)
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

        var diskPartOk = !config.CreateNewPartition;   // nothing to create/assign when the partition already exists
        var bootsectOk = false;
        var bcdbootOk = false;
        var cancelled = false;
        var bcdPresent = false;
        var uefiOk = false;
        var biosOk = false;
        var bootsectCode = -1;
        var bcdbootCode = -1;
        var mountvolCode = -1;

        try
        {
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
                var dpResult = await diskPart.RunCustomScriptAsync(dpScript.ToString(), cancellationToken);
                Log(dpResult);
                diskPartOk = !DiskPartReportedError(dpResult);
                if (!diskPartOk)
                    Log("❌ DiskPart 建立分割區 / 格式化 / 指派磁碟機代號失敗，已跳過 bootsect 與 bcdboot。");
            }

            if (diskPartOk)
            {
                // Step 2: Write boot sector if requested/needed
                Log($"[2/4] Writing bootcode via bootsect on {targetLetter}...");
                var (bsOut, bsErr, bsCode) = await ProcessHelper.RunProcessAsync(
                    "bootsect.exe", $"/nt60 {targetLetter} /force /mbr", cancellationToken: cancellationToken);
                bootsectCode = bsCode;
                Log(string.IsNullOrWhiteSpace(bsOut) ? "(no stdout)" : bsOut.TrimEnd());
                if (!string.IsNullOrWhiteSpace(bsErr)) Log($"[stderr] {bsErr.TrimEnd()}");
                bootsectOk = bsCode == 0;
                Log(bootsectOk ? "✔ bootsect 完成 (exit code 0)。" : $"❌ bootsect 失敗 (exit code {bsCode})。");

                if (bootsectOk)
                {
                    // Step 3: Run bcdboot to populate BCD
                    Log($"[3/4] Creating new BCD boot files from {config.SourceWindowsPath} onto {targetLetter} (Mode: {config.FirmwareType})...");
                    var bcdBootArgs = $"\"{config.SourceWindowsPath}\" /s {targetLetter} /f {config.FirmwareType} /v";
                    var (bbOut, bbErr, bbCode) = await ProcessHelper.RunProcessAsync(
                        "bcdboot.exe", bcdBootArgs, cancellationToken: cancellationToken);
                    bcdbootCode = bbCode;
                    Log(string.IsNullOrWhiteSpace(bbOut) ? "(no stdout)" : bbOut.TrimEnd());
                    if (!string.IsNullOrWhiteSpace(bbErr)) Log($"[stderr] {bbErr.TrimEnd()}");
                    bcdbootOk = bbCode == 0;
                    Log(bcdbootOk ? "✔ bcdboot 完成 (exit code 0)。" : $"❌ bcdboot 失敗 (exit code {bbCode})。");
                }
                else
                {
                    Log("❌ 已跳過 bcdboot（bootsect 未成功）。");
                }
            }

            // Step 4: Verification
            Log("[4/4] Verifying generated boot files...");
            var uefiBcd = Path.Combine($"{letterChar}:\\", "EFI", "Microsoft", "Boot", "BCD");
            var biosBcd = Path.Combine($"{letterChar}:\\", "Boot", "BCD");

            uefiOk = File.Exists(uefiBcd);
            biosOk = File.Exists(biosBcd);
            bcdPresent = uefiOk || biosOk;
            Log($"▸ UEFI BCD: {(uefiOk ? "found" : "not found")} ({uefiBcd})");
            Log($"▸ BIOS BCD: {(biosOk ? "found" : "not found")} ({biosBcd})");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            Log("⏹️ 已取消開機分割區建立。");
        }
        finally
        {
            if (config.RemoveLetterAfterBuild)
            {
                Log($"▸ Unmounting temporary letter {targetLetter}...");
                var (mvOut, mvErr, mvCode) = await ProcessHelper.RunProcessAsync("mountvol.exe", $"{targetLetter} /d");
                mountvolCode = mvCode;
                if (!string.IsNullOrWhiteSpace(mvOut)) Log(mvOut.TrimEnd());
                if (mvCode != 0)
                    Log($"⚠️ mountvol 卸載暫時代號失敗 (exit code {mvCode}){(string.IsNullOrWhiteSpace(mvErr) ? "" : $": {mvErr.Trim()}")} — {targetLetter} 可能仍存在。");
            }
        }

        var success = !cancelled && diskPartOk && bootsectOk && bcdbootOk && bcdPresent;

        Log("");
        if (success)
        {
            Log($"SUCCESS: Boot files verified! (DiskPart OK, bootsect exit 0, bcdboot exit 0, UEFI BCD: {uefiOk}, BIOS BCD: {biosOk}" +
                (config.RemoveLetterAfterBuild ? $", mountvol exit {mountvolCode}" : "") + ")");
        }
        else
        {
            var reasons = new List<string>();
            if (cancelled) reasons.Add("作業被取消");
            if (!diskPartOk) reasons.Add("DiskPart 建立/格式化/指派代號失敗");
            if (!bootsectOk) reasons.Add($"bootsect 失敗 (exit code {bootsectCode})");
            if (!bcdbootOk) reasons.Add($"bcdboot 失敗 (exit code {bcdbootCode})");
            if (!bcdPresent) reasons.Add($"在 {targetLetter} 找不到 EFI\\Microsoft\\Boot\\BCD 或 Boot\\BCD（分割區可能需要重新掛載代號）");
            Log("FAILED: 開機分割區建立未完成 — " + string.Join("；", reasons));
            Log("請檢查上方 DiskPart / bootsect / bcdboot 的輸出後再重試（此訊息取代先前的 SUCCESS）。");
        }

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════
    // MSConfig-Style Safe Boot & Advanced Boot Options
    // ══════════════════════════════════════════════════════════════

    public async Task<SafeBootConfig> GetSafeBootConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = new SafeBootConfig();
        try
        {
            var output = await RunCommandAsync("bcdedit.exe", "/enum {current}", cancellationToken);
            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var parts = Regex.Split(trimmed, @"\s{2,}");
                if (parts.Length < 2)
                {
                    var match = Regex.Match(trimmed, @"^([a-zA-Z0-9_-]+)\s+(.+)$");
                    if (match.Success)
                    {
                        parts = new[] { match.Groups[1].Value, match.Groups[2].Value };
                    }
                    else
                    {
                        continue;
                    }
                }

                var key = parts[0].Trim().ToLowerInvariant();
                var val = parts[1].Trim().ToLowerInvariant();

                switch (key)
                {
                    case "safeboot":
                        config.SafeBootMode = val switch
                        {
                            "minimal" => "Minimal",
                            "network" => "Network",
                            "alternateshell" => "AlternateShell",
                            "dsrepair" => "DsRepair",
                            _ => "Normal"
                        };
                        break;
                    case "noguiboot":
                        config.NoGuiBoot = val is "yes" or "true" or "on";
                        break;
                    case "bootlog":
                        config.BootLog = val is "yes" or "true" or "on";
                        break;
                    case "basevideo":
                        config.BaseVideo = val is "yes" or "true" or "on";
                        break;
                    case "sos":
                        config.Sos = val is "yes" or "true" or "on";
                        break;
                    case "testsigning":
                        config.TestSigning = val is "yes" or "true" or "on";
                        break;
                    case "nointegritychecks":
                        config.NoIntegrityChecks = val is "yes" or "true" or "on";
                        break;
                    case "hypervisorlaunchtype":
                        config.HypervisorLaunchType = val;
                        break;
                }
            }
        }
        catch { }

        return config;
    }

    public async Task<string> SetSafeBootModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var m = mode?.Trim().ToLowerInvariant() ?? "normal";
        var sb = new StringBuilder();

        switch (m)
        {
            case "minimal":
                await RunCommandAsync("bcdedit.exe", "/deletevalue {current} safebootalternateshell", cancellationToken);
                var resMin = await RunCommandAsync("bcdedit.exe", "/set {current} safeboot minimal", cancellationToken);
                sb.AppendLine(resMin);
                break;
            case "alternateshell":
                var resAlt = await RunCommandAsync("bcdedit.exe", "/set {current} safeboot alternateshell", cancellationToken);
                sb.AppendLine(resAlt);
                break;
            case "network":
                await RunCommandAsync("bcdedit.exe", "/deletevalue {current} safebootalternateshell", cancellationToken);
                var resNet = await RunCommandAsync("bcdedit.exe", "/set {current} safeboot network", cancellationToken);
                sb.AppendLine(resNet);
                break;
            case "dsrepair":
                await RunCommandAsync("bcdedit.exe", "/deletevalue {current} safebootalternateshell", cancellationToken);
                var resDs = await RunCommandAsync("bcdedit.exe", "/set {current} safeboot dsrepair", cancellationToken);
                sb.AppendLine(resDs);
                break;
            case "normal":
                var resNorm = await RunCommandAsync("bcdedit.exe", "/deletevalue {current} safeboot", cancellationToken);
                await RunCommandAsync("bcdedit.exe", "/deletevalue {current} safebootalternateshell", cancellationToken);
                sb.AppendLine(resNorm);
                break;
            default:
                throw new ArgumentException($"Unknown safe boot mode: {mode}");
        }

        return sb.ToString().Trim();
    }

    public async Task<string> SetBootFlagAsync(string flag, bool enable, CancellationToken cancellationToken = default)
    {
        var f = flag?.Trim().ToLowerInvariant() ?? "";
        string args = f switch
        {
            "testsigning" => $"/set {{current}} testsigning {(enable ? "on" : "off")}",
            "nointegritychecks" => $"/set {{current}} nointegritychecks {(enable ? "on" : "off")}",
            "hypervisorlaunchtype" => $"/set {{current}} hypervisorlaunchtype {(enable ? "auto" : "off")}",
            _ => enable ? $"/set {{current}} {f} yes" : $"/deletevalue {{current}} {f}"
        };

        return await RunCommandAsync("bcdedit.exe", args, cancellationToken);
    }

    public async Task<string> ApplySafeBootConfigAsync(SafeBootConfig config, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var modeResult = await SetSafeBootModeAsync(config.SafeBootMode, cancellationToken);
        sb.AppendLine(modeResult);

        sb.AppendLine(await SetBootFlagAsync("noguiboot", config.NoGuiBoot, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("bootlog", config.BootLog, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("basevideo", config.BaseVideo, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("sos", config.Sos, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("testsigning", config.TestSigning, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("nointegritychecks", config.NoIntegrityChecks, cancellationToken));
        sb.AppendLine(await SetBootFlagAsync("hypervisorlaunchtype", config.HypervisorLaunchType.Equals("auto", StringComparison.OrdinalIgnoreCase), cancellationToken));

        return sb.ToString().Trim();
    }
}
