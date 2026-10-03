using System.Diagnostics;
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
}
