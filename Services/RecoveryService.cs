using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public enum RecoveryScanMode
{
    /// <summary>Try raw NTFS metadata; if raw access is unavailable fall back to the file-system level scan.</summary>
    Auto = 0,

    /// <summary>Raw NTFS $MFT only (needs Administrator for a live volume; an image file needs none).</summary>
    MftMetadata = 1,

    /// <summary>Signature carve of the free clusters of an NTFS volume / image.</summary>
    SignatureCarve = 2,

    /// <summary>No raw access: Recycle Bin payloads and other file-system level leftovers.</summary>
    FileSystemFallback = 3
}

public sealed class RecoveryScanOptions
{
    public RecoveryScanMode Mode { get; set; } = RecoveryScanMode.Auto;

    /// <summary>Additionally carve known file signatures out of free clusters (slow; needs $Bitmap to be readable).</summary>
    public bool EnableSignatureCarve { get; set; }

    /// <summary>Hard bound on how many MFT records are walked.</summary>
    public int MaxMftRecords { get; set; } = 2_000_000;

    /// <summary>Hard bound on carved candidates.</summary>
    public int MaxSignatureHits { get; set; } = 2_000;

    /// <summary>Hard bound on free clusters inspected by the carver.</summary>
    public long MaxClustersToScan { get; set; } = 500_000;

    /// <summary>Hard bound on the reconstructed length of a single carved file.</summary>
    public long MaxCarveBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Include Recycle Bin $I/$R leftovers in the file-system level scan.</summary>
    public bool IncludeRecycleBin { get; set; } = true;

    /// <summary>Include *.tmp / ~$* / *.bak residue (off by default: these are not deleted files).</summary>
    public bool IncludeTempResidue { get; set; }
}

public sealed class RecoveryVolumeInfo
{
    public string RootPath { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public DriveType DriveType { get; set; }
    public bool IsReady { get; set; }
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }

    public bool IsNtfs => string.Equals(FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase);

    public string DriveTypeText => DriveType switch
    {
        DriveType.Fixed => "本機磁碟",
        DriveType.Removable => "可移除裝置",
        DriveType.Network => "網路磁碟",
        DriveType.CDRom => "光碟機",
        DriveType.Ram => "RAM 磁碟",
        _ => DriveType.ToString()
    };

    public static string FormatBytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:F1} GB"
        : bytes >= 1024L * 1024 ? $"{bytes / 1024.0 / 1024:F0} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:F1} KB"
        : $"{bytes} B";

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? $"{RootPath.TrimEnd('\\')}  {FileSystem}  {DriveTypeText}  ({FormatBytes(TotalSize)})"
        : $"{RootPath.TrimEnd('\\')} [{Label}]  {FileSystem}  {DriveTypeText}  ({FormatBytes(TotalSize)})";

    public override string ToString() => DisplayName;
}

public sealed class RecoveryScanProgress
{
    public string Phase { get; set; } = "";
    public double Percent { get; set; }
    public string Detail { get; set; } = "";
    public int ItemsFound { get; set; }
}

public sealed class RecoveryScanResult
{
    public bool Success { get; set; }
    public string Target { get; set; } = "";
    public string VolumeRoot { get; set; } = "";
    public bool IsImage { get; set; }
    public bool UsedRawAccess { get; set; }
    public bool UsedFileSystemFallback { get; set; }
    public bool FreeClusterMapAvailable { get; set; }
    public string Error { get; set; } = "";
    public string CapabilityReport { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
    public List<RecoveredFileItem> Items { get; set; } = new();
}

public sealed class RecoveryItemOutcome
{
    public RecoveredFileItem? Item { get; set; }
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public long BytesWritten { get; set; }
    public long ZeroFilled { get; set; }
}

public sealed class RecoverySummary
{
    public bool Refused { get; set; }
    public string RefusalReason { get; set; } = "";
    public string DestinationRoot { get; set; } = "";
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public long BytesWritten { get; set; }
    public long ZeroFilled { get; set; }
    public TimeSpan Elapsed { get; set; }
    public List<RecoveryItemOutcome> Outcomes { get; set; } = new();

    public string Headline => Refused
        ? $"⛔ 已拒絕：{RefusalReason}"
        : $"復原結束 — 成功 {Succeeded} 個、失敗 {Failed} 個、略過 {Skipped} 個；共寫入 {RecoveryVolumeInfo.FormatBytes(BytesWritten)}，耗時 {Elapsed.TotalSeconds:F1} 秒。"
          + (ZeroFilled > 0 ? $" 另有 {RecoveryVolumeInfo.FormatBytes(ZeroFilled)} 因叢集資訊缺失而補零。" : "");
}

/// <summary>
/// Bounded, read-only file recovery engine.
///
/// WHAT IT CAN DO
///  • NTFS deleted $MFT entries (live volume, needs Administrator; or any .img/.dd image file, no elevation):
///    file name, parent path, size and MAC timestamps, plus the surviving $DATA run list and resident data.
///  • Free-cluster signature carving (optional): JPEG/PNG/GIF/BMP/TIFF/PDF/ZIP-OOXML/OLE2/RAR/7z/MP3/MP4 headers
///    found in clusters the $Bitmap still marks free.
///  • File-system level fallback without elevation: Recycle Bin $I/$R payloads (name, original path, size, deletion time).
///  • Safe recovery: read-only source handle, destination volume is compared by volume GUID, writes to the source volume are refused.
///
/// WHAT IT CANNOT DO — stated honestly
///  • Modern Windows zeroes an $MFT record when a file is deleted, so the $DATA run list is usually gone: the scan then
///    reports the name/size/timestamps but marks the content unrecoverable instead of inventing data.
///  • FAT/exFAT and ReFS deleted entries are not parsed (only the Recycle Bin fallback applies there).
///  • Compressed, EFS-encrypted and sparse files cannot be reassembled.
///  • Recovered files are never guaranteed: any write to the source volume (or continued use of the machine)
///    can reuse the free clusters. Confidence values are estimates, not promises.
///  • No TRIM/SSD caveat is hidden: on SSD the controller may return zeros for free flash pages.
/// </summary>
public sealed class RecoveryService
{
    public const string CapabilitySummary =
        "可復原：NTFS 刪除的 MFT 項目（檔名/路徑/時間/倖存的資料叢集）、可選的已知檔案簽章雕刻、資源回收筒殘留檔案（免管理員）。" +
        "無法復原：MFT 記錄被 Windows 清除後已遺失的資料叢集、FAT/exFAT/ReFS 刪除項目、壓縮/EFS 加密/稀疏檔案內容。" +
        "實體磁碟區的原始讀取需要系統管理員權限；磁碟映像檔（.img/.dd）不需要。";

    // ══════════════════════════════════════════════════════════════════════
    //  Volume enumeration / targets
    // ══════════════════════════════════════════════════════════════════════

    public IReadOnlyList<RecoveryVolumeInfo> EnumerateVolumes()
    {
        var list = new List<RecoveryVolumeInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var info = new RecoveryVolumeInfo { RootPath = drive.Name, DriveType = drive.DriveType };
                try
                {
                    info.IsReady = drive.IsReady;
                    if (info.IsReady)
                    {
                        info.Label = drive.VolumeLabel;
                        info.FileSystem = drive.DriveFormat;
                        info.TotalSize = drive.TotalSize;
                        info.FreeSpace = drive.AvailableFreeSpace;
                    }
                }
                catch { }
                list.Add(info);
            }
        }
        catch { }

        return list
            .OrderByDescending(v => v.IsReady)
            .ThenByDescending(v => v.IsNtfs)
            .ThenBy(v => v.DriveType == DriveType.Fixed ? 0 : v.DriveType == DriveType.Removable ? 1 : 2)
            .ThenBy(v => v.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Maps any path (file, folder or volume root) to the volume root it lives on, e.g. "D:\".</summary>
    public static string ResolveVolumeRoot(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var trimmed = path.Trim().Trim('"');

            if (trimmed.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.EndsWith("\\") ? trimmed : trimmed + "\\";
            }

            // A directory that is itself a mount point / volume root
            var root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
            {
                return char.ToUpperInvariant(root[0]) + ":\\";
            }

            if (Directory.Exists(trimmed))
            {
                var di = new DirectoryInfo(trimmed);
                var full = di.FullName;
                return char.ToUpperInvariant(full[0]) + ":\\";
            }
        }
        catch { }
        return "";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Scanning
    // ══════════════════════════════════════════════════════════════════════

    public Task<RecoveryScanResult> ScanAsync(
        string target,
        RecoveryScanOptions? options,
        IProgress<RecoveryScanProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        options ??= new RecoveryScanOptions();
        return Task.Run(() => Scan(target, options, progress, log, cancellationToken), cancellationToken);
    }

    private RecoveryScanResult Scan(
        string target,
        RecoveryScanOptions options,
        IProgress<RecoveryScanProgress>? progress,
        Action<string>? log,
        CancellationToken ct)
    {
        var result = new RecoveryScanResult { Target = target };
        void Info(string message)
        {
            try { log?.Invoke(message); } catch { }
        }

        try
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                result.Error = "請先選擇要掃描的磁碟區、資料夾或磁碟映像檔。";
                return result;
            }

            var clean = target.Trim().Trim('"');
            bool isImage = File.Exists(clean) && !clean.StartsWith(@"\\", StringComparison.Ordinal);
            result.IsImage = isImage;
            result.VolumeRoot = isImage ? "" : ResolveVolumeRoot(clean);

            progress?.Report(new RecoveryScanProgress { Phase = "初始化", Percent = 0, Detail = isImage ? $"開啟磁碟映像檔 {clean}" : $"開啟磁碟區 {result.VolumeRoot}" });

            bool rawOnly = options.Mode == RecoveryScanMode.MftMetadata;
            bool fallbackOnly = options.Mode == RecoveryScanMode.FileSystemFallback;
            bool wantRaw = !fallbackOnly;
            bool wantFallback = fallbackOnly || options.Mode == RecoveryScanMode.Auto;

            if (!isImage && string.IsNullOrEmpty(result.VolumeRoot))
            {
                result.Error = "無法判斷來源磁碟區（請輸入例如 D:\\ 的磁碟機根目錄，或選擇 .img/.dd 映像檔）。";
                return result;
            }

            RawBlockSource? raw = null;
            string openError = "";
            if (wantRaw)
            {
                raw = isImage
                    ? RawBlockSource.TryOpenImage(clean, out openError)
                    : RawBlockSource.TryOpenVolume(result.VolumeRoot, out openError);
            }

            if (wantRaw && raw == null)
            {
                Info($"[WARN] 無法取得原始讀取權限：{openError}");
                if (isImage)
                {
                    result.Error = $"無法開啟映像檔：{openError}";
                    return result;
                }
                if (rawOnly || options.Mode == RecoveryScanMode.SignatureCarve)
                {
                    result.Error = $"無法讀取原始磁碟區（{openError}）NTFS MFT / 簽章掃描需要以系統管理員身分執行，或改用磁碟映像檔。";
                    return result;
                }
                result.Warnings.Add($"無法讀取原始磁碟區：{openError} 已改用免管理員的檔案系統層級掃描。");
                wantFallback = true;
            }

            try
            {
                if (raw != null)
                {
                    var ntfs = TryScanNtfs(raw, result, options, progress, Info, ct);
                    if (ntfs != null)
                    {
                        result.Items.AddRange(ntfs);
                        result.UsedRawAccess = true;
                    }
                    else if (isImage)
                    {
                        result.Error = "映像檔不是可解析的 NTFS 映像（本工具目前僅支援 NTFS 的原始解析）。";
                        return result;
                    }
                    else
                    {
                        result.Warnings.Add("來源不是可解析的 NTFS 磁碟區（或開機磁區無法解析），已改用檔案系統層級掃描。");
                        Info("[WARN] 非 NTFS 或無法解析開機磁區 — 改用檔案系統層級掃描。");
                        wantFallback = !rawOnly;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Warnings.Add($"NTFS 掃描失敗：{ex.Message}");
                Info($"[ERROR] NTFS 掃描失敗：{ex.Message}");
                wantFallback = !isImage && !rawOnly;
            }
            finally
            {
                raw?.Dispose();
                raw = null;
            }

            if (wantFallback && !string.IsNullOrEmpty(result.VolumeRoot))
            {
                ScanFileSystemFallback(result, options, progress, Info, ct);
            }

            if (result.Items.Count == 0 && string.IsNullOrEmpty(result.Error))
            {
                result.Warnings.Add("掃描完成，但沒有找到可辨識的刪除項目。");
            }

            result.Success = string.IsNullOrEmpty(result.Error);
            result.CapabilityReport = BuildCapabilityReport(result);
            progress?.Report(new RecoveryScanProgress { Phase = "完成", Percent = 100, Detail = $"找到 {result.Items.Count} 個候選項目", ItemsFound = result.Items.Count });
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Error = "掃描已由使用者取消。";
            result.Success = false;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"掃描失敗：{ex.Message}";
            result.Success = false;
            return result;
        }
    }

    private static string BuildCapabilityReport(RecoveryScanResult result)
    {
        var sb = new StringBuilder();
        sb.Append(result.UsedRawAccess
            ? "✅ 已直接解析 NTFS $MFT 刪除項目（檔名／路徑／時間／倖存的資料叢集）。"
            : "ℹ️ 未使用原始磁碟讀取（未以系統管理員身分執行或來源為非 NTFS）。");
        if (result.UsedFileSystemFallback) sb.Append(" 已加入檔案系統層級救援（資源回收筒殘留檔案）。");
        sb.Append(result.FreeClusterMapAvailable ? " $Bitmap 可讀取，已能判斷叢集是否仍為可用空間。" : " $Bitmap 無法讀取，無法判斷叢集是否已被其他檔案占用。");
        sb.Append(" 提醒：只要繼續使用該磁碟區，可用叢集就可能被覆寫；請立刻把要救的檔案寫到其他磁碟。");
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  NTFS $MFT metadata scan
    // ══════════════════════════════════════════════════════════════════════

    private List<RecoveredFileItem>? TryScanNtfs(
        RawBlockSource src,
        RecoveryScanResult result,
        RecoveryScanOptions options,
        IProgress<RecoveryScanProgress>? progress,
        Action<string> info,
        CancellationToken ct)
    {
        var boot = new byte[512];
        if (!src.TryRead(0, boot)) return null;
        if (boot[3] != 'N' || boot[4] != 'T' || boot[5] != 'F' || boot[6] != 'S') return null;

        int bytesPerSector = U16(boot, 0x0B);
        int sectorsPerCluster = boot[0x0D];
        long totalSectors = I64(boot, 0x28);
        long mftLcn = I64(boot, 0x30);
        sbyte clustersPerRecord = unchecked((sbyte)boot[0x40]);

        if (bytesPerSector is not (512 or 1024 or 2048 or 4096)) return null;
        if (sectorsPerCluster <= 0) return null;
        long clusterSize = (long)bytesPerSector * sectorsPerCluster;
        int recordSize = clustersPerRecord < 0 ? 1 << -clustersPerRecord : (int)(clustersPerRecord * clusterSize);
        if (recordSize is < 256 or > 65536) return null;
        if (mftLcn <= 0) return null;

        info($"[NTFS] 磁區 {bytesPerSector}B、叢集 {clusterSize}B、MFT 記錄 {recordSize}B、MFT 起始叢集 {mftLcn}");

        var mftRecord0 = ReadMftRecord(src, mftLcn * clusterSize, recordSize, bytesPerSector);
        if (mftRecord0 == null) { info("[WARN] 無法讀取 $MFT 記錄 0"); return new List<RecoveredFileItem>(); }

        var mftRuns = new List<RecoveryDataRun>();
        long mftRealSize = 0;
        foreach (var attr in ParseAttributes(mftRecord0))
        {
            if (attr.Type != 0x80 || !attr.NonResident) continue;
            if (!TryGetNonResidentInfo(mftRecord0, attr, out var realSize, out _, out int runsOffset)) continue;
            var runs = ParseMappingPairs(mftRecord0.AsSpan(attr.Offset + runsOffset, attr.Length - runsOffset));
            mftRuns.AddRange(runs);
            mftRealSize = Math.Max(mftRealSize, realSize);
        }

        if (mftRuns.Count == 0)
        {
            info("[WARN] $MFT 沒有可用的資料執行（run list），無法列舉 MFT 記錄。");
            return new List<RecoveredFileItem>();
        }

        long clustersFromRuns = mftRuns.Where(r => !r.IsSparse).Sum(r => r.ClusterCount);
        if (mftRealSize <= 0) mftRealSize = clustersFromRuns * clusterSize;
        long recordCount = Math.Min(mftRealSize / recordSize, options.MaxMftRecords);
        if (recordCount <= 0) return new List<RecoveredFileItem>();
        info($"[NTFS] $MFT 大小 {RecoveryVolumeInfo.FormatBytes(mftRealSize)}（{recordCount:N0} 筆記錄）");

        // $Bitmap (record 6) lets us tell whether a cluster was reused by another file.
        FreeClusterMap? freeMap = null;
        try
        {
            var bitmapRecord = ReadMftRecordFromRuns(src, mftRuns, recordSize, clusterSize, 6, bytesPerSector);
            if (bitmapRecord != null)
            {
                foreach (var attr in ParseAttributes(bitmapRecord))
                {
                    if (attr.Type != 0x80 || !attr.NonResident) continue;
                    if (!TryGetNonResidentInfo(bitmapRecord, attr, out var bitmapSize, out _, out int runsOffset)) continue;
                    var bitmapRuns = ParseMappingPairs(bitmapRecord.AsSpan(attr.Offset + runsOffset, attr.Length - runsOffset));
                    if (bitmapRuns.Count > 0 && bitmapSize > 0)
                    {
                        freeMap = new FreeClusterMap(src, bitmapRuns, clusterSize, Math.Max(bitmapSize, totalSectors * bytesPerSector / clusterSize / 8));
                        result.FreeClusterMapAvailable = true;
                        info("[NTFS] $Bitmap 已載入 — 可判斷叢集是否仍為可用空間。");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            info($"[WARN] $Bitmap 無法解析（{ex.Message}）— 將無法判斷叢集是否已被占用。");
        }

        // ── Walk every MFT record in bounded blocks ──────────────────────────
        var items = new List<RecoveredFileItem>();
        var nameCache = new Dictionary<uint, (string? Name, uint Parent)>();
        var totalBytes = recordCount * recordSize;
        int recordsPerBlock = Math.Max(1, 256 * 1024 / recordSize);
        var block = new byte[recordsPerBlock * (long)recordSize <= int.MaxValue ? (int)(recordsPerBlock * (long)recordSize) : 256 * 1024];
        long deletedSeen = 0;

        for (long blockStart = 0; blockStart < recordCount; blockStart += recordsPerBlock)
        {
            ct.ThrowIfCancellationRequested();
            long byteStart = blockStart * recordSize;
            int byteCount = (int)Math.Min(block.Length, totalBytes - byteStart);
            byteCount -= byteCount % recordSize;
            if (byteCount <= 0) break;

            if (!TryReadFromRuns(src, mftRuns, clusterSize, byteStart, block, byteCount))
            {
                result.Warnings.Add($"MFT 讀取中斷於偏移 {byteStart:N0}（執行清單可能不完整）。");
                info($"[WARN] MFT 讀取中斷於偏移 {byteStart:N0}");
                break;
            }

            int recordCountInBlock = byteCount / recordSize;
            for (int i = 0; i < recordCountInBlock; i++)
            {
                ct.ThrowIfCancellationRequested();
                long recordNumber = blockStart + i;
                var rec = block.AsSpan(i * recordSize, recordSize);

                if (U32(rec, 0) != 0x454C4946) continue;              // "FILE"
                if (!ApplyFixup(rec, bytesPerSector)) continue;        // torn / unused record

                ushort flags = U16(rec, 0x16);
                bool inUse = (flags & 0x0001) != 0;
                bool isDirectory = (flags & 0x0002) != 0;
                if (inUse) continue;                                   // live file — nothing to recover

                long baseRecord = I64(rec, 0x20);
                if (baseRecord != 0) continue;                         // attribute extension record

                var attrs = ParseAttributes(rec);
                var names = new List<FileNameInfo>();
                byte[]? residentData = null;
                var dataRuns = new List<RecoveryDataRun>();
                long dataRealSize = 0, dataAllocatedSize = 0;
                bool dataIsNonResident = false;
                bool compressed = false, encrypted = false, sparse = false;

                foreach (var attr in attrs)
                {
                    if (attr.Type == 0x30)
                    {
                        var fn = ParseFileName(rec, attr);
                        if (fn != null && fn.Name.Length > 0) names.Add(fn);
                    }
                    else if (attr.Type == 0x80 && !attr.IsNamed())
                    {
                        if (attr.NonResident)
                        {
                            if (TryGetNonResidentInfo(rec, attr, out var realSize, out var allocSize, out int runsOffset))
                            {
                                dataRuns.AddRange(ParseMappingPairs(rec.Slice(attr.Offset + runsOffset, attr.Length - runsOffset)));
                                dataRealSize = Math.Max(dataRealSize, realSize);
                                dataAllocatedSize = Math.Max(dataAllocatedSize, allocSize);
                                dataIsNonResident = true;
                            }
                        }
                        else
                        {
                            residentData = GetResidentData(rec, attr) ?? residentData;
                            if (residentData != null) dataRealSize = Math.Max(dataRealSize, residentData.Length);
                        }
                        int attrFlags = U16(rec, attr.Offset + 0x0C);
                        compressed |= (attrFlags & 0x0001) != 0;
                        encrypted |= (attrFlags & 0x4000) != 0;
                        sparse |= (attrFlags & 0x8000) != 0;
                    }
                }

                if (names.Count == 0) continue;
                var primary = names.FirstOrDefault(n => n.Namespace is 1 or 3) ?? names[0];
                if (string.IsNullOrWhiteSpace(primary.Name)) continue;
                if (primary.Name.StartsWith('$')) continue;            // NTFS metadata files
                if (recordNumber < 24) continue;                       // reserved system records

                deletedSeen++;
                var item = new RecoveredFileItem
                {
                    Name = primary.Name,
                    MftRecordNumber = (uint)recordNumber,
                    IsDirectory = isDirectory,
                    CreatedAt = primary.Created,
                    ModifiedAt = primary.Modified,
                    VolumeRoot = result.IsImage ? result.Target : result.VolumeRoot,
                    SourceIsImage = result.IsImage,
                    SourcePath = result.IsImage ? result.Target : result.VolumeRoot,
                    SourceClusterSize = (int)clusterSize,
                    SizeBytes = dataRealSize > 0 ? dataRealSize : primary.RealSize,
                    MethodText = "NTFS $MFT 刪除項目"
                };

                item.OriginalPath = ResolveOriginalPath(src, mftRuns, recordSize, clusterSize, bytesPerSector,
                    primary.ParentRecord, primary.Name, nameCache, 32) ?? "";
                if (isDirectory)
                {
                    item.SourceKind = RecoverySourceKind.DirectoryEntry;
                    item.Confidence = RecoveryConfidence.Medium;
                    item.ConfidenceDetail = "僅目錄項目本身；子檔案需各自復原。";
                }
                else if (residentData is { Length: > 0 })
                {
                    item.SourceKind = RecoverySourceKind.MftResident;
                    item.InlineData = residentData;
                    item.SizeBytes = residentData.Length;
                    item.Confidence = RecoveryConfidence.High;
                    item.ConfidenceDetail = "資料仍保存在 MFT 記錄內（常駐），成功率最高。";
                }
                else if (dataRuns.Count > 0)
                {
                    item.SourceKind = RecoverySourceKind.MftClusters;
                    item.DataRuns = dataRuns;
                    item.Confidence = RecoveryConfidence.Medium;
                    item.ConfidenceDetail = "已從刪除記錄取得資料叢集位置，但叢集可能已被其他檔案覆寫。";

                    if (compressed || encrypted || sparse)
                    {
                        item.Confidence = RecoveryConfidence.Low;
                        item.ConfidenceDetail = compressed ? "檔案為 NTFS 壓縮格式，無法還原原始內容。"
                            : encrypted ? "檔案已由 EFS 加密，無金鑰無法還原內容。"
                            : "檔案為稀疏檔案，內容不完整。";
                    }
                    else
                    {
                        var zeroed = FirstClusterLooksZeroed(src, dataRuns, clusterSize);
                        if (zeroed)
                        {
                            item.Confidence = RecoveryConfidence.Low;
                            item.ConfidenceDetail = "首個資料叢集全為 0x00 — 內容很可能已被覆寫或歸零。";
                        }
                        else if (freeMap != null)
                        {
                            var (checkedClusters, allocated) = freeMap.CountAllocated(dataRuns, 64);
                            if (checkedClusters > 0 && allocated == checkedClusters)
                            {
                                item.Confidence = RecoveryConfidence.Low;
                                item.ConfidenceDetail = "資料叢集已被其他檔案占用（$Bitmap 顯示非可用空間）。";
                            }
                            else if (checkedClusters > 0 && allocated > 0)
                            {
                                item.Confidence = RecoveryConfidence.Low;
                                item.ConfidenceDetail = $"部分資料叢集已被占用（{allocated}/{checkedClusters}），內容可能殘缺。";
                            }
                            else
                            {
                                item.ConfidenceDetail += " $Bitmap 顯示這些叢集仍為可用空間。";
                            }
                        }
                    }
                }
                else
                {
                    item.SourceKind = RecoverySourceKind.MftClusters;
                    item.Confidence = RecoveryConfidence.Unrecoverable;
                    item.ConfidenceDetail = dataIsNonResident
                        ? "找到 $DATA 屬性但資料執行（run list）無法解析，無法安全定位內容。"
                        : "Windows 刪除檔案時已清除 $DATA 屬性（資料叢集位置遺失），只能看到檔名與時間。";
                }

                items.Add(item);
            }

            if ((blockStart / recordsPerBlock) % 16 == 0)
            {
                double pct = Math.Min(99.0, (blockStart + recordsPerBlock) * 100.0 / recordCount);
                progress?.Report(new RecoveryScanProgress
                {
                    Phase = "解析 NTFS $MFT",
                    Percent = pct,
                    Detail = $"已檢查 {Math.Min(blockStart + recordsPerBlock, recordCount):N0} / {recordCount:N0} 筆記錄，找到 {items.Count} 個刪除項目",
                    ItemsFound = items.Count
                });
            }
        }

        info($"[NTFS] MFT 掃描完成：{deletedSeen:N0} 筆非使用中記錄、{items.Count} 個可辨識的刪除項目。");

        if (options.EnableSignatureCarve || options.Mode == RecoveryScanMode.SignatureCarve)
        {
            if (freeMap == null)
            {
                result.Warnings.Add("已要求簽章雕刻，但 $Bitmap 無法讀取，無法判斷哪些叢集是可用空間 — 已略過雕刻。");
                info("[WARN] 無法讀取 $Bitmap，略過簽章雕刻。");
            }
            else
            {
                try
                {
                    var carved = CarveSignatures(src, freeMap, clusterSize, totalSectors * bytesPerSector, options, progress, info, ct);
                    info($"[CARVE] 簽章雕刻找到 {carved.Count} 個候選檔案。");
                    items.AddRange(carved);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result.Warnings.Add($"簽章雕刻失敗：{ex.Message}");
                }
            }
        }

        items = items
            .OrderByDescending(x => x.Confidence == RecoveryConfidence.High)
            .ThenByDescending(x => x.IsRecoverable)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return items;
    }

    /// <summary>
    /// Walks the parent chain of a deleted record until the NTFS root (record 5) to rebuild
    /// "C:\Users\Alice\Docs\report.docx". Missing/unreadable parents stop the walk (partial path).
    /// </summary>
    private static string? ResolveOriginalPath(
        RawBlockSource src,
        List<RecoveryDataRun> mftRuns,
        int recordSize,
        long clusterSize,
        int bytesPerSector,
        uint parentRecord,
        string fileName,
        Dictionary<uint, (string? Name, uint Parent)> cache,
        int maxDepth)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(fileName)) segments.Add(fileName);
        var visited = new HashSet<uint>();
        uint current = parentRecord;

        try
        {
            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current == 5 || current == 0) break;          // 5 = NTFS root directory "."
                if (!visited.Add(current)) break;                 // cycle guard

                if (!cache.TryGetValue(current, out var entry))
                {
                    var rec = ReadMftRecordFromRuns(src, mftRuns, recordSize, clusterSize, current, bytesPerSector);
                    if (rec == null) { cache[current] = (null, 0); break; }

                    var attrs = ParseAttributes(rec);
                    FileNameInfo? fn = null;
                    foreach (var attr in attrs)
                    {
                        if (attr.Type != 0x30) continue;
                        var candidate = ParseFileName(rec, attr);
                        if (candidate == null || candidate.Name.Length == 0) continue;
                        if (candidate.Namespace is 1 or 3) { fn = candidate; break; }
                        fn ??= candidate;
                    }
                    entry = fn == null ? (null, 0u) : (fn.Name, fn.ParentRecord);
                    cache[current] = entry;
                }

                if (string.IsNullOrEmpty(entry.Name) || entry.Name == ".") break;
                segments.Add(entry.Name);
                current = entry.Parent;
            }
        }
        catch { }

        if (segments.Count <= 1) return segments.Count == 1 ? segments[0] : null;   // parent chain unknown: name only
        segments.Reverse();
        var root = ResolveVolumeRoot(src.Identity);
        var joined = string.Join("\\", segments);
        return string.IsNullOrEmpty(root) ? joined : root.TrimEnd('\\') + "\\" + joined;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Signature carving of free clusters
    // ══════════════════════════════════════════════════════════════════════

    private static readonly (string Ext, string Label, byte[] Magic, int Offset)[] Signatures =
    {
        (".jpg",  "JPEG 影像",      new byte[] { 0xFF, 0xD8, 0xFF }, 0),
        (".png",  "PNG 影像",       new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0),
        (".gif",  "GIF 影像",       new byte[] { 0x47, 0x49, 0x46, 0x38 }, 0),
        (".bmp",  "BMP 影像",       new byte[] { 0x42, 0x4D }, 0),
        (".tif",  "TIFF 影像",      new byte[] { 0x49, 0x49, 0x2A, 0x00 }, 0),
        (".pdf",  "PDF 文件",       new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, 0),
        (".zip",  "ZIP / OOXML",    new byte[] { 0x50, 0x4B, 0x03, 0x04 }, 0),
        (".rar",  "RAR 壓縮檔",     new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }, 0),
        (".7z",   "7-Zip 壓縮檔",   new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, 0),
        (".mp4",  "MP4 / MOV 視訊", new byte[] { 0x66, 0x74, 0x79, 0x70 }, 4),
        (".mp3",  "MP3 音訊",       new byte[] { 0x49, 0x44, 0x33 }, 0),
        (".doc",  "OLE2 文件",      new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, 0)
    };

    private List<RecoveredFileItem> CarveSignatures(
        RawBlockSource src,
        FreeClusterMap freeMap,
        long clusterSize,
        long volumeBytes,
        RecoveryScanOptions options,
        IProgress<RecoveryScanProgress>? progress,
        Action<string> info,
        CancellationToken ct)
    {
        var found = new List<RecoveredFileItem>();
        long totalClusters = volumeBytes / clusterSize;
        long scanLimit = Math.Min(totalClusters, options.MaxClustersToScan);
        var cluster = new byte[clusterSize];
        int index = 0;

        for (long lcn = 0; lcn < scanLimit && found.Count < options.MaxSignatureHits; lcn++)
        {
            if ((lcn & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RecoveryScanProgress
                {
                    Phase = "簽章雕刻（可用叢集）",
                    Percent = Math.Min(99.0, lcn * 100.0 / scanLimit),
                    Detail = $"已掃描 {lcn:N0} / {scanLimit:N0} 個叢集，找到 {found.Count} 個簽章",
                    ItemsFound = found.Count
                });
            }

            if (!freeMap.IsFree(lcn)) continue;
            if (!src.TryRead(lcn * clusterSize, cluster)) continue;

            for (int sectorOffset = 0; sectorOffset + 16 <= cluster.Length; sectorOffset += 512)
            {
                var match = MatchSignature(cluster.AsSpan(sectorOffset));
                if (match == null) continue;

                long hitOffset = lcn * clusterSize + sectorOffset;
                long available = ContiguousFreeBytes(freeMap, lcn, clusterSize, options.MaxCarveBytes);
                long length = CarveLength(src, hitOffset, available, match.Value.Ext, options.MaxCarveBytes);
                if (length < 64) continue;

                index++;
                var item = new RecoveredFileItem
                {
                    Name = $"carved_{index:D4}{match.Value.Ext}",
                    IsDirectory = false,
                    SizeBytes = length,
                    Confidence = RecoveryConfidence.Low,
                    ConfidenceDetail = $"以 {match.Value.Label} 簽章於可用叢集重建（檔名、路徑與完整性皆未知）。",
                    SourceKind = RecoverySourceKind.CarvedSignature,
                    SourceIsImage = src.IsImage,
                    SourcePath = src.Identity,
                    VolumeRoot = src.IsImage ? "" : src.Identity,
                    RawOffset = hitOffset,
                    RawLength = length,
                    SourceClusterSize = (int)clusterSize,
                    MethodText = "可用叢集簽章雕刻"
                };
                found.Add(item);
                info($"[CARVE] {match.Value.Label} @ offset {hitOffset:N0} → {item.Name} ({RecoveryVolumeInfo.FormatBytes(length)})");
                break;
            }
        }

        return found;
    }

    private static (string Ext, string Label)? MatchSignature(ReadOnlySpan<byte> buffer)
    {
        foreach (var sig in Signatures)
        {
            if (buffer.Length < sig.Offset + sig.Magic.Length) continue;
            bool ok = true;
            for (int i = 0; i < sig.Magic.Length; i++)
            {
                if (buffer[sig.Offset + i] != sig.Magic[i]) { ok = false; break; }
            }
            if (ok) return (sig.Ext, sig.Label);
        }
        return null;
    }

    private static long ContiguousFreeBytes(FreeClusterMap freeMap, long startLcn, long clusterSize, long cap)
    {
        long clusters = 0;
        long maxClusters = cap / clusterSize;
        while (clusters < maxClusters && freeMap.IsFree(startLcn + clusters)) clusters++;
        return clusters * clusterSize;
    }

    private static long CarveLength(RawBlockSource src, long offset, long available, string ext, long cap)
    {
        long probe = Math.Min(available, Math.Min(cap, 8L * 1024 * 1024));
        if (probe <= 0) return 0;
        var buffer = new byte[probe];
        if (!src.TryRead(offset, buffer)) return 0;

        (byte[] Needle, int Tail)[] terminators = ext switch
        {
            ".jpg" => new[] { (new byte[] { 0xFF, 0xD9 }, 0) },
            ".png" => new[] { (Encoding.ASCII.GetBytes("IEND"), 4) },
            ".pdf" => new[] { (Encoding.ASCII.GetBytes("%%EOF"), 0) },
            ".gif" => new[] { (new byte[] { 0x3B }, 0) },
            ".zip" => new[] { (Encoding.ASCII.GetBytes("PK\u0005\u0006"), 18) },
            _ => Array.Empty<(byte[], int)>()
        };

        foreach (var (needle, tail) in terminators)
        {
            int idx = IndexOf(buffer, needle);
            if (idx >= 0)
            {
                long end = idx + needle.Length + tail;
                if (end >= 64 && end <= available) return end;
            }
        }

        return Math.Min(available, cap);
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  File-system level fallback (no elevation)
    // ══════════════════════════════════════════════════════════════════════

    private void ScanFileSystemFallback(
        RecoveryScanResult result,
        RecoveryScanOptions options,
        IProgress<RecoveryScanProgress>? progress,
        Action<string> info,
        CancellationToken ct)
    {
        result.UsedFileSystemFallback = true;
        var root = result.VolumeRoot;
        info($"[FALLBACK] 檔案系統層級掃描：{root}（免管理員權限）");
        progress?.Report(new RecoveryScanProgress { Phase = "資源回收筒", Percent = 5, Detail = "列舉 $Recycle.Bin 殘留檔案" });

        if (options.IncludeRecycleBin)
        {
            foreach (var sidDir in SafeEnumerateDirectories(Path.Combine(root, "$Recycle.Bin")))
            {
                ct.ThrowIfCancellationRequested();
                int added = 0;
                foreach (var iFile in SafeEnumerateFiles(sidDir, "$I*"))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var name = Path.GetFileName(iFile);
                        var rPath = Path.Combine(sidDir, "$R" + name.Substring(2));
                        var meta = ParseRecycleInfo(iFile);
                        bool payloadExists = File.Exists(rPath);

                        var item = new RecoveredFileItem
                        {
                            Name = meta.Name,
                            OriginalPath = meta.OriginalPath,
                            SizeBytes = meta.Size,
                            DeletedAt = meta.DeletedAt,
                            IsDirectory = false,
                            VolumeRoot = root,
                            MethodText = "資源回收筒殘留（$I/$R）",
                            SourceKind = RecoverySourceKind.DirectFileCopy,
                            SourcePath = payloadExists ? rPath : "",
                            Confidence = payloadExists ? RecoveryConfidence.High : RecoveryConfidence.Unrecoverable,
                            ConfidenceDetail = payloadExists
                                ? "檔案內容仍在 $Recycle.Bin 中，可直接複製（非破壞性）。"
                                : "只找到 $I 中繼資料，$R 內容已不存在 — 無法復原。"
                        };
                        if (payloadExists)
                        {
                            try { item.SizeBytes = new FileInfo(rPath).Length; } catch { }
                        }
                        result.Items.Add(item);
                        added++;
                    }
                    catch { }
                }
                if (added > 0) info($"[FALLBACK] {sidDir} → {added} 個資源回收筒項目");
            }
        }

        if (options.IncludeTempResidue)
        {
            progress?.Report(new RecoveryScanProgress { Phase = "暫存殘留", Percent = 40, Detail = "掃描 ~$ / *.tmp / *.bak 殘留" });
            foreach (var dir in new[]
                     {
                         Path.GetTempPath(),
                         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                         Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                     })
            {
                ct.ThrowIfCancellationRequested();
                foreach (var pattern in new[] { "~$*", "*.tmp", "*.bak", "*.old", "*.wbk" })
                {
                    foreach (var file in SafeEnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly, maxFiles: 2000))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length <= 0) continue;
                            result.Items.Add(new RecoveredFileItem
                            {
                                Name = fi.Name,
                                OriginalPath = fi.FullName,
                                SizeBytes = fi.Length,
                                CreatedAt = fi.CreationTime,
                                ModifiedAt = fi.LastWriteTime,
                                VolumeRoot = root,
                                MethodText = "暫存 / 備份殘留檔案",
                                SourceKind = RecoverySourceKind.DirectFileCopy,
                                SourcePath = fi.FullName,
                                Confidence = RecoveryConfidence.Low,
                                ConfidenceDetail = "這是仍存在的暫存／備份檔案，不是刪除項目；內容可能不完整。"
                            });
                        }
                        catch { }
                    }
                }
            }
        }

        result.Items = result.Items
            .OrderByDescending(x => x.Confidence == RecoveryConfidence.High)
            .ThenByDescending(x => x.DeletedAt ?? DateTime.MinValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly, int maxFiles = 20000)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(path)) return list;
            foreach (var f in Directory.EnumerateFiles(path, pattern, option))
            {
                list.Add(f);
                if (list.Count >= maxFiles) break;
            }
        }
        catch { }
        return list;
    }

    private sealed class RecycleInfo
    {
        public string Name = "";
        public string OriginalPath = "";
        public long Size;
        public DateTime? DeletedAt;
    }

    /// <summary>Parses a Windows Vista+ $I metadata file: version(8) / size(8) / FILETIME(8) / path.</summary>
    private static RecycleInfo ParseRecycleInfo(string iFilePath)
    {
        var info = new RecycleInfo { Name = Path.GetFileName(iFilePath) };
        try
        {
            var bytes = File.ReadAllBytes(iFilePath);
            if (bytes.Length < 24) return info;
            long version = BitConverter.ToInt64(bytes, 0);
            info.Size = BitConverter.ToInt64(bytes, 8);
            long fileTime = BitConverter.ToInt64(bytes, 16);
            try { info.DeletedAt = DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); } catch { }

            if (version == 2)
            {
                // Windows 10/11 writes a 4-byte path-length prefix at 0x18 (path starts at 0x1C);
                // the older Vista/7 layout stores the UTF-16 path directly at 0x18.
                int start = 24;
                int pathChars = -1;
                if (bytes.Length >= 28)
                {
                    int declaredLength = BitConverter.ToInt32(bytes, 24);
                    if (declaredLength > 0 && declaredLength % 2 == 0 && declaredLength <= bytes.Length - 28)
                    {
                        start = 28;
                        pathChars = declaredLength / 2;
                    }
                }
                int available = (bytes.Length - start) / 2;
                int count = pathChars > 0 ? Math.Min(pathChars, available) : available;
                var chars = Encoding.Unicode.GetString(bytes, start, count * 2);
                info.OriginalPath = chars.TrimEnd('\0');
            }
            else
            {
                // Windows XP layout: fixed ANSI buffer after the 24-byte header.
                var text = Encoding.Latin1.GetString(bytes, 24, Math.Min(260, bytes.Length - 24));
                info.OriginalPath = text.TrimEnd('\0');
            }

            info.Name = string.IsNullOrWhiteSpace(info.OriginalPath)
                ? Path.GetFileName(iFilePath)
                : Path.GetFileName(info.OriginalPath.TrimEnd('\\'));
            if (string.IsNullOrWhiteSpace(info.Name)) info.Name = Path.GetFileName(iFilePath);
        }
        catch { }
        return info;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Recovery (write) — never touches the source volume
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Refuses any destination that would write back onto the source volume (or into the source image).</summary>
    public (bool Ok, string Message) CheckDestination(string destinationRoot, IReadOnlyList<RecoveredFileItem> items)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
            return (false, "尚未選擇復原目的資料夾。");

        string destFull;
        try
        {
            destFull = Path.GetFullPath(destinationRoot.Trim().Trim('"'));
        }
        catch (Exception ex)
        {
            return (false, $"目的路徑無效：{ex.Message}");
        }

        if (File.Exists(destFull)) return (false, "目的路徑是一個檔案，請選擇資料夾。");

        foreach (var group in items.Where(i => i.SourceIsImage && !string.IsNullOrWhiteSpace(i.SourcePath)).GroupBy(i => i.SourcePath))
        {
            try
            {
                var imageFull = Path.GetFullPath(group.Key);
                if (string.Equals(destFull, imageFull, StringComparison.OrdinalIgnoreCase) ||
                    destFull.StartsWith(imageFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "目的資料夾位於來源映像檔內部，請選擇映像檔以外的位置。");
                }
            }
            catch { }
        }

        foreach (var group in items.Where(i => !i.SourceIsImage && !string.IsNullOrWhiteSpace(i.VolumeRoot)).GroupBy(i => i.VolumeRoot))
        {
            if (SameVolume(destFull, group.Key))
            {
                return (false,
                    $"拒絕執行：目的資料夾位於來源磁碟區 {group.Key} 上。把復原資料寫回來源磁碟區會重新配置尚未使用的叢集，" +
                    "直接覆寫掉正要搶救的內容。請改選其他磁碟區（或外接磁碟）。");
            }
        }

        return (true, "OK");
    }

    private static bool SameVolume(string destinationPath, string volumeRoot)
    {
        var destId = TryGetVolumeIdentity(destinationPath);
        var sourceId = TryGetVolumeIdentity(volumeRoot);
        if (!string.IsNullOrEmpty(destId) && !string.IsNullOrEmpty(sourceId))
        {
            return string.Equals(destId, sourceId, StringComparison.OrdinalIgnoreCase);
        }

        static string Normalize(string p)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(p)) ?? p;
                return root.TrimEnd('\\').ToUpperInvariant();
            }
            catch { return p.TrimEnd('\\').ToUpperInvariant(); }
        }

        return string.Equals(Normalize(destinationPath), Normalize(volumeRoot), StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetVolumeIdentity(string path)
    {
        try
        {
            var dir = path;
            if (!Directory.Exists(dir)) dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            var mount = new StringBuilder(512);
            if (!GetVolumePathNameW(dir, mount, mount.Capacity)) return null;

            var name = new StringBuilder(512);
            if (!GetVolumeNameForVolumeMountPointW(mount.ToString(), name, name.Capacity)) return null;

            return name.ToString().TrimEnd('\\').ToUpperInvariant();
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNameW(string lpszFileName, StringBuilder lpszVolumePathName, int cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, int cchBufferLength);

    public Task<RecoverySummary> RecoverAsync(
        IReadOnlyList<RecoveredFileItem> items,
        string? destinationRoot,
        bool preserveOriginalPaths,
        IProgress<RecoveryItemOutcome>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Recover(items, destinationRoot ?? "", preserveOriginalPaths, progress, log, cancellationToken), cancellationToken);
    }

    private RecoverySummary Recover(
        IReadOnlyList<RecoveredFileItem> items,
        string destinationRoot,
        bool preserveOriginalPaths,
        IProgress<RecoveryItemOutcome>? progress,
        Action<string>? log,
        CancellationToken ct)
    {
        var summary = new RecoverySummary { DestinationRoot = destinationRoot, Requested = items?.Count ?? 0 };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Info(string message) { try { log?.Invoke(message); } catch { } }

        if (items == null || items.Count == 0)
        {
            summary.RefusalReason = "沒有選取任何項目。";
            summary.Refused = true;
            return summary;
        }

        var guard = CheckDestination(destinationRoot, items);
        if (!guard.Ok)
        {
            summary.Refused = true;
            summary.RefusalReason = guard.Message;
            Info($"[REFUSED] {guard.Message}");
            return summary;
        }

        var destRoot = Path.GetFullPath(destinationRoot.Trim().Trim('"'));
        try { Directory.CreateDirectory(destRoot); }
        catch (Exception ex)
        {
            summary.Refused = true;
            summary.RefusalReason = $"無法建立目的資料夾：{ex.Message}";
            return summary;
        }

        Info($"[RECOVER] 目的資料夾：{destRoot}（保留原始路徑：{(preserveOriginalPaths ? "是" : "否")}）");

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, RawBlockSource?>(StringComparer.OrdinalIgnoreCase);
        var sourceClusters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                var outcome = new RecoveryItemOutcome { Item = item };

                if (!item.IsRecoverable)
                {
                    outcome.Skipped = true;
                    outcome.Message = $"略過（{item.ConfidenceDetail}）";
                    summary.Skipped++;
                    item.StatusText = "略過：僅中繼資料";
                    Info($"  [SKIP] {item.Name} — {item.ConfidenceDetail}");
                    summary.Outcomes.Add(outcome);
                    progress?.Report(outcome);
                    continue;
                }

                string destination = BuildDestinationPath(item, destRoot, preserveOriginalPaths, used);
                outcome.DestinationPath = destination;

                try
                {
                    long written = 0, zeroFilled = 0;
                    switch (item.SourceKind)
                    {
                        case RecoverySourceKind.DirectFileCopy:
                            if (!File.Exists(item.SourcePath)) throw new FileNotFoundException("來源檔案已不存在", item.SourcePath);
                            File.Copy(item.SourcePath, destination, overwrite: false);
                            written = new FileInfo(destination).Length;
                            break;

                        case RecoverySourceKind.MftResident:
                            File.WriteAllBytes(destination, item.InlineData ?? Array.Empty<byte>());
                            written = item.InlineData?.Length ?? 0;
                            break;

                        case RecoverySourceKind.MftClusters:
                        {
                            var src = GetSource(item, sources, sourceClusters, out long clusterSize);
                            if (src == null) throw new IOException("無法再次開啟來源（可能已失去存取權）。");
                            (written, zeroFilled) = WriteRuns(src, item, clusterSize, destination, ct);
                            break;
                        }

                        case RecoverySourceKind.CarvedSignature:
                        {
                            var src = GetSource(item, sources, sourceClusters, out _);
                            if (src == null) throw new IOException("無法再次開啟來源（可能已失去存取權）。");
                            (written, zeroFilled) = WriteRawRange(src, item.RawOffset, item.RawLength, destination, ct);
                            break;
                        }

                        case RecoverySourceKind.DirectoryEntry:
                            Directory.CreateDirectory(destination);
                            written = 0;
                            break;
                    }

                    outcome.Success = true;
                    outcome.BytesWritten = written;
                    outcome.ZeroFilled = zeroFilled;
                    outcome.Message = zeroFilled > 0
                        ? $"成功寫入 {RecoveryVolumeInfo.FormatBytes(written)}（其中 {RecoveryVolumeInfo.FormatBytes(zeroFilled)} 因叢集資訊缺失補零）"
                        : $"成功寫入 {RecoveryVolumeInfo.FormatBytes(written)}";
                    summary.Succeeded++;
                    summary.BytesWritten += written;
                    summary.ZeroFilled += zeroFilled;
                    item.StatusText = "✅ 已復原";
                    item.RecoveredTo = destination;
                    Info($"  [OK] {item.Name} → {destination}");
                }
                catch (OperationCanceledException)
                {
                    TryDeletePartial(outcome.DestinationPath);
                    throw;
                }
                catch (Exception ex)
                {
                    TryDeletePartial(outcome.DestinationPath);
                    outcome.Success = false;
                    outcome.Message = ex.Message;
                    summary.Failed++;
                    item.StatusText = $"❌ 失敗：{ex.Message}";
                    Info($"  [FAIL] {item.Name} — {ex.Message}");
                }

                summary.Outcomes.Add(outcome);
                progress?.Report(outcome);
            }
        }
        catch (OperationCanceledException)
        {
            Info("[CANCELLED] 使用者已取消復原。");
        }
        finally
        {
            foreach (var kv in sources.Values) { try { kv?.Dispose(); } catch { } }
        }

        watch.Stop();
        summary.Elapsed = watch.Elapsed;
        Info($"[SUMMARY] {summary.Headline}");
        return summary;
    }

    private static RawBlockSource? GetSource(
        RecoveredFileItem item,
        Dictionary<string, RawBlockSource?> sources,
        Dictionary<string, long> clusters,
        out long clusterSize)
    {
        clusterSize = item.SourceClusterSize > 0 ? item.SourceClusterSize : 4096;
        var identity = item.SourcePath;
        if (string.IsNullOrEmpty(identity)) return null;
        if (sources.TryGetValue(identity, out var existing))
        {
            if (clusters.TryGetValue(identity, out var size) && size > 0) clusterSize = size;
            return existing;
        }

        RawBlockSource? src = item.SourceIsImage
            ? RawBlockSource.TryOpenImage(identity, out _)
            : RawBlockSource.TryOpenVolume(item.VolumeRoot, out _);

        if (src != null)
        {
            var size = src.ReadNtfsClusterSize();
            if (size > 0) { clusterSize = size; clusters[identity] = size; }
        }

        sources[identity] = src;
        return src;
    }

    private static (long Written, long ZeroFilled) WriteRuns(RawBlockSource src, RecoveredFileItem item, long clusterSize, string destination, CancellationToken ct)
    {
        long remaining = item.SizeBytes > 0 ? item.SizeBytes : item.DataRuns.Sum(r => r.ClusterCount * clusterSize);
        long written = 0, zero = 0;
        var buffer = new byte[4 * 1024 * 1024];
        using var fs = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);

        foreach (var run in item.DataRuns)
        {
            if (remaining <= 0) break;
            ct.ThrowIfCancellationRequested();
            long runBytes = run.ClusterCount * clusterSize;
            long toWrite = Math.Min(runBytes, remaining);

            if (run.IsSparse)
            {
                WriteZeros(fs, toWrite);
                zero += toWrite;
            }
            else
            {
                long offset = 0;
                while (offset < toWrite)
                {
                    ct.ThrowIfCancellationRequested();
                    int chunk = (int)Math.Min(buffer.Length, toWrite - offset);
                    src.Read(run.Lcn * clusterSize + offset, buffer.AsSpan(0, chunk));
                    fs.Write(buffer, 0, chunk);
                    offset += chunk;
                }
            }

            written += toWrite;
            remaining -= toWrite;
        }

        if (remaining > 0)
        {
            WriteZeros(fs, remaining);
            zero += remaining;
            written += remaining;
        }

        return (written, zero);
    }

    private static (long Written, long ZeroFilled) WriteRawRange(RawBlockSource src, long offset, long length, string destination, CancellationToken ct)
    {
        long written = 0;
        var buffer = new byte[4 * 1024 * 1024];
        using var fs = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
        while (written < length)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(buffer.Length, length - written);
            if (!src.TryRead(offset + written, buffer.AsSpan(0, chunk)))
            {
                // Partial tail: write what is readable, zero the rest and report honestly.
                fs.Write(buffer, 0, chunk);
                written += chunk;
                return (written, chunk);
            }
            fs.Write(buffer, 0, chunk);
            written += chunk;
        }
        return (written, 0);
    }

    private static void WriteZeros(FileStream fs, long count)
    {
        var zeros = new byte[Math.Min(count, 1 << 20)];
        while (count > 0)
        {
            int chunk = (int)Math.Min(zeros.Length, count);
            fs.Write(zeros, 0, chunk);
            count -= chunk;
        }
    }

    private static void TryDeletePartial(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string BuildDestinationPath(RecoveredFileItem item, string destRoot, bool preservePaths, HashSet<string> used)
    {
        var name = SanitizeName(string.IsNullOrWhiteSpace(item.Name) ? (item.IsDirectory ? "recovered_folder" : "recovered_file") : item.Name);
        string relative;

        if (preservePaths && !string.IsNullOrWhiteSpace(item.OriginalPath))
        {
            relative = SanitizeRelativePath(item.OriginalPath, name, item.IsDirectory);
        }
        else
        {
            relative = name;
        }

        var full = Path.GetFullPath(Path.Combine(destRoot, relative));
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        if (File.Exists(full) || Directory.Exists(full) || used.Contains(full))
        {
            var dir = Path.GetDirectoryName(full) ?? destRoot;
            var baseName = Path.GetFileNameWithoutExtension(full);
            var ext = Path.GetExtension(full);
            for (int i = 2; i < 10_000; i++)
            {
                var candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate) && !used.Contains(candidate))
                {
                    full = candidate;
                    break;
                }
            }
        }

        used.Add(full);
        return full;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }
        var clean = sb.ToString().Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) clean = "recovered_file";
        return clean.Length > 150 ? clean[..150] : clean;
    }

    private static string SanitizeRelativePath(string originalPath, string fallbackName, bool isDirectory)
    {
        try
        {
            var trimmed = originalPath.Replace('/', '\\').Trim();
            var root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root)) trimmed = trimmed[root.Length..];

            var segments = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s != "." && s != "..")
                .Select(SanitizeName)
                .Take(16)
                .ToList();

            if (segments.Count == 0) return fallbackName;

            var last = segments[^1];
            if (string.Equals(last, fallbackName, StringComparison.OrdinalIgnoreCase) || isDirectory)
            {
                return string.Join("\\", segments);
            }
            segments.Add(fallbackName);
            return string.Join("\\", segments);
        }
        catch
        {
            return fallbackName;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  NTFS low-level helpers
    // ══════════════════════════════════════════════════════════════════════

    private readonly struct NtfsAttr
    {
        public uint Type { get; init; }
        public int Offset { get; init; }
        public int Length { get; init; }
        public bool NonResident { get; init; }
        public int NameLength { get; init; }
        public bool IsNamed() => NameLength > 0;
    }

    private sealed class FileNameInfo
    {
        public uint ParentRecord;
        public string Name = "";
        public byte Namespace;
        public long RealSize;
        public long AllocatedSize;
        public DateTime? Created;
        public DateTime? Modified;
    }

    private static ushort U16(ReadOnlySpan<byte> b, int o) => o + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b[o..]) : (ushort)0;
    private static uint U32(ReadOnlySpan<byte> b, int o) => o + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b[o..]) : 0;
    private static long I64(ReadOnlySpan<byte> b, int o) => o + 8 <= b.Length ? BinaryPrimitives.ReadInt64LittleEndian(b[o..]) : 0;

    private static byte[]? ReadMftRecord(RawBlockSource src, long offset, int recordSize, int bytesPerSector)
    {
        var buffer = new byte[recordSize];
        if (!src.TryRead(offset, buffer)) return null;
        if (U32(buffer, 0) != 0x454C4946) return null;
        return ApplyFixup(buffer, bytesPerSector) ? buffer : null;
    }

    private static byte[]? ReadMftRecordFromRuns(
        RawBlockSource src,
        List<RecoveryDataRun> mftRuns,
        int recordSize,
        long clusterSize,
        long recordNumber,
        int bytesPerSector)
    {
        long byteIndex = recordNumber * recordSize;
        long clusterIndex = byteIndex / clusterSize;
        long within = byteIndex % clusterSize;
        long cursor = 0;

        foreach (var run in mftRuns)
        {
            if (clusterIndex >= cursor && clusterIndex < cursor + run.ClusterCount)
            {
                if (run.IsSparse) return null;
                long offset = (run.Lcn + (clusterIndex - cursor)) * clusterSize + within;
                return ReadMftRecord(src, offset, recordSize, bytesPerSector);
            }
            cursor += run.ClusterCount;
        }
        return null;
    }

    /// <summary>Applies the NTFS update sequence array (fixup). Returns false for torn/stale records.</summary>
    private static bool ApplyFixup(Span<byte> record, int bytesPerSector)
    {
        if (record.Length < 0x30) return false;
        int usaOffset = U16(record, 0x04);
        int usaCount = U16(record, 0x06);
        if (usaOffset <= 0 || usaCount < 2 || usaOffset + usaCount * 2 > record.Length) return false;

        ushort usn = U16(record, usaOffset);
        int sectors = usaCount - 1;
        for (int i = 1; i <= sectors; i++)
        {
            int pos = i * bytesPerSector - 2;
            if (pos + 2 > record.Length) break;
            if (U16(record, pos) != usn) return false;
            ushort original = U16(record, usaOffset + i * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(record[pos..], original);
        }
        return true;
    }

    private static List<NtfsAttr> ParseAttributes(ReadOnlySpan<byte> record)
    {
        var list = new List<NtfsAttr>();
        if (record.Length < 0x18) return list;
        int offset = U16(record, 0x14);
        int guard = 0;
        while (offset + 8 <= record.Length && guard++ < 512)
        {
            uint type = U32(record, offset);
            if (type == 0xFFFFFFFF) break;
            uint length = U32(record, offset + 4);
            if (length < 16 || offset + length > record.Length) break;
            list.Add(new NtfsAttr
            {
                Type = type,
                Offset = offset,
                Length = (int)length,
                NonResident = record[offset + 8] != 0,
                NameLength = record[offset + 9]
            });
            offset += (int)length;
        }
        return list;
    }

    private static bool TryGetNonResidentInfo(ReadOnlySpan<byte> record, NtfsAttr attr, out long realSize, out long allocatedSize, out int runsOffset)
    {
        realSize = 0;
        allocatedSize = 0;
        runsOffset = 0;
        int o = attr.Offset;
        if (o + 0x40 > record.Length) return false;
        runsOffset = U16(record, o + 0x20);
        allocatedSize = I64(record, o + 0x28);
        realSize = I64(record, o + 0x30);
        return runsOffset > 0 && o + runsOffset < o + attr.Length;
    }

    private static List<RecoveryDataRun> ParseMappingPairs(ReadOnlySpan<byte> data)
    {
        var runs = new List<RecoveryDataRun>();
        int p = 0;
        long lcn = 0;

        while (p < data.Length && runs.Count < 1_000_000)
        {
            byte header = data[p++];
            if (header == 0) break;

            int lengthSize = header & 0x0F;
            int offsetSize = (header >> 4) & 0x0F;
            if (lengthSize is 0 or > 8 || offsetSize > 8) break;
            if (p + lengthSize + offsetSize > data.Length) break;

            long length = 0;
            for (int i = 0; i < lengthSize; i++) length |= (long)data[p + i] << (8 * i);
            p += lengthSize;

            long delta = 0;
            for (int i = 0; i < offsetSize; i++) delta |= (long)data[p + i] << (8 * i);
            p += offsetSize;

            if (offsetSize == 0)
            {
                runs.Add(new RecoveryDataRun { Lcn = -1, ClusterCount = length });
            }
            else
            {
                int shift = 64 - 8 * offsetSize;
                delta = (delta << shift) >> shift;
                lcn += delta;
                runs.Add(new RecoveryDataRun { Lcn = lcn, ClusterCount = length });
            }
        }

        return runs;
    }

    private static FileNameInfo? ParseFileName(ReadOnlySpan<byte> record, NtfsAttr attr)
    {
        if (attr.NonResident) return null;
        int contentSize = (int)U32(record, attr.Offset + 0x10);
        int contentOffset = U16(record, attr.Offset + 0x14);
        int start = attr.Offset + contentOffset;
        if (contentSize < 0x42 || start + contentSize > record.Length) return null;

        var info = new FileNameInfo
        {
            ParentRecord = (uint)(I64(record, start) & 0xFFFFFFFFFFFF),
            Created = FromFileTime(I64(record, start + 0x08)),
            Modified = FromFileTime(I64(record, start + 0x10)),
            AllocatedSize = I64(record, start + 0x28),
            RealSize = I64(record, start + 0x30)
        };

        int nameLength = record[start + 0x40];
        info.Namespace = record[start + 0x41];
        if (nameLength <= 0 || start + 0x42 + nameLength * 2 > record.Length) return null;
        info.Name = Encoding.Unicode.GetString(record.Slice(start + 0x42, nameLength * 2));

        // The $FILE_NAME header stores allocated/real size in the field order:
        // created(8) modified(8) mftModified(8) accessed(8) allocated(8) real(8)
        // — offsets above follow that layout.
        return info;
    }

    private static DateTime? FromFileTime(long value)
    {
        try
        {
            if (value <= 0) return null;
            var dt = DateTime.FromFileTimeUtc(value);
            if (dt.Year < 1980 || dt.Year > 2200) return null;
            return dt.ToLocalTime();
        }
        catch { return null; }
    }

    private static byte[]? GetResidentData(ReadOnlySpan<byte> record, NtfsAttr attr)
    {
        if (attr.NonResident) return null;
        int contentSize = (int)U32(record, attr.Offset + 0x10);
        int contentOffset = U16(record, attr.Offset + 0x14);
        if (contentSize <= 0 || contentSize > 1 << 20) return null;
        int start = attr.Offset + contentOffset;
        if (start + contentSize > record.Length) return null;
        return record.Slice(start, contentSize).ToArray();
    }

    private static bool FirstClusterLooksZeroed(RawBlockSource src, List<RecoveryDataRun> runs, long clusterSize)
    {
        try
        {
            var first = runs.FirstOrDefault(r => !r.IsSparse);
            if (first == null) return false;
            var probe = new byte[Math.Min(512, clusterSize)];
            if (!src.TryRead(first.Lcn * clusterSize, probe)) return false;
            foreach (var b in probe)
            {
                if (b != 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Copies a byte range that lives in VCN order inside a run list into one contiguous buffer.</summary>
    private static bool TryReadFromRuns(RawBlockSource src, List<RecoveryDataRun> runs, long clusterSize, long byteOffset, byte[] destination, int count)
    {
        long vcn = 0;
        long remaining = count;
        int written = 0;
        long cursor = byteOffset;

        foreach (var run in runs)
        {
            if (remaining <= 0) break;
            long runStartByte = vcn * clusterSize;
            long runBytes = run.ClusterCount * clusterSize;
            long runEndByte = runStartByte + runBytes;
            long vcnEnd = vcn + run.ClusterCount;

            if (cursor < runEndByte && cursor + remaining > runStartByte)
            {
                long readStart = Math.Max(cursor, runStartByte);
                long readEnd = Math.Min(cursor + remaining, runEndByte);
                int len = (int)(readEnd - readStart);

                if (run.IsSparse)
                {
                    Array.Clear(destination, written, len);
                }
                else
                {
                    long deviceOffset = run.Lcn * clusterSize + (readStart - runStartByte);
                    if (!src.TryRead(deviceOffset, destination.AsSpan(written, len))) return false;
                }

                written += len;
                remaining -= len;
                cursor = readEnd;
            }

            vcn = vcnEnd;
        }

        if (remaining > 0)
        {
            Array.Clear(destination, written, (int)remaining);
            return false;
        }
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Raw source (live volume handle or image file)
    // ══════════════════════════════════════════════════════════════════════

    private sealed class RawBlockSource : IDisposable
    {
        private readonly SafeFileHandle _handle;
        public string Identity { get; }
        public bool IsImage { get; }
        public long Length { get; }

        private RawBlockSource(SafeFileHandle handle, string identity, bool isImage, long length)
        {
            _handle = handle;
            Identity = identity;
            IsImage = isImage;
            Length = length;
        }

        public static RawBlockSource? TryOpenVolume(string volumeRoot, out string error)
        {
            error = "";
            try
            {
                var device = ToDevicePath(volumeRoot);
                var handle = CreateFileW(device, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int code = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    error = code == 5
                        ? $"存取被拒（Win32 5）：原始磁碟區 {device} 需要以系統管理員身分執行。"
                        : $"無法開啟 {device}（Win32 {code}）。";
                    return null;
                }

                long length = TryGetFileSize(handle);
                if (length <= 0)
                {
                    try { length = new DriveInfo(Path.GetPathRoot(volumeRoot)!).TotalSize; } catch { }
                }
                return new RawBlockSource(handle, volumeRoot, false, length);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static RawBlockSource? TryOpenImage(string imagePath, out string error)
        {
            error = "";
            try
            {
                var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.RandomAccess);
                var source = new RawBlockSource(fs.SafeFileHandle, Path.GetFullPath(imagePath), true, fs.Length);
                source._keepAlive = fs;   // keep the FileStream alive: disposing it would close the handle
                return source;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private FileStream? _keepAlive;

        private static long TryGetFileSize(SafeFileHandle handle)
        {
            try
            {
                if (!GetFileSizeEx(handle, out long size)) return 0;
                return size;
            }
            catch { return 0; }
        }

        private static string ToDevicePath(string volumeRoot)
        {
            var trimmed = volumeRoot.Trim();
            if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal) || trimmed.StartsWith(@"\\.\", StringComparison.Ordinal))
                return trimmed.TrimEnd('\\');
            if (trimmed.Length >= 2 && trimmed[1] == ':')
                return @"\\.\" + char.ToUpperInvariant(trimmed[0]) + ":";
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
                return trimmed.TrimEnd('\\');
            return @"\\.\" + trimmed.TrimEnd('\\');
        }

        /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes; throws if the source ends early.</summary>
        public void Read(long offset, Span<byte> buffer)
        {
            if (!TryRead(offset, buffer))
                throw new IOException($"來源讀取失敗（偏移 {offset:N0}，長度 {buffer.Length:N0}）。");
        }

        public bool TryRead(long offset, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read;
                try
                {
                    read = RandomAccess.Read(_handle, buffer[total..], offset + total);
                }
                catch (Exception)
                {
                    if (total > 0) buffer[total..].Clear();
                    return false;
                }
                if (read <= 0)
                {
                    if (total > 0) buffer[total..].Clear();
                    return false;
                }
                total += read;
            }
            return true;
        }

        public long ReadNtfsClusterSize()
        {
            try
            {
                var boot = new byte[512];
                if (!TryRead(0, boot)) return 0;
                if (boot[3] != 'N' || boot[4] != 'T' || boot[5] != 'F' || boot[6] != 'S') return 0;
                int bytesPerSector = U16(boot, 0x0B);
                int sectorsPerCluster = boot[0x0D];
                if (bytesPerSector <= 0 || sectorsPerCluster <= 0) return 0;
                return (long)bytesPerSector * sectorsPerCluster;
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            try { _handle.Dispose(); } catch { }
            try { _keepAlive?.Dispose(); } catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileSizeEx(SafeFileHandle hFile, out long lpFileSize);
    }

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    // ══════════════════════════════════════════════════════════════════════
    //  $Bitmap access
    // ══════════════════════════════════════════════════════════════════════

    private sealed class FreeClusterMap
    {
        private readonly RawBlockSource _src;
        private readonly List<RecoveryDataRun> _bitmapRuns;
        private readonly long _clusterSize;
        private readonly long _bitmapBytes;
        private readonly byte[] _cache = new byte[64 * 1024];
        private long _cacheStart = -1;
        private int _cacheLength;

        public FreeClusterMap(RawBlockSource src, List<RecoveryDataRun> bitmapRuns, long clusterSize, long bitmapBytes)
        {
            _src = src;
            _bitmapRuns = bitmapRuns;
            _clusterSize = clusterSize;
            _bitmapBytes = bitmapBytes;
        }

        public bool IsFree(long lcn)
        {
            try
            {
                long byteIndex = lcn >> 3;
                if (byteIndex < 0 || byteIndex >= _bitmapBytes) return false;
                if (!EnsureCached(byteIndex)) return false;
                byte value = _cache[byteIndex - _cacheStart];
                return (value & (1 << (int)(lcn & 7))) == 0;
            }
            catch { return false; }
        }

        /// <summary>Returns (clustersChecked, clustersAllocated) for the first <paramref name="maxChecks"/> non-sparse clusters.</summary>
        public (int Checked, int Allocated) CountAllocated(List<RecoveryDataRun> runs, int maxChecks)
        {
            int checkedClusters = 0, allocated = 0;
            try
            {
                foreach (var run in runs)
                {
                    if (run.IsSparse) continue;
                    for (long i = 0; i < run.ClusterCount && checkedClusters < maxChecks; i++)
                    {
                        // Walk from the end of the run backwards: the head of a file is the most likely to be reused.
                        long lcn = run.Lcn + i;
                        checkedClusters++;
                        if (!IsFree(lcn)) allocated++;
                    }
                    if (checkedClusters >= maxChecks) break;
                }
            }
            catch { }
            return (checkedClusters, allocated);
        }

        private bool EnsureCached(long byteIndex)
        {
            if (_cacheStart >= 0 && byteIndex >= _cacheStart && byteIndex < _cacheStart + _cacheLength) return true;

            long start = byteIndex - (byteIndex % _cache.Length);
            int length = (int)Math.Min(_cache.Length, _bitmapBytes - start);
            if (length <= 0) return false;

            var span = _cache.AsSpan(0, length);
            if (!TryReadBitmapBytes(start, span)) return false;
            _cacheStart = start;
            _cacheLength = length;
            return true;
        }

        private bool TryReadBitmapBytes(long bitmapOffset, Span<byte> destination)
        {
            long vcn = 0;
            int written = 0;
            long cursor = bitmapOffset;

            foreach (var run in _bitmapRuns)
            {
                if (written >= destination.Length) break;
                long runStart = vcn * _clusterSize;
                long runBytes = run.ClusterCount * _clusterSize;
                long runEnd = runStart + runBytes;

                if (cursor < runEnd && cursor + (destination.Length - written) > runStart)
                {
                    long readStart = Math.Max(cursor, runStart);
                    long readEnd = Math.Min(cursor + (destination.Length - written), runEnd);
                    int len = (int)(readEnd - readStart);
                    if (run.IsSparse)
                    {
                        destination.Slice(written, len).Clear();
                    }
                    else
                    {
                        long deviceOffset = run.Lcn * _clusterSize + (readStart - runStart);
                        if (!_src.TryRead(deviceOffset, destination.Slice(written, len))) return false;
                    }
                    written += len;
                    cursor = readEnd;
                }
                vcn += run.ClusterCount;
            }

            if (written < destination.Length) destination[written..].Clear();
            return written > 0;
        }
    }
}
