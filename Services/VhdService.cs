using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Comprehensive VHD / VHDX Virtual Hard Disk Management Service.
/// Supports creation (Dynamic/Fixed, VHD/VHDX), mount/attach (with Read-Only option),
/// detach, compaction, capacity expansion, and 1-Click Native VHD Boot deployment.
/// </summary>
public class VhdService
{
    private static async Task<string> RunDiskPartScriptAsync(string script)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dm_vhd_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, script + "\r\nexit\r\n", ProcessHelper.GetConsoleEncoding());
            var (output, err, code) = await ProcessHelper.RunProcessAsync("diskpart.exe", $"/s \"{tempFile}\"");
            return string.IsNullOrWhiteSpace(output) ? err : output;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Creates a new VHD or VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> CreateVhdAsync(
        string vhdPath,
        long sizeMB,
        bool isVhdx = true,
        bool isDynamic = true)
    {
        var ext = isVhdx ? ".vhdx" : ".vhd";
        if (!vhdPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            vhdPath += ext;
        }

        var dir = Path.GetDirectoryName(vhdPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var typeStr = isDynamic ? "expandable" : "fixed";
        var script = $"create vdisk file=\"{vhdPath}\" maximum={sizeMB} type={typeStr}";

        var output = await RunDiskPartScriptAsync(script);
        if (output.Contains("successfully created", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利建立", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功创建", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功建立 {(isVhdx ? "VHDX" : "VHD")} 虛擬磁碟：{Path.GetFileName(vhdPath)} (大小: {sizeMB:N0} MB, 類型: {(isDynamic ? "動態擴充" : "固定容量")})");
        }

        return (false, output);
    }

    /// <summary>
    /// Attaches (mounts) an existing VHD / VHDX file, with optional Read-Only protection.
    /// </summary>
    public async Task<(bool Success, string Message)> AttachVhdAsync(string vhdPath, bool readOnly = false)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var readOnlyArg = readOnly ? " readonly" : "";
        var script = $"select vdisk file=\"{vhdPath}\"\r\nattach vdisk{readOnlyArg}";

        var output = await RunDiskPartScriptAsync(script);
        if (output.Contains("successfully attached", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利附加", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功附加", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功掛載虛擬硬碟：{Path.GetFileName(vhdPath)}{(readOnly ? " [唯讀保護]" : "")}");
        }

        return (false, output);
    }

    /// <summary>
    /// Detaches (unmounts) a mounted VHD / VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> DetachVhdAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\ndetach vdisk";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully detached", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利分離", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功分离", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功安全卸載虛擬硬碟：{Path.GetFileName(vhdPath)}");
        }

        return (false, output);
    }

    /// <summary>
    /// Compacts a dynamic VHD / VHDX file to reclaim unused zero-byte space.
    /// </summary>
    public async Task<(bool Success, string Message)> CompactVhdAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully compacted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利壓縮", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功压缩", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功壓縮收縮虛擬硬碟：{Path.GetFileName(vhdPath)} (已釋放未使用區塊空間)");
        }

        return (false, output);
    }

    /// <summary>
    /// Expands the maximum capacity limit of a VHD / VHDX file.
    /// </summary>
    public async Task<(bool Success, string Message)> ExpandVhdAsync(string vhdPath, long newMaximumSizeMB)
    {
        if (!File.Exists(vhdPath))
            return (false, $"找不到指定的虛擬硬碟檔案: {vhdPath}");

        var script = $"select vdisk file=\"{vhdPath}\"\r\nexpand vdisk maximum={newMaximumSizeMB}";
        var output = await RunDiskPartScriptAsync(script);

        if (output.Contains("successfully expanded", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利擴展", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功扩展", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"已成功擴展虛擬硬碟上限容量至: {newMaximumSizeMB:N0} MB");
        }

        return (false, output);
    }

    /// <summary>
    /// Attaches, initializes (GPT/MBR), partitions, formats as NTFS, and assigns a drive letter to a VHD/VHDX.
    /// </summary>
    public async Task<(bool Success, string Message, string AssignedLetter)> InitializeAndFormatVhdAsync(
        string vhdPath,
        string label = "VHDX_DATA",
        char? driveLetter = null,
        bool asGpt = true,
        string fileSystem = "ntfs")
    {
        var style = asGpt ? "gpt" : "mbr";
        var letterCmd = driveLetter.HasValue ? $"assign letter={driveLetter.Value}" : "assign";
        var fs = string.IsNullOrWhiteSpace(fileSystem) ? "ntfs" : fileSystem.Trim().ToLowerInvariant();

        var script = $"""
select vdisk file="{vhdPath}"
attach vdisk
convert {style}
create partition primary
format fs={fs} quick label="{label}"
{letterCmd}
detail vdisk
""";

        var output = await RunDiskPartScriptAsync(script);
        var letterMatch = Regex.Match(output, @"Volume\s+\d+\s+([A-Z])\s+", RegexOptions.IgnoreCase);
        var assigned = letterMatch.Success ? letterMatch.Groups[1].Value : (driveLetter?.ToString() ?? "");

        if (output.Contains("successfully formatted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("順利格式化", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("成功格式化", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"虛擬硬碟已初始化並格式化為 {fs.ToUpperInvariant()}，磁碟代號: {assigned}:", assigned);
        }

        return (false, output, assigned);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  VHDX image mode (raw disk/volume ⇄ .vhdx)
    //  The created image holds a byte-for-byte copy of the source device,
    //  written through the raw handle of the freshly attached virtual disk.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the physical disk number of an attached VHD/VHDX (the virtual disk is exposed as \\.\PhysicalDriveN).
    /// </summary>
    public async Task<(bool Found, int DiskNumber, string Message)> GetAttachedDiskNumberAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath))
            return (false, -1, $"找不到虛擬硬碟檔案: {vhdPath}");

        var escaped = vhdPath.Replace("'", "''");
        var ps = $"$d = Get-DiskImage -ImagePath '{escaped}' | Get-Disk; if ($d) {{ [string]$d.Number }} else {{ 'NOT_ATTACHED' }}";

        // The disk needs a moment to appear in the storage stack right after "attach vdisk".
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var (outStr, errStr, _) = await ProcessHelper.RunProcessAsync(
                "powershell.exe", $"-NoProfile -NonInteractive -Command \"{ps}\"");

            var text = (outStr ?? "").Trim();
            var lastLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
            if (int.TryParse(lastLine, out var diskNumber) && diskNumber >= 0)
                return (true, diskNumber, $"映像磁碟已掛載為 \\\\.\\PhysicalDrive{diskNumber}");

            if (lastLine.Contains("NOT_ATTACHED", StringComparison.OrdinalIgnoreCase) && attempt >= 2)
                return (false, -1, "映像未掛載（Get-DiskImage 查無磁碟）。" + (string.IsNullOrWhiteSpace(errStr) ? "" : $" [{errStr.Trim()}]"));

            await Task.Delay(700);
        }

        return (false, -1, "逾時：無法取得映像的實體磁碟編號（Get-DiskImage / Get-Disk 無回應）。");
    }

    /// <summary>
    /// Creates a .vhdx image whose block content is an exact copy of a raw source device
    /// (\\.\PhysicalDriveN or \\.\X:). Creation parameters come from the real source geometry.
    /// The VHDX is created, attached, filled through raw handles, then detached so the file is a
    /// self-contained image that can be attached/mounted again at any time.
    /// </summary>
    public async Task<DiskCloneService.CloneResult> CreateImageFromDeviceAsync(
        string sourceDevicePath,
        string vhdPath,
        DiskCloneService.CloneOptions options,
        IProgress<DiskCloneService.CloneProgress>? progress = null,
        CancellationToken ct = default)
    {
        var cloneService = new DiskCloneService();
        var result = new DiskCloneService.CloneResult { SourceDevice = sourceDevicePath, TargetDevice = vhdPath };

        if (!AdminHelper.IsRunningAsAdmin())
        {
            result.Message = "❌ 拒絕執行：建立原始區塊映像需要系統管理員權限。";
            return result;
        }
        if (!options.DestructiveTargetConfirmed)
        {
            result.Message = "❌ 拒絕執行：未取得映像建立/覆寫確認。";
            return result;
        }
        if (string.IsNullOrWhiteSpace(vhdPath))
        {
            result.Message = "❌ 請指定 .vhdx 映像輸出路徑。";
            return result;
        }
        if (!Path.GetExtension(vhdPath).Equals(".vhdx", StringComparison.OrdinalIgnoreCase))
        {
            result.Message = "❌ VHDX 映像模式僅支援 .vhdx 輸出（VHD 上限 2TB 且不支援 4Kn 磁區）。";
            return result;
        }

        bool attached = false;
        try
        {
            progress?.Report(new DiskCloneService.CloneProgress { Stage = "讀取來源幾何", Percent = 0 });

            // Exact source geometry - never a guessed size.
            var sourceInfo = await cloneService.QueryDeviceInfoAsync(sourceDevicePath, ct);
            result.SourceBytes = sourceInfo.TotalBytes;
            result.SourceBytesPerSector = sourceInfo.BytesPerSector;
            result.CopyLength = sourceInfo.TotalBytes;

            var sizeMB = (long)Math.Ceiling(sourceInfo.TotalBytes / 1048576.0);
            if (sizeMB <= 0) { result.Message = "❌ 來源裝置容量無效。"; return result; }

            // 1. Create the VHDX (expandable by default so the file stays small when the source has holes/zeros).
            var create = await CreateVhdAsync(vhdPath, sizeMB, isVhdx: true, isDynamic: true);
            result.Diagnostics.Add($"[diskpart] create vdisk maximum={sizeMB} MB type=expandable → {(create.Success ? "OK" : create.Message)}");
            if (!create.Success) { result.Message = $"❌ 建立 VHDX 失敗：{create.Message}"; return result; }

            // 2. Attach it, resolve its PhysicalDriveN.
            var attach = await AttachVhdAsync(vhdPath, readOnly: false);
            result.Diagnostics.Add($"[diskpart] attach vdisk → {(attach.Success ? "OK" : attach.Message)}");
            if (!attach.Success) { result.Message = $"❌ 掛載 VHDX 失敗：{attach.Message}"; return result; }
            attached = true;

            var (found, diskNumber, numberMsg) = await GetAttachedDiskNumberAsync(vhdPath);
            result.Diagnostics.Add(numberMsg);
            if (!found) { result.Message = $"❌ 無法取得 VHDX 的磁碟編號：{numberMsg}"; return result; }

            // 3. Raw copy source → the virtual disk. A fresh VHDX has no volumes, so it must not be offlined.
            var imageOptions = new DiskCloneService.CloneOptions
            {
                DestructiveTargetConfirmed = true,
                VerifyAfterWrite = options.VerifyAfterWrite,
                OfflineTargetDiskFirst = false,
                BringTargetOnlineAfterwards = false,
                ChunkSizeBytes = options.ChunkSizeBytes
            };

            var copy = await cloneService.CloneDeviceToDeviceAsync(
                sourceDevicePath, DiskCloneService.GetPhysicalDrivePath(diskNumber), imageOptions,
                progress, ct);

            result.CopiedBytes = copy.CopiedBytes;
            result.Cancelled = copy.Cancelled;
            result.SourceBytesPerSector = copy.SourceBytesPerSector;
            result.TargetBytesPerSector = copy.TargetBytesPerSector;
            result.CopyLength = copy.CopyLength;
            result.TargetBytes = copy.TargetBytes;
            result.Elapsed = copy.Elapsed;
            result.AverageBytesPerSecond = copy.AverageBytesPerSecond;
            result.VerificationRequested = copy.VerificationRequested;
            result.VerificationPassed = copy.VerificationPassed;
            result.VerificationSummary = copy.VerificationSummary;
            result.Diagnostics.AddRange(copy.Diagnostics);

            // 4. Detach so the image file is closed cleanly.
            var detach = await DetachVhdAsync(vhdPath);
            attached = false;
            result.Diagnostics.Add($"[diskpart] detach vdisk → {(detach.Success ? "OK" : detach.Message)}");

            if (!copy.Success)
            {
                result.Message = copy.Cancelled
                    ? $"⏹️ 映像建立已取消：{vhdPath}\n影像檔為「部分寫入」狀態（已寫入 {NativeStorageService.FormatBytes(result.CopiedBytes)}），請刪除後重新建立。\n{copy.Message}"
                    : $"❌ 映像建立失敗：{copy.Message}\n影像檔可能不完整：{vhdPath}";
                return result;
            }

            result.Success = true;
            result.TargetDevice = vhdPath;
            result.Message =
                $"✅ VHDX 映像建立完成：{Path.GetFileName(vhdPath)}\n" +
                $"來源 {sourceDevicePath}（{NativeStorageService.FormatBytes(sourceInfo.TotalBytes)}，邏輯磁區 {sourceInfo.BytesPerSector} B）\n" +
                $"已寫入 {NativeStorageService.FormatBytes(result.CopiedBytes)}，耗時 {result.Elapsed:hh\\:mm\\:ss}，平均 {result.AverageBytesPerSecond / 1024 / 1024:0.0} MB/s\n" +
                (result.VerificationRequested ? (result.VerificationPassed ? $"🔎 {result.VerificationSummary}\n" : $"❌ {result.VerificationSummary}\n") : "") +
                "ℹ️ 整顆磁碟映像可直接掛載使用；單一磁碟區映像為原始磁區內容，主要用於還原。";
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            result.Message = $"⏹️ 已取消：映像 {vhdPath} 為部分寫入狀態，請刪除後重新建立。";
            return result;
        }
        catch (Exception ex)
        {
            result.Message = $"❌ 建立 VHDX 映像時發生例外：{ex.Message}";
            result.Diagnostics.Add($"[EXCEPTION] {ex.GetType().Name}: {ex.Message}");
            return result;
        }
        finally
        {
            if (attached)
            {
                try { await DetachVhdAsync(vhdPath); }
                catch (Exception ex) { result.Diagnostics.Add($"[WARN] 清理時 detach vdisk 失敗: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Restores a .vhdx image (whole disk image or single volume image) back onto a raw target device
    /// with the full DiskCloneService safety gates. The image is attached read-only for the transfer.
    /// </summary>
    public async Task<DiskCloneService.CloneResult> RestoreImageToDeviceAsync(
        string vhdPath,
        string targetDevicePath,
        DiskCloneService.CloneOptions options,
        IProgress<DiskCloneService.CloneProgress>? progress = null,
        CancellationToken ct = default)
    {
        var cloneService = new DiskCloneService();
        var result = new DiskCloneService.CloneResult { SourceDevice = vhdPath, TargetDevice = targetDevicePath };

        if (!AdminHelper.IsRunningAsAdmin())
        {
            result.Message = "❌ 拒絕執行：從映像還原需要系統管理員權限。";
            return result;
        }
        if (!options.DestructiveTargetConfirmed)
        {
            result.Message = "❌ 拒絕執行：未取得目標裝置的破壞性覆寫確認。";
            return result;
        }
        if (!File.Exists(vhdPath))
        {
            result.Message = $"❌ 找不到映像檔：{vhdPath}";
            return result;
        }

        bool attached = false;
        try
        {
            var attach = await AttachVhdAsync(vhdPath, readOnly: true);
            result.Diagnostics.Add($"[diskpart] attach vdisk readonly → {(attach.Success ? "OK" : attach.Message)}");
            if (!attach.Success) { result.Message = $"❌ 無法以唯讀模式掛載映像：{attach.Message}"; return result; }
            attached = true;

            var (found, diskNumber, numberMsg) = await GetAttachedDiskNumberAsync(vhdPath);
            result.Diagnostics.Add(numberMsg);
            if (!found) { result.Message = $"❌ 無法取得映像的磁碟編號：{numberMsg}"; return result; }

            var copy = await cloneService.CloneDeviceToDeviceAsync(
                DiskCloneService.GetPhysicalDrivePath(diskNumber), targetDevicePath, options, progress, ct);

            result.CopiedBytes = copy.CopiedBytes;
            result.Cancelled = copy.Cancelled;
            result.Success = copy.Success;
            result.SourceBytes = copy.SourceBytes;
            result.TargetBytes = copy.TargetBytes;
            result.SourceBytesPerSector = copy.SourceBytesPerSector;
            result.TargetBytesPerSector = copy.TargetBytesPerSector;
            result.CopyLength = copy.CopyLength;
            result.UntouchedTailBytes = copy.UntouchedTailBytes;
            result.Elapsed = copy.Elapsed;
            result.AverageBytesPerSecond = copy.AverageBytesPerSecond;
            result.VerificationRequested = copy.VerificationRequested;
            result.VerificationPassed = copy.VerificationPassed;
            result.VerificationSummary = copy.VerificationSummary;
            result.Diagnostics.AddRange(copy.Diagnostics);
            result.Message = copy.Success
                ? $"✅ 映像還原完成：{Path.GetFileName(vhdPath)} → {targetDevicePath}\n{copy.Message}"
                : copy.Message;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            result.Message = "⏹️ 映像還原已取消（目標可能為部分寫入狀態）。";
            return result;
        }
        catch (Exception ex)
        {
            result.Message = $"❌ 從映像還原時發生例外：{ex.Message}";
            result.Diagnostics.Add($"[EXCEPTION] {ex.GetType().Name}: {ex.Message}");
            return result;
        }
        finally
        {
            if (attached)
            {
                try { await DetachVhdAsync(vhdPath); }
                catch (Exception ex) { result.Diagnostics.Add($"[WARN] 清理時 detach vdisk 失敗: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Queries detailed geometry and status of a VHD/VHDX file.
    /// </summary>
    public async Task<string> GetVhdDetailsAsync(string vhdPath)
    {
        if (!File.Exists(vhdPath)) return "檔案不存在";
        var script = $"select vdisk file=\"{vhdPath}\"\r\ndetail vdisk";
        return await RunDiskPartScriptAsync(script);
    }

    /// <summary>
    /// Deploys a Windows WIM image to a VHD/VHDX file and configures native VHD boot in Windows BCD.
    /// </summary>
    public async Task<(bool Success, string Message)> DeployWimToNativeVhdBootAsync(
        string wimPath,
        int wimIndex,
        string vhdPath,
        long sizeMB,
        string bootDescription,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        try
        {
            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 1/5: 正在建立高相容動態 VHDX ({sizeMB:N0} MB)...");
            var createRes = await CreateVhdAsync(vhdPath, sizeMB, isVhdx: true, isDynamic: true);
            if (!createRes.Success) return (false, $"建立 VHDX 失敗: {createRes.Message}");

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 2/5: 正在初始化磁區並掛載為 NTFS...");
            var initRes = await InitializeAndFormatVhdAsync(vhdPath, "VHD_WIN", asGpt: true);
            if (!initRes.Success) return (false, $"初始化 VHDX 失敗: {initRes.Message}");

            var vhdDrive = string.IsNullOrEmpty(initRes.AssignedLetter) ? "V:" : $"{initRes.AssignedLetter}:";
            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 3/5: VHDX 已掛載至 {vhdDrive}，正在透過 DISM 解壓縮套用 Windows 映像 (Index {wimIndex})...");

            var dismArgs = $"/Apply-Image /ImageFile:\"{wimPath}\" /Index:{wimIndex} /ApplyDir:\"{vhdDrive}\"";
            var (dismOut, dismErr, dismCode) = await ProcessHelper.RunProcessAsync("dism.exe", dismArgs, cancellationToken: ct);
            if (dismCode != 0)
            {
                return (false, $"DISM 套用映像失敗: {(string.IsNullOrWhiteSpace(dismErr) ? dismOut : dismErr)}");
            }

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 4/5: 正在寫入主機 UEFI/BIOS BCD 原生 VHD 開機引導項...");
            var bcdArgs = $"\"{vhdDrive}\\Windows\" /d /addlast";
            var (bcdOut, bcdErr, bcdCode) = await ProcessHelper.RunProcessAsync("bcdboot.exe", bcdArgs, cancellationToken: ct);

            // Set custom description in BCD if specified
            if (!string.IsNullOrWhiteSpace(bootDescription))
            {
                try
                {
                    await ProcessHelper.RunProcessAsync("bcdedit.exe", $"/set {{default}} description \"{bootDescription}\"", cancellationToken: ct);
                }
                catch { }
            }

            onProgress($"[{DateTime.Now:HH:mm:ss}] 步驟 5/5: Native VHD Boot 部署完畢！重開機即可在 Windows 開機選單直接啟動此 VHDX 系統！");
            return (true, $"已成功部署 Windows 至 VHDX ({vhdPath})！\n免割實體硬碟分區即可暢享全新獨立 Windows 雙系統！");
        }
        catch (Exception ex)
        {
            return (false, $"例外錯誤: {ex.Message}");
        }
    }
}
