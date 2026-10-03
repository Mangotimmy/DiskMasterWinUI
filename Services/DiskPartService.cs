using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class DiskPartService
{
    private static readonly SemaphoreSlim _diskPartLock = new(1, 1);

    private async Task<string> RunDiskPartAsync(string script, int timeoutSeconds = 60)
    {
        await _diskPartLock.WaitAsync();
        var tempFile = Path.Combine(Path.GetTempPath(), $"dm_{Guid.NewGuid():N}.txt");
        try
        {
            // Use Encoding.Default (or UTF8) so non-English characters in volume labels aren't corrupted
            var encoding = ProcessHelper.GetConsoleEncoding();
            await File.WriteAllTextAsync(tempFile, script + "\r\nexit\r\n", encoding);

            var psi = new ProcessStartInfo
            {
                FileName = "diskpart.exe",
                Arguments = $"/s \"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Configurable timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cts.Token);
                // Allow a brief moment for asynchronous streams to flush
                process.WaitForExit(300);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return $"ERROR: DiskPart timed out after {timeoutSeconds} seconds.\n\nPartial output:\n{stdout}\n{stderr}";
            }

            var result = stdout.ToString();
            if (stderr.Length > 0)
                result += $"\n[STDERR] {stderr}";

            return result;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
            _diskPartLock.Release();
        }
    }

    /// <summary>
    /// Batches "list disk" and "list volume" in a single diskpart session to avoid duplicate VDS initialization.
    /// </summary>
    public async Task<(List<DiskInfo> Disks, List<VolumeInfo> Volumes, string RawOutput)> ListDisksAndVolumesAsync()
    {
        var script = "list disk\r\nlist volume";
        var output = await RunDiskPartAsync(script);
        return (OutputParser.ParseDisks(output), OutputParser.ParseVolumes(output), output);
    }

    public async Task<(List<DiskInfo> Disks, string RawOutput)> ListDisksAsync()
    {
        var output = await RunDiskPartAsync("list disk");
        return (OutputParser.ParseDisks(output), output);
    }

    public async Task<(List<VolumeInfo> Volumes, string RawOutput)> ListVolumesAsync()
    {
        var output = await RunDiskPartAsync("list volume");
        return (OutputParser.ParseVolumes(output), output);
    }

    public async Task<(List<PartitionInfo> Partitions, string RawOutput)> ListPartitionsAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\nlist partition";
        var output = await RunDiskPartAsync(script);
        return (OutputParser.ParsePartitions(output), output);
    }

    public async Task<string> CleanDiskAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\nclean";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CleanDiskAllAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\nclean all";
        // Clean all can take hours on TB drives - allow up to 24 hours
        return await RunDiskPartAsync(script, timeoutSeconds: 86400);
    }

    public async Task<string> InitializeDiskAsync(int diskNumber, bool asGpt)
    {
        var style = asGpt ? "gpt" : "mbr";
        var script = $"select disk {diskNumber}\r\nconvert {style}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreatePrimaryPartitionAsync(int diskNumber, int? sizeMB = null)
    {
        var sizeArg = sizeMB.HasValue ? $" size={sizeMB.Value}" : "";
        var script = $"select disk {diskNumber}\r\ncreate partition primary{sizeArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateEfiPartitionAsync(int diskNumber, int sizeMB = 260)
    {
        var script = $"select disk {diskNumber}\r\ncreate partition efi size={sizeMB}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateMsrPartitionAsync(int diskNumber, int sizeMB = 16)
    {
        var script = $"select disk {diskNumber}\r\ncreate partition msr size={sizeMB}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> FormatVolumeAsync(int volumeNumber, string fileSystem = "NTFS", string label = "", bool quickFormat = true)
    {
        var labelArg = string.IsNullOrEmpty(label) ? "" : $" label=\"{label}\"";
        var quickArg = quickFormat ? " quick" : "";
        var script = $"select volume {volumeNumber}\r\nformat fs={fileSystem}{labelArg}{quickArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> AssignLetterAsync(int volumeNumber, char letter)
    {
        var script = $"select volume {volumeNumber}\r\nassign letter={letter}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> RemoveLetterAsync(int volumeNumber)
    {
        var script = $"select volume {volumeNumber}\r\nremove";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> DeletePartitionAsync(int diskNumber, int partitionNumber, bool force = false)
    {
        var overrideArg = force ? " override" : "";
        var script = $"select disk {diskNumber}\r\nselect partition {partitionNumber}\r\ndelete partition{overrideArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> SetActivePartitionAsync(int diskNumber, int partitionNumber)
    {
        var script = $"select disk {diskNumber}\r\nselect partition {partitionNumber}\r\nactive";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> ExtendVolumeAsync(int volumeNumber, int? sizeMB = null)
    {
        var sizeArg = sizeMB.HasValue ? $" size={sizeMB.Value}" : "";
        var script = $"select volume {volumeNumber}\r\nextend{sizeArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> ShrinkVolumeAsync(int volumeNumber, int sizeMB)
    {
        var script = $"select volume {volumeNumber}\r\nshrink desired={sizeMB}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> DetailDiskAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\ndetail disk";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> DetailVolumeAsync(int volumeNumber)
    {
        var script = $"select volume {volumeNumber}\r\ndetail volume";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> ClearDiskReadOnlyAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\nattributes disk clear readonly";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> SetDiskOnlineOfflineAsync(int diskNumber, bool online)
    {
        var cmd = online ? "online disk" : "offline disk";
        var script = $"select disk {diskNumber}\r\n{cmd}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> RescanDisksAsync()
    {
        return await RunDiskPartAsync("rescan");
    }

    public async Task<string> RunCustomScriptAsync(string script)
    {
        return await RunDiskPartAsync(script);
    }

    // ── Phase 3: Advanced DiskPart Operations ──

    public async Task<string> ExtendFilesystemAsync(int volumeNumber)
    {
        var script = $"select volume {volumeNumber}\r\nextend filesystem";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateExtendedPartitionAsync(int diskNumber, int? sizeMB = null)
    {
        var sizeArg = sizeMB.HasValue ? $" size={sizeMB.Value}" : "";
        var script = $"select disk {diskNumber}\r\ncreate partition extended{sizeArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateLogicalPartitionAsync(int diskNumber, int? sizeMB = null)
    {
        var sizeArg = sizeMB.HasValue ? $" size={sizeMB.Value}" : "";
        var script = $"select disk {diskNumber}\r\ncreate partition logical{sizeArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> SetPartitionIdAsync(int diskNumber, int partitionNumber, string id)
    {
        var script = $"select disk {diskNumber}\r\nselect partition {partitionNumber}\r\nset id={id}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> SetGptAttributesAsync(int diskNumber, int partitionNumber, string attributesHex)
    {
        var script = $"select disk {diskNumber}\r\nselect partition {partitionNumber}\r\ngpt attributes={attributesHex}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> GetUniqueIdAsync(int diskNumber)
    {
        var script = $"select disk {diskNumber}\r\nuniqueid disk";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> SetUniqueIdAsync(int diskNumber, string id)
    {
        var script = $"select disk {diskNumber}\r\nuniqueid disk id={id}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> ShrinkQueryMaxAsync(int volumeNumber)
    {
        var script = $"select volume {volumeNumber}\r\nshrink querymax";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> FormatWithClusterSizeAsync(int volumeNumber, string fileSystem, int clusterSize, string label = "", bool quick = true)
    {
        var labelArg = string.IsNullOrEmpty(label) ? "" : $" label=\"{label}\"";
        var quickArg = quick ? " quick" : "";
        var script = $"select volume {volumeNumber}\r\nformat fs={fileSystem} unit={clusterSize}{labelArg}{quickArg}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateRecoveryPartitionAsync(int diskNumber, int sizeMB = 1000)
    {
        var script = $"select disk {diskNumber}\r\ncreate partition primary size={sizeMB}\r\nformat quick fs=ntfs label=\"Recovery\"\r\nset id=de94bba4-06d1-4d40-a16a-bfd50179d6ac\r\ngpt attributes=0x8000000000000001";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> CreateStandardUefiLayoutAsync(int diskNumber, int efiMB = 260, int recoveryMB = 1000)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"select disk {diskNumber}");
        sb.AppendLine("clean");
        sb.AppendLine("convert gpt");
        sb.AppendLine($"create partition efi size={efiMB}");
        sb.AppendLine("format quick fs=fat32 label=\"System\"");
        sb.AppendLine("assign letter=S");
        sb.AppendLine("create partition msr size=16");
        sb.AppendLine("create partition primary");
        sb.AppendLine($"shrink minimum={recoveryMB}");
        sb.AppendLine("format quick fs=ntfs label=\"Windows\"");
        sb.AppendLine("assign letter=W");
        sb.AppendLine("create partition primary");
        sb.AppendLine("format quick fs=ntfs label=\"Recovery\"");
        sb.AppendLine("set id=de94bba4-06d1-4d40-a16a-bfd50179d6ac");
        sb.AppendLine("gpt attributes=0x8000000000000001");
        return await RunDiskPartAsync(sb.ToString(), timeoutSeconds: 300);
    }

    public async Task<string> SetVolumeAttributesAsync(int volumeNumber, string attribute, bool set)
    {
        var action = set ? "set" : "clear";
        var script = $"select volume {volumeNumber}\r\nattributes volume {action} {attribute}";
        return await RunDiskPartAsync(script);
    }

    public async Task<string> AssignMountPointAsync(int volumeNumber, string mountPath)
    {
        var script = $"select volume {volumeNumber}\r\nassign mount=\"{mountPath}\"";
        return await RunDiskPartAsync(script);
    }
}
