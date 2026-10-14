using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Raw (block level) clone engine for whole disks and single volumes, plus the geometry probes
/// reused by the VHDX image mode.
///
/// It talks to the Win32 raw device namespace only:
///     \\.\PhysicalDriveN   (whole disk)
///     \\.\X:               (single volume / partition)
///
/// The logical sector size is always read back from the device
/// (IOCTL_DISK_GET_DRIVE_GEOMETRY_EX / IOCTL_DISK_GET_LENGTH_INFO); 512 bytes is never assumed.
/// Every buffer, offset and transfer length is aligned to the sector size of the device pair,
/// transfers are chunked, and progress / throughput / ETA are reported per chunk.
///
/// SAFETY: all destructive entry points require (1) an elevated process,
/// (2) <see cref="CloneOptions.DestructiveTargetConfirmed"/>, and (3) a target that is neither the
/// running system disk/volume nor the source itself. Capacity validation happens before the first byte
/// is written. This class never guesses - it either proves the plan is safe or fails with a message.
/// </summary>
public sealed class DiskCloneService
{
    // UI mode identifiers shared with the ViewModel (both clone modes required by the feature spec).
    public const string ModeRaw = "🔁 原始區塊複製 (Disk/Volume → Disk/Volume)";
    public const string ModeCreateImage = "💽 建立 VHDX 映像 (Disk/Volume → .vhdx)";
    public const string ModeRestoreImage = "♻️ 從 VHDX 映像還原 (.vhdx → Disk/Volume)";

    // ── Win32 constants ──
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    private const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    private const int ERROR_INVALID_PARAMETER = 87;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_WRITE_PROTECT = 19;
    private const int ERROR_NOT_READY = 21;

    private const int DefaultChunkSize = 4 * 1024 * 1024;   // 4 MiB, re-aligned to the device sector size
    private const int MinChunkSize = 64 * 1024;
    private const int ProgressIntervalMs = 200;

    // ── P/Invoke ──
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpace(
        string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    private static extern uint GetDriveType(string lpRootPathName);

    [DllImport("ntdll.dll")]
    private static extern UIntPtr RtlCompareMemory(IntPtr source1, IntPtr source2, UIntPtr length);

    // ── Native structures ──
    [StructLayout(LayoutKind.Sequential)]
    private struct GET_LENGTH_INFORMATION
    {
        public long Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISK_GEOMETRY
    {
        public long Cylinders;
        public int MediaType;
        public int TracksPerCylinder;
        public int SectorsPerTrack;
        public int BytesPerSector;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISK_GEOMETRY_EX
    {
        public DISK_GEOMETRY Geometry;
        public long DiskSize;
    }

    /// <summary>Raised when the device rejects FILE_FLAG_NO_BUFFERING so the engine can retry cached.</summary>
    private sealed class UnbufferedIoNotSupportedException : IOException
    {
        public UnbufferedIoNotSupportedException(string message) : base(message) { }
    }

    // ───────────────────────── Public device model ─────────────────────────

    /// <summary>A selectable raw device (whole disk or single volume).</summary>
    public sealed class CloneDevice
    {
        public string DevicePath { get; init; } = "";
        public bool IsPhysicalDrive { get; init; }
        public int DiskNumber { get; init; } = -1;
        public char? DriveLetter { get; init; }
        public string VolumeLabel { get; init; } = "";
        public string SizeDisplay { get; init; } = "";
        public string Description { get; init; } = "";

        public string DisplayName => IsPhysicalDrive
            ? $"💽 {DevicePath}  {Description}  ({SizeDisplay})"
            : $"🗂️ {DevicePath}  {(string.IsNullOrWhiteSpace(VolumeLabel) ? Description : VolumeLabel)}  ({SizeDisplay})";

        public override string ToString() => DisplayName;
    }

    /// <summary>Live geometry read straight from the device handle.</summary>
    public sealed class CloneDeviceInfo
    {
        public string DevicePath { get; set; } = "";
        public bool IsPhysicalDrive { get; set; }
        public int DiskNumber { get; set; } = -1;
        public char? DriveLetter { get; set; }
        public int BytesPerSector { get; set; }
        public long TotalBytes { get; set; }
        public bool IsRemovable { get; set; }
        public bool IsWriteProtected { get; set; }

        public string SizeDisplay => NativeStorageService.FormatBytes(TotalBytes);

        public string GeometrySummary =>
            $"{DevicePath} | {SizeDisplay} | 邏輯磁區 {BytesPerSector} B" +
            (IsRemovable ? " | 可卸除媒體" : "") +
            (IsWriteProtected ? " | 唯讀" : "");
    }

    public sealed class CloneOptions
    {
        /// <summary>Set to true only after the user approved an explicit destructive-target confirmation dialog.</summary>
        public bool DestructiveTargetConfirmed { get; set; }

        /// <summary>Re-read the target after writing and byte-compare it against the source.</summary>
        public bool VerifyAfterWrite { get; set; }

        /// <summary>Take the target whole disk offline (diskpart) before the raw write.</summary>
        public bool OfflineTargetDiskFirst { get; set; } = true;

        /// <summary>Bring the target whole disk back online after a successful/aborted clone.</summary>
        public bool BringTargetOnlineAfterwards { get; set; } = true;

        /// <summary>Requested chunk size; re-aligned down to a common sector multiple.</summary>
        public int ChunkSizeBytes { get; set; } = DefaultChunkSize;

        /// <summary>Copy only this many bytes (defaults to the full source device length).</summary>
        public long? CopyLengthOverride { get; set; }
    }

    public sealed class CloneProgress
    {
        public string Stage { get; init; } = "";
        public double Percent { get; init; }
        public long BytesDone { get; init; }
        public long TotalBytes { get; init; }
        public double BytesPerSecond { get; init; }
        public TimeSpan Elapsed { get; init; }
        public TimeSpan? Eta { get; init; }
    }

    public sealed class CloneResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Message { get; set; } = "";
        public string SourceDevice { get; set; } = "";
        public string TargetDevice { get; set; } = "";
        public int SourceBytesPerSector { get; set; }
        public int TargetBytesPerSector { get; set; }
        public long SourceBytes { get; set; }
        public long TargetBytes { get; set; }
        public long CopyLength { get; set; }
        public long CopiedBytes { get; set; }
        public long UntouchedTailBytes { get; set; }
        public TimeSpan Elapsed { get; set; }
        public double AverageBytesPerSecond { get; set; }
        public bool VerificationRequested { get; set; }
        public bool VerificationPassed { get; set; }
        public string VerificationSummary { get; set; } = "";
        public List<string> Diagnostics { get; } = new();

        /// <summary>True when the target was modified but not fully written (cancelled / failed midway).</summary>
        public bool TargetLeftPartial => CopiedBytes > 0 && CopiedBytes < CopyLength;
    }

    // ───────────────────────── Device path helpers ─────────────────────────

    public static string GetPhysicalDrivePath(int diskNumber) => $@"\\.\PhysicalDrive{diskNumber}";

    public static string GetVolumePath(char driveLetter) => $@"\\.\{char.ToUpperInvariant(driveLetter)}:";

    public static bool TryParseDiskNumber(string devicePath, out int diskNumber)
    {
        diskNumber = -1;
        if (string.IsNullOrWhiteSpace(devicePath)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(devicePath.Trim(), @"PhysicalDrive(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out diskNumber);
    }

    public static bool TryParseDriveLetter(string devicePath, out char driveLetter)
    {
        driveLetter = '\0';
        if (string.IsNullOrWhiteSpace(devicePath)) return false;
        var m = System.Text.RegularExpressions.Regex.Match(devicePath.Trim(), @"^\\\\\.\\([A-Za-z]):?$");
        if (!m.Success) return false;
        driveLetter = char.ToUpperInvariant(m.Groups[1].Value[0]);
        return true;
    }

    public static bool IsRawDevicePath(string devicePath) =>
        TryParseDiskNumber(devicePath, out _) || TryParseDriveLetter(devicePath, out _);

    /// <summary>Turns diskpart "list disk / list volume" output into the pickable device list.</summary>
    public async Task<(List<CloneDevice> Devices, string RawOutput)> ListCloneDevicesAsync()
    {
        var diskPart = new DiskPartService();
        var (disks, volumes, raw) = await diskPart.ListDisksAndVolumesAsync();

        var result = new List<CloneDevice>();
        foreach (var disk in disks)
        {
            result.Add(new CloneDevice
            {
                DevicePath = GetPhysicalDrivePath(disk.Number),
                IsPhysicalDrive = true,
                DiskNumber = disk.Number,
                Description = string.IsNullOrWhiteSpace(disk.FriendlyName) ? disk.GptOrMbr : $"{disk.FriendlyName} [{disk.GptOrMbr}]",
                SizeDisplay = string.IsNullOrWhiteSpace(disk.SizeDisplay) ? "未知" : disk.SizeDisplay
            });
        }

        foreach (var volume in volumes)
        {
            if (string.IsNullOrWhiteSpace(volume.Letter)) continue;   // raw handles need a mounted letter
            var letter = volume.Letter.TrimEnd(':')[0];
            result.Add(new CloneDevice
            {
                DevicePath = GetVolumePath(letter),
                IsPhysicalDrive = false,
                DriveLetter = letter,
                VolumeLabel = volume.Label,
                Description = string.IsNullOrWhiteSpace(volume.FileSystem) ? volume.Type : volume.FileSystem,
                SizeDisplay = string.IsNullOrWhiteSpace(volume.SizeDisplay) ? "未知" : volume.SizeDisplay
            });
        }

        return (result, raw);
    }

    // ───────────────────────── Geometry probe ─────────────────────────

    /// <summary>Reads sector size / exact byte length straight from the device (no assumptions).</summary>
    public Task<CloneDeviceInfo> QueryDeviceInfoAsync(string devicePath, CancellationToken ct = default)
        => Task.Run(() => QueryDeviceInfo(devicePath), ct);

    public CloneDeviceInfo QueryDeviceInfo(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            throw new ArgumentException("未指定裝置路徑。", nameof(devicePath));

        if (!IsRawDevicePath(devicePath))
            throw new ArgumentException($"不是有效的原始裝置路徑（需為 \\\\.\\PhysicalDriveN 或 \\\\.\\X:）：{devicePath}", nameof(devicePath));

        using var handle = CreateFile(devicePath,
            FILE_READ_ATTRIBUTES | GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"無法開啟裝置 {devicePath}（需要系統管理員權限）");

        var info = new CloneDeviceInfo { DevicePath = devicePath };
        if (TryParseDiskNumber(devicePath, out var diskNumber))
        {
            info.IsPhysicalDrive = true;
            info.DiskNumber = diskNumber;
        }
        else if (TryParseDriveLetter(devicePath, out var letter))
        {
            info.IsPhysicalDrive = false;
            info.DriveLetter = letter;
            var driveType = GetDriveType($"{letter}:\\");
            info.IsRemovable = driveType == 2;   // DRIVE_REMOVABLE
        }

        // 1. Exact length: IOCTL_DISK_GET_LENGTH_INFO (works for both disks and volumes)
        var lengthBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<GET_LENGTH_INFORMATION>());
        try
        {
            if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0,
                    lengthBuffer, (uint)Marshal.SizeOf<GET_LENGTH_INFORMATION>(), out _, IntPtr.Zero))
            {
                info.TotalBytes = Marshal.PtrToStructure<GET_LENGTH_INFORMATION>(lengthBuffer).Length;
            }
        }
        finally { Marshal.FreeHGlobal(lengthBuffer); }

        // 2. Logical sector size + (fallback) disk size
        var geomBuffer = Marshal.AllocHGlobal(64);
        try
        {
            if (DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0,
                    geomBuffer, 64, out _, IntPtr.Zero))
            {
                var geom = Marshal.PtrToStructure<DISK_GEOMETRY_EX>(geomBuffer);
                info.BytesPerSector = geom.Geometry.BytesPerSector;
                if (info.TotalBytes <= 0) info.TotalBytes = geom.DiskSize;
            }
        }
        finally { Marshal.FreeHGlobal(geomBuffer); }

        // 3. Last resort for the sector size: the mounted file system
        if (info.BytesPerSector <= 0 && info.DriveLetter.HasValue)
        {
            if (GetDiskFreeSpace($"{info.DriveLetter}:\\", out _, out var bps, out _, out _) && bps > 0)
                info.BytesPerSector = (int)bps;
        }

        if (info.BytesPerSector <= 0)
            throw new IOException($"無法讀取 {devicePath} 的邏輯磁區大小（裝置未就緒或被鎖定）。");

        if (info.TotalBytes <= 0)
            throw new IOException($"無法讀取 {devicePath} 的容量（裝置未就緒或被鎖定）。");

        return info;
    }

    // ───────────────────────── Destructive clone ─────────────────────────

    /// <summary>
    /// Block-level clone of one raw device onto another. Validates admin rights, the destructive
    /// confirmation flag, source != target, system-disk protection, capacity and sector geometry
    /// BEFORE the first write; then performs chunked aligned reads/writes with progress, throughput,
    /// ETA, cancellation and an optional read-back verification.
    /// </summary>
    public Task<CloneResult> CloneDeviceToDeviceAsync(
        string sourceDevicePath,
        string targetDevicePath,
        CloneOptions options,
        IProgress<CloneProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => CloneCoreAsync(sourceDevicePath, targetDevicePath, options, progress, ct), ct);
    }

    private async Task<CloneResult> CloneCoreAsync(
        string sourceDevicePath,
        string targetDevicePath,
        CloneOptions options,
        IProgress<CloneProgress>? progress,
        CancellationToken ct)
    {
        var result = new CloneResult
        {
            SourceDevice = sourceDevicePath,
            TargetDevice = targetDevicePath,
            VerificationRequested = options.VerifyAfterWrite
        };
        var sw = Stopwatch.StartNew();
        int? targetDiskNumber = null;
        bool targetDiskTakenOffline = false;

        try
        {
            // ── Gate 1: elevation ──
            if (!AdminHelper.IsRunningAsAdmin())
                return Fail(result, "❌ 拒絕執行：區塊級複製需要系統管理員權限（請以「以系統管理員身分執行」重新啟動 DiskMaster）。");

            // ── Gate 2: explicit destructive confirmation ──
            if (!options.DestructiveTargetConfirmed)
                return Fail(result, "❌ 拒絕執行：未取得目標裝置的破壞性覆寫確認（DestructiveTargetConfirmed = false）。");

            // ── Gate 3: paths must be raw devices and different ──
            if (!IsRawDevicePath(sourceDevicePath) || !IsRawDevicePath(targetDevicePath))
                return Fail(result, "❌ 裝置路徑無效：來源與目標都必須是 \\\\.\\PhysicalDriveN 或 \\\\.\\X:。");

            if (string.Equals(sourceDevicePath.Trim(), targetDevicePath.Trim(), StringComparison.OrdinalIgnoreCase))
                return Fail(result, "❌ 拒絕執行：來源與目標是同一個裝置。");

            // ── Geometry (read only, nothing written yet) ──
            var source = QueryDeviceInfo(sourceDevicePath);
            var target = QueryDeviceInfo(targetDevicePath);
            result.SourceBytesPerSector = source.BytesPerSector;
            result.TargetBytesPerSector = target.BytesPerSector;
            result.SourceBytes = source.TotalBytes;
            result.TargetBytes = target.TotalBytes;
            result.Diagnostics.Add($"來源幾何: {source.GeometrySummary}");
            result.Diagnostics.Add($"目標幾何: {target.GeometrySummary}");

            var systemDisk = TryGetSystemDiskNumber();

            if (source.IsWriteProtected) { /* reading a write-protected source is fine */ }
            if (!source.IsPhysicalDrive)
                result.Diagnostics.Add("⚠️ 來源為已掛載的磁碟區且未使用 VSS 快照：若檔案系統在複製期間仍被寫入，映像可能為非一致性狀態（建議離線或在 WinPE 中複製）。");
            if (source.IsPhysicalDrive && systemDisk.HasValue && source.DiskNumber == systemDisk.Value)
                result.Diagnostics.Add("⚠️ 來源是執行中的系統磁碟且未使用 VSS 快照，映像可能為非一致性狀態（不建議在開機狀態下複製系統磁碟）。");
            if (target.IsWriteProtected)
                return Fail(result, "❌ 目標裝置為唯讀（write protected），請先清除唯讀屬性。");

            // ── Gate 4: system / boot disk protection ──
            if (target.IsPhysicalDrive)
            {
                targetDiskNumber = target.DiskNumber;
                if (systemDisk.HasValue && target.DiskNumber == systemDisk.Value)
                    return Fail(result, $"❌ 拒絕執行：Disk {target.DiskNumber} 是執行中的系統磁碟，禁止覆寫。");
                if (!systemDisk.HasValue && target.DiskNumber == 0)
                    return Fail(result, "❌ 拒絕執行：無法辨識系統磁碟編號，為安全起見拒絕覆寫 Disk 0。");

                if (source.IsPhysicalDrive && source.DiskNumber == target.DiskNumber)
                    return Fail(result, "❌ 拒絕執行：來源與目標是同一顆實體磁碟。");
            }
            else
            {
                var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:")[0];
                if (target.DriveLetter == char.ToUpperInvariant(systemDrive))
                    return Fail(result, $"❌ 拒絕執行：{target.DevicePath} 是執行中的系統磁碟區，禁止覆寫。");
                if (source.DriveLetter.HasValue && target.DriveLetter.HasValue && source.DriveLetter == target.DriveLetter)
                    return Fail(result, "❌ 拒絕執行：來源與目標是同一個磁碟區。");

                var sourceDisk = source.DriveLetter.HasValue ? TryGetDiskNumberOfLetter(source.DriveLetter.Value) : null;
                var targetDisk = target.DriveLetter.HasValue ? TryGetDiskNumberOfLetter(target.DriveLetter.Value) : null;
                if (sourceDisk.HasValue && targetDisk.HasValue && sourceDisk.Value == targetDisk.Value)
                    return Fail(result, "❌ 拒絕執行：來源與目標磁碟區位於同一顆實體磁碟。");
                if (targetDisk.HasValue && systemDisk.HasValue && targetDisk.Value == systemDisk.Value)
                    result.Diagnostics.Add($"⚠️ 目標磁碟區位於系統磁碟 Disk {targetDisk.Value} 上，但自身不是系統磁碟區。");
            }

            // ── Gate 5: capacity + sector geometry validation, BEFORE any write ──
            var sectorLcm = Lcm(source.BytesPerSector, target.BytesPerSector);
            var requested = options.CopyLengthOverride ?? source.TotalBytes;
            if (requested <= 0) return Fail(result, "❌ 來源裝置長度無效 (0 bytes)。");

            var copyLength = requested - (requested % sectorLcm);
            if (copyLength <= 0)
                return Fail(result, $"❌ 複製長度 {requested} bytes 小於來源與目標磁區的最小公倍數 {sectorLcm} B，幾何不相容。");
            if (target.TotalBytes < copyLength)
                return Fail(result, $"❌ 容量不足：目標 {NativeStorageService.FormatBytes(target.TotalBytes)} 小於來源 {NativeStorageService.FormatBytes(copyLength)}。");

            result.CopyLength = copyLength;
            result.UntouchedTailBytes = Math.Max(0, target.TotalBytes - copyLength);

            int chunk = AlignDown(options.ChunkSizeBytes <= 0 ? DefaultChunkSize : options.ChunkSizeBytes, sectorLcm);
            if (chunk < AlignUp(MinChunkSize, sectorLcm)) chunk = AlignUp(MinChunkSize, sectorLcm);
            result.Diagnostics.Add($"對齊區塊大小: {NativeStorageService.FormatBytes(chunk)}，來源磁區 {source.BytesPerSector} B，目標磁區 {target.BytesPerSector} B，共用最小公倍數 {sectorLcm} B。");

            // ── Prepare the target (non-destructive until the first write) ──
            if (target.IsPhysicalDrive && options.OfflineTargetDiskFirst && targetDiskNumber.HasValue)
            {
                var diskPart = new DiskPartService();
                var clearRo = await diskPart.ClearDiskReadOnlyAsync(targetDiskNumber.Value, ct);
                result.Diagnostics.Add($"[diskpart] attributes disk clear readonly → {(clearRo.Contains("successfully", StringComparison.OrdinalIgnoreCase) || clearRo.Contains("成功", StringComparison.Ordinal) ? "OK" : clearRo.Trim())}");
                var offline = await diskPart.SetDiskOnlineOfflineAsync(targetDiskNumber.Value, online: false, ct);
                targetDiskTakenOffline = offline.Contains("successfully", StringComparison.OrdinalIgnoreCase) || offline.Contains("成功", StringComparison.Ordinal);
                result.Diagnostics.Add($"[diskpart] offline disk {targetDiskNumber.Value} → {(targetDiskTakenOffline ? "OK" : offline.Trim())}");
                if (!targetDiskTakenOffline)
                    return Fail(result, $"❌ 無法將目標 Disk {targetDiskNumber.Value} 離線（可能含有使用中的磁碟區）。請先關閉所有使用該磁碟的程式後重試。\n{offline.Trim()}");
            }

            // ── Copy loop (unbuffered first, cached fallback) ──
            string copySummary;
            try
            {
                copySummary = RunCopyLoop(sourceDevicePath, targetDevicePath, copyLength, chunk, source.BytesPerSector, target.BytesPerSector,
                    unbuffered: true, result, progress, ct);
            }
            catch (UnbufferedIoNotSupportedException ex) when (!ct.IsCancellationRequested)
            {
                result.Diagnostics.Add($"⚠️ 裝置拒絕無緩衝 I/O（{ex.Message}），改用系統快取 I/O 重新開始複製。");
                result.CopiedBytes = 0;
                copySummary = RunCopyLoop(sourceDevicePath, targetDevicePath, copyLength, chunk, source.BytesPerSector, target.BytesPerSector,
                    unbuffered: false, result, progress, ct);
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            result.AverageBytesPerSecond = result.Elapsed.TotalSeconds > 0 ? result.CopiedBytes / result.Elapsed.TotalSeconds : 0;

            // ── Optional read-back verification ──
            if (options.VerifyAfterWrite && !ct.IsCancellationRequested)
            {
                var (passed, summary) = VerifyMatch(sourceDevicePath, targetDevicePath, copyLength, sectorLcm, result, progress, ct);
                result.VerificationPassed = passed;
                result.VerificationSummary = summary;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"✅ 複製完成：{sourceDevicePath} → {targetDevicePath}");
            sb.AppendLine($"已寫入 {NativeStorageService.FormatBytes(result.CopiedBytes)} / 共 {NativeStorageService.FormatBytes(copyLength)}");
            sb.AppendLine($"耗時 {result.Elapsed:hh\\:mm\\:ss}，平均 {result.AverageBytesPerSecond / 1024 / 1024:0.0} MB/s，{copySummary}");
            if (result.UntouchedTailBytes > 0)
                sb.AppendLine($"⚠️ 目標比來源大 {NativeStorageService.FormatBytes(result.UntouchedTailBytes)}，尾端未寫入（保持原狀，未清除）。");
            if (options.VerifyAfterWrite)
                sb.AppendLine(result.VerificationPassed ? $"🔎 {result.VerificationSummary}" : $"❌ {result.VerificationSummary}");
            result.Success = true;
            result.Message = sb.ToString().TrimEnd();
            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Cancelled = true;
            result.Elapsed = sw.Elapsed;
            result.Message = BuildCancelledMessage(result);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Elapsed = sw.Elapsed;
            result.Message = (result.TargetLeftPartial
                ? $"❌ 複製失敗，目標處於「部分寫入」狀態（已寫入 {NativeStorageService.FormatBytes(result.CopiedBytes)}，尚有 {NativeStorageService.FormatBytes(Math.Max(0, result.CopyLength - result.CopiedBytes))} 未寫入）。\n"
                : "❌ 複製失敗（尚未寫入目標資料）。\n") + ex.Message;
            result.Diagnostics.Add($"[EXCEPTION] {ex.GetType().Name}: {ex.Message}");
            return result;
        }
        finally
        {
            if (targetDiskTakenOffline && targetDiskNumber.HasValue && options.BringTargetOnlineAfterwards)
            {
                try
                {
                    var diskPart = new DiskPartService();
                    await diskPart.SetDiskOnlineOfflineAsync(targetDiskNumber.Value, online: true);
                    result.Diagnostics.Add($"[diskpart] online disk {targetDiskNumber.Value}（複製結束後恢復上線）");
                }
                catch (Exception ex) { result.Diagnostics.Add($"[WARN] 無法將 Disk {targetDiskNumber} 回復上線: {ex.Message}"); }
            }
        }
    }

    private static CloneResult Fail(CloneResult result, string message)
    {
        result.Success = false;
        result.Message = message;
        return result;
    }

    private static string BuildCancelledMessage(CloneResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"⏹️ 使用者已取消複製：{result.SourceDevice} → {result.TargetDevice}");
        sb.AppendLine($"已寫入 {NativeStorageService.FormatBytes(result.CopiedBytes)} / 共 {NativeStorageService.FormatBytes(result.CopyLength)}，耗時 {result.Elapsed:hh\\:mm\\:ss}。");
        if (result.TargetLeftPartial)
            sb.AppendLine($"⚠️ 目標目前為「部分複製」狀態，尚有 {NativeStorageService.FormatBytes(Math.Max(0, result.CopyLength - result.CopiedBytes))} 未寫入；請勿直接使用該目標，重新執行或以磁碟工具修復。");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Aligned chunked read/write loop. Throws UnbufferedIoNotSupportedException on the very first unbuffered transfer failure.</summary>
    private string RunCopyLoop(
        string sourcePath, string targetPath, long copyLength, int chunk, int sourceSector, int targetSector,
        bool unbuffered, CloneResult result, IProgress<CloneProgress>? progress, CancellationToken ct)
    {
        uint srcFlags = unbuffered ? FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN : FILE_FLAG_SEQUENTIAL_SCAN;
        uint dstFlags = unbuffered ? FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH : 0;
        int transferAlignment = Lcm(sourceSector, targetSector);
        var ioSw = Stopwatch.StartNew();

        using var src = CreateFile(sourcePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, srcFlags, IntPtr.Zero);
        if (src.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"無法開啟來源裝置 {sourcePath}");

        using var dst = CreateFile(targetPath, GENERIC_WRITE | GENERIC_READ | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, dstFlags, IntPtr.Zero);
        if (dst.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            var hint = err == ERROR_ACCESS_DENIED || err == ERROR_SHARING_VIOLATION
                ? "（目標仍在被使用中，請先離線目標磁碟或關閉所有存取該磁碟區的程式）"
                : "";
            throw new Win32Exception(err, $"無法以寫入模式開啟目標裝置 {targetPath}{hint}");
        }

        // Volume target: lock + dismount so the OS cannot cache or remount behind the raw write.
        if (TryParseDriveLetter(targetPath, out _))
        {
            LockAndDismountVolume(dst, targetPath, result);
        }

        // Single aligned staging buffer: VirtualAlloc guarantees 64 KiB alignment, which satisfies
        // the FILE_FLAG_NO_BUFFERING alignment requirement for every logical sector size in use today.
        var buffer = VirtualAlloc(IntPtr.Zero, (UIntPtr)chunk, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (buffer == IntPtr.Zero)
            throw new IOException("無法配置對齊的 I/O 緩衝區 (VirtualAlloc 失敗)。");

        try
        {
            long offset = 0;
            long lastReportMs = -ProgressIntervalMs;
            var reportTimer = Stopwatch.StartNew();

            while (offset < copyLength)
            {
                ct.ThrowIfCancellationRequested();

                int want = (int)Math.Min(chunk, copyLength - offset);
                want -= want % Math.Max(1, transferAlignment);   // keep every transfer aligned to both devices' sectors
                if (want <= 0) break;

                try
                {
                    ReadExactly(src, buffer, want, offset);
                    WriteExactly(dst, buffer, want, offset);
                }
                catch (Win32Exception w32) when (unbuffered && offset == 0 && w32.NativeErrorCode == ERROR_INVALID_PARAMETER)
                {
                    // The device rejects the unbuffered alignment contract; the caller retries cached.
                    throw new UnbufferedIoNotSupportedException($"Win32 {ERROR_INVALID_PARAMETER}");
                }

                offset += want;
                result.CopiedBytes = offset;

                if (reportTimer.ElapsedMilliseconds - lastReportMs >= ProgressIntervalMs || offset >= copyLength)
                {
                    lastReportMs = reportTimer.ElapsedMilliseconds;
                    var elapsed = ioSw.Elapsed;
                    var bps = elapsed.TotalSeconds > 0 ? offset / elapsed.TotalSeconds : 0;
                    TimeSpan? eta = bps > 0 ? TimeSpan.FromSeconds(Math.Max(0, copyLength - offset) / bps) : null;
                    progress?.Report(new CloneProgress
                    {
                        Stage = "複製中",
                        Percent = copyLength > 0 ? offset * 100.0 / copyLength : 100,
                        BytesDone = offset,
                        TotalBytes = copyLength,
                        BytesPerSecond = bps,
                        Elapsed = elapsed,
                        Eta = eta
                    });
                }
            }

            FlushFileBuffers(dst);

            progress?.Report(new CloneProgress
            {
                Stage = "寫入完成",
                Percent = 100,
                BytesDone = offset,
                TotalBytes = copyLength,
                BytesPerSecond = ioSw.Elapsed.TotalSeconds > 0 ? offset / ioSw.Elapsed.TotalSeconds : 0,
                Elapsed = ioSw.Elapsed,
                Eta = TimeSpan.Zero
            });

            return unbuffered ? "無緩衝 (NO_BUFFERING + WRITE_THROUGH)" : "系統快取 (buffered)";
        }
        finally
        {
            VirtualFree(buffer, UIntPtr.Zero, MEM_RELEASE);
        }
    }

    private static void LockAndDismountVolume(SafeFileHandle targetHandle, string targetPath, CloneResult result)
    {
        if (DeviceIoControl(targetHandle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            result.Diagnostics.Add($"[FSCTL_LOCK_VOLUME] {targetPath} 已鎖定");
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            var detail = err == ERROR_ACCESS_DENIED || err == ERROR_SHARING_VIOLATION
                ? "磁碟區正在使用中（有檔案被開啟或程式正在存取）"
                : new Win32Exception(err).Message;
            throw new IOException($"❌ 無法鎖定目標磁碟區 {targetPath}：{detail}。請先關閉所有使用該磁碟區的程式後重試。");
        }

        if (DeviceIoControl(targetHandle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            result.Diagnostics.Add($"[FSCTL_DISMOUNT_VOLUME] {targetPath} 已卸載（避免寫入期間被檔案系統覆蓋）");
        else
            result.Diagnostics.Add($"[WARN] FSCTL_DISMOUNT_VOLUME 失敗 Win32={Marshal.GetLastWin32Error()}（將直接寫入原始磁區）");
    }

    private static void ReadExactly(SafeFileHandle handle, IntPtr buffer, int count, long offset)
    {
        int done = 0;
        while (done < count)
        {
            if (!ReadFile(handle, IntPtr.Add(buffer, done), (uint)(count - done), out var read, IntPtr.Zero) || read == 0)
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"讀取來源失敗 (offset={offset + done}, len={count - done})");
            }
            done += (int)read;
        }
    }

    private static void WriteExactly(SafeFileHandle handle, IntPtr buffer, int count, long offset)
    {
        int done = 0;
        while (done < count)
        {
            if (!WriteFile(handle, IntPtr.Add(buffer, done), (uint)(count - done), out var written, IntPtr.Zero) || written == 0)
            {
                var err = Marshal.GetLastWin32Error();
                var hint = err == ERROR_WRITE_PROTECT ? "（目標為唯讀）" : err == ERROR_ACCESS_DENIED ? "（目標仍被其他程式使用）" : "";
                throw new Win32Exception(err, $"寫入目標失敗 (offset={offset + done}, len={count - done}){hint}");
            }
            done += (int)written;
        }
    }

    /// <summary>Read-back verification: re-reads both devices and byte-compares every sector.</summary>
    private (bool Passed, string Summary) VerifyMatch(
        string sourcePath, string targetPath, long length, int sector,
        CloneResult result, IProgress<CloneProgress>? progress, CancellationToken ct)
    {
        try
        {
            return VerifyMatchOnce(sourcePath, targetPath, length, sector, result, progress, ct, unbuffered: true);
        }
        catch (UnbufferedIoNotSupportedException)
        {
            result.Diagnostics.Add("⚠️ 裝置拒絕無緩衝 I/O，改用系統快取進行讀回驗證。");
            progress?.Report(new CloneProgress { Stage = "讀回驗證中（快取模式）", Percent = 0, TotalBytes = length });
            return VerifyMatchOnce(sourcePath, targetPath, length, sector, result, progress, ct, unbuffered: false);
        }
    }

    private (bool Passed, string Summary) VerifyMatchOnce(
        string sourcePath, string targetPath, long length, int sector,
        CloneResult result, IProgress<CloneProgress>? progress, CancellationToken ct, bool unbuffered)
    {
        int chunk = AlignDown(DefaultChunkSize, sector);
        if (chunk <= 0) chunk = sector;

        uint readFlags = unbuffered ? FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN : FILE_FLAG_SEQUENTIAL_SCAN;
        using var src = CreateFile(sourcePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, readFlags, IntPtr.Zero);
        using var dst = CreateFile(targetPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, readFlags, IntPtr.Zero);
        if (src.IsInvalid || dst.IsInvalid)
            return (false, $"驗證失敗：無法以唯讀模式重新開啟裝置進行讀回比對（Win32 {Marshal.GetLastWin32Error()}）。");

        var bufA = VirtualAlloc(IntPtr.Zero, (UIntPtr)chunk, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        var bufB = VirtualAlloc(IntPtr.Zero, (UIntPtr)chunk, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (bufA == IntPtr.Zero || bufB == IntPtr.Zero)
        {
            if (bufA != IntPtr.Zero) VirtualFree(bufA, UIntPtr.Zero, MEM_RELEASE);
            if (bufB != IntPtr.Zero) VirtualFree(bufB, UIntPtr.Zero, MEM_RELEASE);
            return (false, "驗證失敗：無法配置驗證緩衝區。");
        }

        var verifyTimer = Stopwatch.StartNew();
        long verified = 0;
        long firstMismatch = -1;
        int mismatchedChunks = 0;
        long lastReportMs = -ProgressIntervalMs;

        try
        {
            while (verified < length)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(chunk, length - verified);
                want -= want % sector;
                if (want <= 0) break;

                try
                {
                    ReadExactly(src, bufA, want, verified);
                    ReadExactly(dst, bufB, want, verified);
                }
                catch (Win32Exception w32) when (unbuffered && verified == 0 && w32.NativeErrorCode == ERROR_INVALID_PARAMETER)
                {
                    throw new UnbufferedIoNotSupportedException($"Win32 {ERROR_INVALID_PARAMETER}");
                }

                var equalBytes = RtlCompareMemory(bufA, bufB, (UIntPtr)(ulong)want).ToUInt64();
                if (equalBytes != (ulong)want)
                {
                    mismatchedChunks++;
                    if (firstMismatch < 0)
                    {
                        var a = new byte[want];
                        var b = new byte[want];
                        Marshal.Copy(bufA, a, 0, want);
                        Marshal.Copy(bufB, b, 0, want);
                        for (int i = 0; i < want; i++)
                        {
                            if (a[i] != b[i]) { firstMismatch = verified + i; break; }
                        }
                    }
                }

                verified += want;
                if (verifyTimer.ElapsedMilliseconds - lastReportMs >= ProgressIntervalMs || verified >= length)
                {
                    lastReportMs = verifyTimer.ElapsedMilliseconds;
                    progress?.Report(new CloneProgress
                    {
                        Stage = unbuffered ? "讀回驗證中" : "讀回驗證中（快取模式）",
                        Percent = length > 0 ? verified * 100.0 / length : 100,
                        BytesDone = verified,
                        TotalBytes = length,
                        BytesPerSecond = verifyTimer.Elapsed.TotalSeconds > 0 ? verified / verifyTimer.Elapsed.TotalSeconds : 0,
                        Elapsed = verifyTimer.Elapsed,
                        Eta = null
                    });
                }
            }
        }
        finally
        {
            VirtualFree(bufA, UIntPtr.Zero, MEM_RELEASE);
            VirtualFree(bufB, UIntPtr.Zero, MEM_RELEASE);
        }

        if (mismatchedChunks == 0)
            return (true, $"讀回驗證通過：{NativeStorageService.FormatBytes(verified)} 與來源完全相符（逐磁區比對）。");

        result.Diagnostics.Add($"[VERIFY] 首個不符位元組位移 = 0x{firstMismatch:X}，不符區塊數 = {mismatchedChunks}");
        return (false, $"讀回驗證失敗：{mismatchedChunks} 個區塊與來源不符，首個差異位移 0x{firstMismatch:X}（{NativeStorageService.FormatBytes(verified)} 已比對）。");
    }

    // ───────────────────────── WMI helpers (read-only) ─────────────────────────

    /// <summary>Disk number that owns the running Windows installation, or null when it cannot be resolved.</summary>
    public static int? TryGetSystemDiskNumber()
    {
        var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\')[0];
        return TryGetDiskNumberOfLetter(systemDrive);
    }

    public static int? TryGetDiskNumberOfLetter(char driveLetter)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                var letterObj = obj["DriveLetter"];
                if (letterObj == null) continue;
                var letter = Convert.ToChar(letterObj);
                if (letter == '\0') continue;
                if (char.ToUpperInvariant(letter) == char.ToUpperInvariant(driveLetter))
                    return Convert.ToInt32(obj["DiskNumber"] ?? 0);
            }
        }
        catch
        {
            // Storage WMI unavailable (WinPE / older builds): report "unknown" so callers can stay conservative.
        }
        return null;
    }

    // ───────────────────────── Small numeric helpers ─────────────────────────

    private static int AlignDown(int value, int alignment)
    {
        if (alignment <= 0) return value;
        return value - (value % alignment);
    }

    private static int AlignUp(int value, int alignment)
    {
        if (alignment <= 0) return value;
        var rem = value % alignment;
        return rem == 0 ? value : value + (alignment - rem);
    }

    private static int Lcm(int a, int b)
    {
        if (a <= 0 || b <= 0) return Math.Max(1, Math.Max(a, b));
        return a / Gcd(a, b) * b;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) { (a, b) = (b, a % b); }
        return Math.Abs(a);
    }
}
