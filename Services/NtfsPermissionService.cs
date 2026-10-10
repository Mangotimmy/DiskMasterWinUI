using System.Diagnostics;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class NtfsPermissionService
{
    private async Task<int> RunStreamingProcessAsync(
        string fileName,
        string arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutputLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutputLine($"[ERROR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit(200);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            onOutputLine("[INFO] Permission operation cancelled by user.");
            return -1;
        }
        catch (Exception ex)
        {
            onOutputLine($"[EXCEPTION] Failed to run {fileName}: {ex.Message}");
            return -1;
        }
    }

    public async Task<int> TakeOwnershipAsync(string path, bool recursive, Action<string> onOutput, CancellationToken ct = default)
    {
        var recFlag = recursive ? " /R /D Y" : "";
        var cleanPath = path.TrimEnd('\\');
        onOutput($"[{DateTime.Now:HH:mm:ss}] Running takeown /F \"{cleanPath}\"{recFlag}...");
        return await RunStreamingProcessAsync("takeown.exe", $"/F \"{cleanPath}\"{recFlag}", onOutput, ct);
    }

    public async Task<int> GrantPermissionAsync(
        string path,
        string principal,
        string accessLevel,
        string inheritance,
        bool recursive,
        bool continueOnError,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        var cleanPath = path.TrimEnd('\\');
        var recFlag = recursive ? " /T" : "";
        var errFlag = continueOnError ? " /C" : "";
        var args = $"\"{cleanPath}\" /grant:r \"{principal}\":{inheritance}({accessLevel}){recFlag}{errFlag}";

        onOutput($"[{DateTime.Now:HH:mm:ss}] Running icacls {args}...");
        return await RunStreamingProcessAsync("icacls.exe", args, onOutput, ct);
    }

    public async Task<int> ResetPermissionsAsync(string path, bool recursive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanPath = path.TrimEnd('\\');
        var recFlag = recursive ? " /T /C" : "";
        var args = $"\"{cleanPath}\" /reset{recFlag}";

        onOutput($"[{DateTime.Now:HH:mm:ss}] Running icacls {args}...");
        return await RunStreamingProcessAsync("icacls.exe", args, onOutput, ct);
    }

    public async Task<bool> ApplyPresetAsync(string path, NtfsPresetType preset, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanPath = path.TrimEnd('\\');
        onOutput($"══════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] Applying Preset: {preset} on \"{cleanPath}\"");
        onOutput($"══════════════════════════════════════════════════════");

        switch (preset)
        {
            case NtfsPresetType.TakeOwnershipAndUnlock:
                // Step 1: take ownership recursively
                onOutput("Step 1/2: Taking ownership of all files and folders...");
                await TakeOwnershipAsync(cleanPath, true, onOutput, ct);
                if (ct.IsCancellationRequested) return false;

                // Step 2: grant Administrators and current user Full Control
                onOutput("\nStep 2/2: Granting Administrators Full Control recursively...");
                var grantCode = await GrantPermissionAsync(cleanPath, "Administrators", "F", "(OI)(CI)", true, true, onOutput, ct);
                onOutput($"Preset TakeOwnershipAndUnlock completed with code {grantCode}.");
                return grantCode == 0;

            case NtfsPresetType.GrantEveryoneFullControl:
                onOutput("Granting Everyone Full Control recursively...");
                var codeEveryone = await GrantPermissionAsync(cleanPath, "Everyone", "F", "(OI)(CI)", true, true, onOutput, ct);
                onOutput($"Preset GrantEveryoneFullControl completed with code {codeEveryone}.");
                return codeEveryone == 0;

            case NtfsPresetType.ResetToDefaultInheritance:
                onOutput("Resetting all permissions to inherited defaults recursively...");
                var codeReset = await ResetPermissionsAsync(cleanPath, true, onOutput, ct);
                onOutput($"Preset ResetToDefaultInheritance completed with code {codeReset}.");
                return codeReset == 0;

            case NtfsPresetType.StrictAdministratorsOnly:
                onOutput("Step 1/2: Taking ownership...");
                await TakeOwnershipAsync(cleanPath, true, onOutput, ct);
                if (ct.IsCancellationRequested) return false;

                onOutput("\nStep 2/2: Resetting inheritance and restricting to Administrators and SYSTEM...");
                var codeStrict = await RunStreamingProcessAsync(
                    "icacls.exe",
                    $"\"{cleanPath}\" /inheritance:r /grant:r \"Administrators\":(OI)(CI)(F) /grant:r \"SYSTEM\":(OI)(CI)(F) /T /C",
                    onOutput,
                    ct);
                onOutput($"Preset StrictAdministratorsOnly completed with code {codeStrict}.");
                return codeStrict == 0;

            default:
                return false;
        }
    }

    public async Task<NtfsFeatureSummary> InspectNtfsFeaturesAsync(string path, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var summary = new NtfsFeatureSummary();
            var cleanPath = path.TrimEnd('\\');

            // 1. Check basic attributes
            try
            {
                if (File.Exists(cleanPath) || Directory.Exists(cleanPath))
                {
                    var attr = File.GetAttributes(cleanPath);
                    summary.IsCompressed = attr.HasFlag(FileAttributes.Compressed);
                    summary.IsEncrypted = attr.HasFlag(FileAttributes.Encrypted);
                    summary.IsSparse = attr.HasFlag(FileAttributes.SparseFile);
                    summary.IsReparsePoint = attr.HasFlag(FileAttributes.ReparsePoint);
                }
            }
            catch { }

            // 2. Query Owner and ACLs via icacls
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                var (outStr, _, _) = await ProcessHelper.RunProcessAsync("icacls.exe", $"\"{cleanPath}\"", cancellationToken: timeoutCts.Token);
                if (!string.IsNullOrWhiteSpace(outStr))
                {
                    var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("成功處理", StringComparison.OrdinalIgnoreCase)) continue;

                        // Match ACE pattern: Principal:(permissions)
                        var colonIdx = trimmed.LastIndexOf(':');
                        if (colonIdx > 0 && colonIdx < trimmed.Length - 1)
                        {
                            var principal = trimmed.Substring(0, colonIdx).Trim();
                            var rest = trimmed.Substring(colonIdx + 1).Trim();
                            bool isDeny = rest.Contains("(DENY)", StringComparison.OrdinalIgnoreCase);

                            summary.AclEntries.Add(new NtfsAceItem
                            {
                                Principal = principal,
                                AccessType = isDeny ? "Deny" : "Allow",
                                Permissions = rest,
                                IsInherited = rest.Contains("(I)")
                            });
                        }
                    }
                }
            }
            catch { }

            // 3. Query Alternate Data Streams via PowerShell Get-Item -Stream *
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                var psCmd = $"Get-Item -LiteralPath '{cleanPath.Replace("'", "''")}' -Stream * | Select-Object Stream, Length | ConvertTo-Csv -NoTypeInformation";
                var (streamOut, _, _) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"", cancellationToken: timeoutCts.Token);
                if (!string.IsNullOrWhiteSpace(streamOut))
                {
                    var lines = streamOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',').Select(p => p.Trim('"')).ToArray();
                        if (parts.Length >= 2)
                        {
                            var sName = parts[0];
                            long.TryParse(parts[1], out long len);
                            bool isZone = sName.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase);
                            if (isZone) summary.HasZoneIdentifier = true;

                            summary.AlternateStreams.Add(new AlternateDataStreamItem
                            {
                                StreamName = sName,
                                SizeBytes = len,
                                DisplaySize = TopLargeFileItem.FormatBytes(len),
                                IsZoneIdentifier = isZone
                            });
                        }
                    }
                }
            }
            catch { }

            // 4. Query Hardlinks via fsutil hardlink list
            try
            {
                if (File.Exists(cleanPath))
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
                    var (hlOut, _, _) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"hardlink list \"{cleanPath}\"", cancellationToken: timeoutCts.Token);
                    if (!string.IsNullOrWhiteSpace(hlOut))
                    {
                        var hlLines = hlOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => !l.Contains("hardlink", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        summary.HardlinkCount = Math.Max(1, hlLines.Count);
                        summary.HardlinkPaths = hlLines;
                    }
                }
            }
            catch { }

            return summary;
        }, ct);
    }

    public async Task<(bool Success, string Message)> RemoveDenyAclsAsync(string path)
    {
        var clean = path.TrimEnd('\\');
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("icacls.exe", $"\"{clean}\" /remove:d Everyone /remove:d Users /T /C");
        return (code == 0, code == 0 ? "All DENY permission overrides removed successfully." : errStr);
    }

    public async Task<(bool Success, string Message)> UnblockZoneIdentifierAsync(string path)
    {
        var clean = path.TrimEnd('\\').Replace("'", "''");
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"Unblock-File -LiteralPath '{clean}'\"");
        return (code == 0, code == 0 ? $"Internet download lock (Zone.Identifier) removed from {path}." : errStr);
    }

    public async Task<(bool Success, string Message)> BackupAclsAsync(string path, string aclSavePath)
    {
        var clean = path.TrimEnd('\\');
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("icacls.exe", $"\"{clean}\" /save \"{aclSavePath}\" /T /C");
        return (code == 0, code == 0 ? $"ACL security descriptors backed up to: {aclSavePath}" : errStr);
    }

    public async Task<(bool Success, string Message)> RestoreAclsAsync(string targetDir, string aclSavePath)
    {
        var clean = targetDir.TrimEnd('\\');
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("icacls.exe", $"\"{clean}\" /restore \"{aclSavePath}\" /C");
        return (code == 0, code == 0 ? "ACL security descriptors successfully restored!" : errStr);
    }
}
