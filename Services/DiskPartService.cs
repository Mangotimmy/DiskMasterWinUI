using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public class DiskPartService
{
    private static readonly SemaphoreSlim _diskPartLock = new(1, 1);

    /// <summary>File systems DiskMaster is allowed to pass to "format fs=..." (allow-list, never free text).</summary>
    public static readonly string[] SupportedFileSystems = { "NTFS", "FAT32", "exFAT", "ReFS" };

    private static readonly int[] NtfsClusterSizes = { 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };
    private static readonly int[] Fat32ClusterSizes = { 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };
    private static readonly int[] ExFatClusterSizes = { 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144, 524288, 1048576, 2097152, 4194304, 8388608, 16777216, 33554432 };
    private static readonly int[] ReFsClusterSizes = { 4096, 65536 };

    private const uint DRIVE_REMOVABLE = 2;
    private const uint DRIVE_CDROM = 5;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    private static extern uint GetDriveType(string lpRootPathName);

    /// <summary>Normalizes a user supplied file system name against the allow-list ("" when unsupported).</summary>
    public static string CanonicalFileSystem(string? fileSystem)
    {
        if (string.IsNullOrWhiteSpace(fileSystem)) return "";
        return SupportedFileSystems.FirstOrDefault(fs => fs.Equals(fileSystem.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    /// <summary>Allowed cluster sizes (bytes) for a file system; empty means "only the default".</summary>
    public static int[] GetAllowedClusterSizes(string? fileSystem) => CanonicalFileSystem(fileSystem) switch
    {
        "NTFS" => NtfsClusterSizes,
        "FAT32" => Fat32ClusterSizes,
        "exFAT" => ExFatClusterSizes,
        "ReFS" => ReFsClusterSizes,
        _ => Array.Empty<int>()
    };

    /// <summary>
    /// Pre-flight ReFS availability check: the refs.sys driver must be installed and the Windows edition
    /// must be one that ships client ReFS (Pro for Workstations / Enterprise) or a Server SKU.
    /// Returns a human readable reason instead of letting diskpart emit a raw error.
    /// </summary>
    public static (bool Supported, string Reason) CheckReFsSupport()
    {
        string product = "", edition = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            product = key?.GetValue("ProductName")?.ToString() ?? "";
            edition = key?.GetValue("EditionID")?.ToString() ?? "";
        }
        catch (Exception ex)
        {
            return (false, $"無法讀取 Windows 版本資訊，無法確認 ReFS 支援：{ex.Message}");
        }

        var refsDriver = Path.Combine(Environment.SystemDirectory, "drivers", "refs.sys");
        if (!File.Exists(refsDriver))
            return (false, $"此 Windows 版本未安裝 ReFS 檔案系統驅動程式 (refs.sys)，無法格式化為 ReFS。");

        if (edition.StartsWith("Server", StringComparison.OrdinalIgnoreCase) ||
            product.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"{product} ({edition}) 支援 ReFS。");
        }

        var clientSupported = edition.Equals("ProfessionalWorkstation", StringComparison.OrdinalIgnoreCase) ||
                              edition.Equals("ProfessionalWorkstationN", StringComparison.OrdinalIgnoreCase) ||
                              edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase) ||
                              edition.Equals("EnterpriseN", StringComparison.OrdinalIgnoreCase) ||
                              edition.Equals("EnterpriseS", StringComparison.OrdinalIgnoreCase) ||
                              edition.Equals("EnterpriseSN", StringComparison.OrdinalIgnoreCase);

        if (clientSupported)
            return (true, $"{product} ({edition}) 支援 ReFS。");

        return (false, $"目前版本「{product}」({edition}) 不支援 ReFS：ReFS 僅適用於 Windows Pro for Workstations / Enterprise 或 Windows Server，" +
                       "無法在 Home / Pro 用戶端上格式化為 ReFS。");
    }

    /// <summary>
    /// Validates every value that will be interpolated into the diskpart format script.
    /// Returns null when the request is safe, otherwise a clear rejection reason.
    /// </summary>
    public static string? ValidateFormatArguments(int volumeNumber, string canonicalFileSystem, string? label, int? clusterSize, char? driveLetter)
    {
        if (volumeNumber < 0)
            return "磁碟區編號無效（必須是磁碟區清單中的整數編號）。";

        if (string.IsNullOrEmpty(canonicalFileSystem))
            return $"不支援的檔案系統：僅允許 {string.Join(" / ", SupportedFileSystems)}。";

        label ??= "";
        if (label.Length > 32)
            return "磁碟區標籤最長 32 個字元。";
        if (canonicalFileSystem == "FAT32" && label.Length > 11)
            return "FAT32 標籤最長 11 個字元。";
        if (label.Any(ch => char.IsControl(ch)) || label.Contains('"') || label.Contains('\\') || label.Contains('/'))
            return "磁碟區標籤含有不允許的字元（不可包含引號、斜線或控制字元）。";

        if (clusterSize.HasValue && !GetAllowedClusterSizes(canonicalFileSystem).Contains(clusterSize.Value))
            return $"{canonicalFileSystem} 不支援 {clusterSize.Value} bytes 的叢集大小（允許：{string.Join(", ", GetAllowedClusterSizes(canonicalFileSystem))}）。";

        if (canonicalFileSystem != "ReFS") return null;

        var (supported, reason) = CheckReFsSupport();
        if (!supported) return reason;

        if (!driveLetter.HasValue)
            return "ReFS 需要一個具有磁碟機代號的固定磁碟區；請先為目標磁碟區指派代號後再格式化。";

        var letter = char.ToUpperInvariant(driveLetter.Value);
        var driveType = GetDriveType($"{letter}:\\");
        if (driveType == DRIVE_REMOVABLE)
            return "ReFS 無法用於可卸除媒體（USB 隨身碟 / 讀卡機）。";
        if (driveType == DRIVE_CDROM)
            return "ReFS 無法用於光碟機。";
        if (driveType == 0)
            return $"無法查詢 {letter}: 的磁碟類型（裝置未就緒或沒有代號），已拒絕 ReFS 格式化。";

        var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\')[0];
        if (letter == char.ToUpperInvariant(systemDrive))
            return "ReFS 無法格式化開機 / 系統磁碟區（Windows 不支援從 ReFS 開機）。";

        return null;
    }

    /// <summary>
    /// Validated format entry point. Runs the ReFS / removable / boot-volume pre-checks, validates the
    /// label + cluster size, and only then builds "format fs=... unit=... label=... quick" from those values.
    /// </summary>
    public async Task<(bool Success, string Output)> FormatVolumeCheckedAsync(
        int volumeNumber,
        string fileSystem,
        string label = "",
        bool quickFormat = true,
        int? clusterSize = null,
        char? driveLetter = null,
        CancellationToken cancellationToken = default)
    {
        var fs = CanonicalFileSystem(fileSystem);
        var rejection = ValidateFormatArguments(volumeNumber, fs, label, clusterSize, driveLetter);
        if (rejection != null)
            return (false, $"ERROR: 已拒絕格式化 Volume {volumeNumber}：{rejection}");

        var labelArg = string.IsNullOrEmpty(label) ? "" : $" label=\"{label}\"";
        var clusterArg = clusterSize.HasValue ? $" unit={clusterSize.Value}" : "";
        var quickArg = quickFormat ? " quick" : "";
        var script = $"select volume {volumeNumber}\r\nformat fs={fs}{clusterArg}{labelArg}{quickArg}";

        // A full (non quick) format of a large volume can take hours - cancellable by the user.
        var output = await RunDiskPartAsync(script, quickFormat ? 3600 : 86400, cancellationToken);

        var success = output.Contains("successfully formatted", StringComparison.OrdinalIgnoreCase) ||
                      output.Contains("順利格式化", StringComparison.Ordinal) ||
                      output.Contains("成功格式化", StringComparison.Ordinal);
        if (!success)
            output = $"ERROR: diskpart 格式化 Volume {volumeNumber} 為 {fs} 失敗或被取消。\n{output}";
        return (success, output);
    }

    private async Task<string> RunDiskPartAsync(string script, int timeoutSeconds = 60)
        => await RunDiskPartAsync(script, timeoutSeconds, CancellationToken.None);

    /// <summary>
    /// Runs a diskpart script under the process-wide diskpart lock.
    /// A4 fix: the lock wait and the diskpart session are both cancellable, so a long operation
    /// (e.g. "clean all") no longer blocks every other disk operation silently and un-cancellably.
    /// Cancellation never throws out of this method: it returns an explicit ERROR: text instead,
    /// so every existing caller keeps its string-based contract.
    /// </summary>
    private async Task<string> RunDiskPartAsync(string script, int timeoutSeconds, CancellationToken cancellationToken)
    {
        try
        {
            await _diskPartLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return "ERROR: 已取消 - 在等待 DiskPart 鎖定時被中止（另一個 DiskPart 作業正在進行中）。";
        }

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

            // Configurable timeout, linked with the caller's cancellation token (user cancel).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cts.Token);
                // Allow a brief moment for asynchronous streams to flush
                process.WaitForExit(300);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                var reason = cancellationToken.IsCancellationRequested
                    ? "使用者取消"
                    : $"逾時 {timeoutSeconds} 秒";
                return $"ERROR: DiskPart 已中止（{reason}）。\n\nPartial output:\n{stdout}\n{stderr}";
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
    public async Task<(List<DiskInfo> Disks, List<VolumeInfo> Volumes, string RawOutput)> ListDisksAndVolumesAsync(CancellationToken cancellationToken = default)
    {
        var script = "list disk\r\nlist volume";
        var output = await RunDiskPartAsync(script, 60, cancellationToken);
        return (OutputParser.ParseDisks(output), OutputParser.ParseVolumes(output), output);
    }

    public async Task<(List<DiskInfo> Disks, string RawOutput)> ListDisksAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunDiskPartAsync("list disk", 60, cancellationToken);
        return (OutputParser.ParseDisks(output), output);
    }

    public async Task<(List<VolumeInfo> Volumes, string RawOutput)> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunDiskPartAsync("list volume", 60, cancellationToken);
        return (OutputParser.ParseVolumes(output), output);
    }

    public async Task<(List<PartitionInfo> Partitions, string RawOutput)> ListPartitionsAsync(int diskNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select disk {diskNumber}\r\nlist partition";
        var output = await RunDiskPartAsync(script, 60, cancellationToken);
        return (OutputParser.ParsePartitions(output), output);
    }

    public async Task<string> CleanDiskAsync(int diskNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select disk {diskNumber}\r\nclean";
        return await RunDiskPartAsync(script, 60, cancellationToken);
    }

    /// <summary>"clean all" wipes every sector: give it a hard ceiling instead of a silent 24 h block.</summary>
    public const int CleanAllTimeoutSeconds = 4 * 60 * 60;

    public async Task<string> CleanDiskAllAsync(int diskNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select disk {diskNumber}\r\nclean all";
        // "clean all" can take hours on TB drives - capped at 4 h and cancellable by the caller.
        return await RunDiskPartAsync(script, CleanAllTimeoutSeconds, cancellationToken);
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

    public async Task<string> FormatVolumeAsync(int volumeNumber, string fileSystem = "NTFS", string label = "", bool quickFormat = true, CancellationToken cancellationToken = default)
    {
        // Routed through the validated path so every caller (including the Easy Mode flow) gets the
        // file-system allow-list, the label/cluster validation and the ReFS pre-checks for free.
        var (_, output) = await FormatVolumeCheckedAsync(volumeNumber, fileSystem, label, quickFormat, cancellationToken: cancellationToken);
        return output;
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

    public async Task<string> DetailDiskAsync(int diskNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select disk {diskNumber}\r\ndetail disk";
        return await RunDiskPartAsync(script, 60, cancellationToken);
    }

    public async Task<string> DetailVolumeAsync(int volumeNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select volume {volumeNumber}\r\ndetail volume";
        return await RunDiskPartAsync(script, 60, cancellationToken);
    }

    public async Task<string> ClearDiskReadOnlyAsync(int diskNumber, CancellationToken cancellationToken = default)
    {
        var script = $"select disk {diskNumber}\r\nattributes disk clear readonly";
        return await RunDiskPartAsync(script, 60, cancellationToken);
    }

    public async Task<string> SetDiskOnlineOfflineAsync(int diskNumber, bool online, CancellationToken cancellationToken = default)
    {
        var cmd = online ? "online disk" : "offline disk";
        var script = $"select disk {diskNumber}\r\n{cmd}";
        return await RunDiskPartAsync(script, 120, cancellationToken);
    }

    public async Task<string> RescanDisksAsync(CancellationToken cancellationToken = default)
    {
        return await RunDiskPartAsync("rescan", 300, cancellationToken);
    }

    public async Task<string> RunCustomScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        return await RunDiskPartAsync(script, 60, cancellationToken);
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

    public async Task<string> FormatWithClusterSizeAsync(int volumeNumber, string fileSystem, int clusterSize, string label = "", bool quick = true, CancellationToken cancellationToken = default)
    {
        // Validated path: cluster sizes are allow-listed per file system before any script is built.
        var (_, output) = await FormatVolumeCheckedAsync(volumeNumber, fileSystem, label, quick, clusterSize, cancellationToken: cancellationToken);
        return output;
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
