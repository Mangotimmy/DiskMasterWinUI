using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public partial class WimDeployService
{
    [GeneratedRegex(@"\[\s*={0,}\s*(\d{1,3}(?:\.\d+)?)\s*%\s*={0,}\s*\]")]
    private static partial Regex DismPercentRegex();

    private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken);
    }

    public async Task<List<WimImageInfo>> GetImageInfoAsync(string imagePath)
    {
        var list = new List<WimImageInfo>();
        if (!File.Exists(imagePath)) return list;

        var output = await RunCommandAsync("dism.exe", $"/Get-ImageInfo /ImageFile:\"{imagePath}\"");

        // Split by Index : \d+ or 索引 : \d+
        var indexBlocks = Regex.Split(output, @"(?=(?:Index|索引)\s*[:：]\s*\d+)", RegexOptions.IgnoreCase);

        foreach (var block in indexBlocks)
        {
            var idxMatch = Regex.Match(block, @"(?:Index|索引)\s*[:：]\s*(\d+)", RegexOptions.IgnoreCase);
            if (!idxMatch.Success) continue;

            var info = new WimImageInfo
            {
                Index = int.Parse(idxMatch.Groups[1].Value)
            };

            var nameMatch = Regex.Match(block, @"(?:Name|名稱|名称)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
            if (nameMatch.Success) info.Name = nameMatch.Groups[1].Value.Trim();

            var descMatch = Regex.Match(block, @"(?:Description|描述)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
            if (descMatch.Success) info.Description = descMatch.Groups[1].Value.Trim();

            var sizeMatch = Regex.Match(block, @"(?:Size|大小)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
            if (sizeMatch.Success)
            {
                info.SizeDisplay = sizeMatch.Groups[1].Value.Trim();
                ParseAndFormatSize(info, info.SizeDisplay);
            }

            var archMatch = Regex.Match(block, @"(?:Architecture|架構|架构|アーキテクチャ)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
            if (archMatch.Success) info.Architecture = NormalizeArchitecture(archMatch.Groups[1].Value.Trim());

            list.Add(info);
        }

        // Parallel detailed inspection of each index to retrieve exact architecture, version, build, and edition
        var detailTasks = list.Select(async info =>
        {
            try
            {
                var detailOutput = await RunCommandAsync("dism.exe", $"/Get-ImageInfo /ImageFile:\"{imagePath}\" /Index:{info.Index}");
                ParseDetailBlock(info, detailOutput);
            }
            catch { }

            EnrichWindowsMetadata(info);
        });

        await Task.WhenAll(detailTasks);

        return list;
    }

    private static void ParseAndFormatSize(WimImageInfo info, string rawSize)
    {
        var digits = Regex.Replace(rawSize, @"[^\d]", "");
        if (long.TryParse(digits, out var bytes) && bytes > 0)
        {
            info.SizeBytes = bytes;
            info.FormattedSize = FormatBytes(bytes);
        }
        else
        {
            info.FormattedSize = rawSize;
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L * 1024L)
            return $"{(double)bytes / (1024L * 1024L * 1024L * 1024L):F2} TB";
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB";
        if (bytes >= 1024L * 1024L)
            return $"{(double)bytes / (1024L * 1024L):F2} MB";
        if (bytes >= 1024L)
            return $"{(double)bytes / 1024L:F1} KB";
        return $"{bytes} B";
    }

    private static string NormalizeArchitecture(string rawArch)
    {
        var lower = rawArch.Trim().ToLowerInvariant();
        if (lower.Contains("arm64") || lower.Contains("aarch64") || lower == "12") return "arm64";
        if (lower.Contains("x64") || lower.Contains("amd64") || lower.Contains("64") || lower == "9") return "x64";
        if (lower.Contains("x86") || lower.Contains("32") || lower == "0") return "x86";
        if (lower.Contains("arm") || lower == "5") return "arm";
        return rawArch.Trim();
    }

    private static void ParseDetailBlock(WimImageInfo info, string detailOutput)
    {
        if (string.IsNullOrWhiteSpace(detailOutput)) return;

        var archMatch = Regex.Match(detailOutput, @"(?:Architecture|架構|架构|アーキテクチャ)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
        if (archMatch.Success && !string.IsNullOrWhiteSpace(archMatch.Groups[1].Value))
        {
            info.Architecture = NormalizeArchitecture(archMatch.Groups[1].Value);
        }

        var verMatch = Regex.Match(detailOutput, @"(?:Version|版本|バージョン)\s*[:：]\s*([0-9\.]+)", RegexOptions.IgnoreCase);
        if (verMatch.Success)
        {
            info.Version = verMatch.Groups[1].Value.Trim();
        }

        var spBuildMatch = Regex.Match(detailOutput, @"(?:ServicePack Build|ServicePack 組建|ServicePack 內部版本|ServicePack 内部版本)\s*[:：]\s*(\d+)", RegexOptions.IgnoreCase);
        if (spBuildMatch.Success && !string.IsNullOrWhiteSpace(info.Version))
        {
            var sp = spBuildMatch.Groups[1].Value.Trim();
            if (!info.Version.EndsWith("." + sp))
            {
                info.Version += "." + sp;
            }
        }

        var editionMatch = Regex.Match(detailOutput, @"(?:Edition|版本代號|エディション)\s*[:：]\s*([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (editionMatch.Success)
        {
            info.Edition = editionMatch.Groups[1].Value.Trim();
        }

        var sizeMatch = Regex.Match(detailOutput, @"(?:Size|大小)\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
        if (sizeMatch.Success && info.SizeBytes == 0)
        {
            info.SizeDisplay = sizeMatch.Groups[1].Value.Trim();
            ParseAndFormatSize(info, info.SizeDisplay);
        }
    }

    private static void EnrichWindowsMetadata(WimImageInfo info)
    {
        // 1. Architecture inference fallback
        if (string.IsNullOrWhiteSpace(info.Architecture))
        {
            var text = (info.Name + " " + info.Description).ToLowerInvariant();
            if (text.Contains("arm64") || text.Contains("aarch64")) info.Architecture = "arm64";
            else if (text.Contains("x64") || text.Contains("amd64") || text.Contains("64-bit") || text.Contains("64位元") || text.Contains("64位")) info.Architecture = "x64";
            else if (text.Contains("x86") || text.Contains("32-bit") || text.Contains("32位元") || text.Contains("32位")) info.Architecture = "x86";
            else if (text.Contains("windows 11") || text.Contains("win11")) info.Architecture = "x64";
            else if (info.SizeBytes > 15L * 1024L * 1024L * 1024L) info.Architecture = "x64";
        }

        // 2. BuildNumber extraction
        if (!string.IsNullOrWhiteSpace(info.Version))
        {
            var parts = info.Version.Split('.');
            if (parts.Length >= 3)
            {
                info.BuildNumber = parts[2];
            }
            else
            {
                info.BuildNumber = info.Version;
            }
        }
        else
        {
            var buildMatch = Regex.Match(info.Name + " " + info.Description, @"(?:Build|組建|內部版本|内部版本)\s*(\d{5})", RegexOptions.IgnoreCase);
            if (buildMatch.Success)
            {
                info.BuildNumber = buildMatch.Groups[1].Value;
            }
        }

        // 3. MarketingVersion mapping (e.g. 24H2, 23H2)
        var textCombo = info.Name + " " + info.Description;
        var releaseMatch = Regex.Match(textCombo, @"\b(2[1-5]H[12]|LTSC \d{4}|LTSB \d{4})\b", RegexOptions.IgnoreCase);
        if (releaseMatch.Success)
        {
            info.MarketingVersion = releaseMatch.Value.ToUpperInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(info.BuildNumber))
        {
            info.MarketingVersion = info.BuildNumber switch
            {
                "26100" => "24H2",
                "22631" => "23H2",
                "22621" => "22H2",
                "22000" => "21H2",
                "19045" => "22H2",
                "19044" => "21H2",
                "19043" => "21H1",
                "19042" => "20H2",
                "19041" => "2004",
                "17763" => "LTSC 2019",
                "14393" => "LTSB 2016",
                "10240" => "LTSB 2015",
                "7601" or "7600" => "SP1",
                "9600" => "8.1",
                _ => ""
            };
        }

        // Ensure FormattedSize is populated
        if (string.IsNullOrWhiteSpace(info.FormattedSize))
        {
            ParseAndFormatSize(info, info.SizeDisplay);
        }
    }

    public async Task<string> ApplyImageAsync(
        string imagePath,
        int index,
        string targetDrive,
        bool compact,
        Action<int, string>? progressCallback = null)
    {
        var sb = new StringBuilder();
        var compactArg = compact ? " /Compact" : "";
        var targetClean = targetDrive.TrimEnd('\\') + Path.DirectorySeparatorChar;
        var args = $"/Apply-Image /ImageFile:\"{imagePath}\" /Index:{index} /ApplyDir:\"{targetClean}\"{compactArg}";

        var encoding = ProcessHelper.GetConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = "dism.exe",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        using var process = Process.Start(psi);
        if (process == null) return "ERROR: Failed to launch dism.exe";

        // Parallel drain of stderr to eliminate 4KB pipe deadlock hazard
        var stderrTask = process.StandardError.ReadToEndAsync();

        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line == null) break;

            sb.AppendLine(line);
            var m = DismPercentRegex().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, out var pct))
            {
                progressCallback?.Invoke((int)pct, line.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                progressCallback?.Invoke(-1, line.Trim());
            }
        }

        await process.WaitForExitAsync();
        process.WaitForExit(200);

        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sb.AppendLine($"[STDERR]\n{stderr.Trim()}");
        }

        return sb.ToString();
    }

    public async Task<string> ApplyWin11BypassTweaksAsync(string targetDrive)
    {
        var sb = new StringBuilder();
        var targetClean = targetDrive.TrimEnd('\\') + Path.DirectorySeparatorChar;
        var sysHive = Path.Combine(targetClean, "Windows", "System32", "config", "SYSTEM");
        var swHive = Path.Combine(targetClean, "Windows", "System32", "config", "SOFTWARE");

        if (!File.Exists(sysHive) || !File.Exists(swHive))
        {
            return $"ERROR: Offline registry hives not found at {sysHive}. Did the WIM finish applying?";
        }

        // 1. SYSTEM -> LabConfig
        try
        {
            await RunCommandAsync("reg.exe", $"load HKLM\\DM_SYS \"{sysHive}\"");
            using (var setupKey = Registry.LocalMachine.OpenSubKey(@"DM_SYS\Setup", true))
            {
                if (setupKey != null)
                {
                    using var lab = setupKey.CreateSubKey("LabConfig", true);
                    lab.SetValue("BypassTPMCheck", 1, RegistryValueKind.DWord);
                    lab.SetValue("BypassSecureBootCheck", 1, RegistryValueKind.DWord);
                    lab.SetValue("BypassRAMCheck", 1, RegistryValueKind.DWord);
                    lab.SetValue("BypassCPUCheck", 1, RegistryValueKind.DWord);
                    lab.SetValue("BypassStorageCheck", 1, RegistryValueKind.DWord);
                    sb.AppendLine("✓ Injected LabConfig (BypassTPM, BypassSecureBoot, BypassRAM, BypassCPU, BypassStorage).");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"SYSTEM Hive Error: {ex.Message}");
        }
        finally
        {
            await RunCommandAsync("reg.exe", "unload HKLM\\DM_SYS");
        }

        // 2. SOFTWARE -> OOBE BypassNRO
        try
        {
            await RunCommandAsync("reg.exe", $"load HKLM\\DM_SW \"{swHive}\"");
            using (var oobeKey = Registry.LocalMachine.CreateSubKey(@"DM_SW\Microsoft\Windows\CurrentVersion\OOBE", true))
            {
                if (oobeKey != null)
                {
                    oobeKey.SetValue("BypassNRO", 1, RegistryValueKind.DWord);
                    sb.AppendLine("✓ Injected OOBE BypassNRO (Bypass Microsoft Account online requirement).");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"SOFTWARE Hive Error: {ex.Message}");
        }
        finally
        {
            await RunCommandAsync("reg.exe", "unload HKLM\\DM_SW");
        }

        return sb.ToString();
    }

    public async Task<string> AddDriversAsync(string targetDrive, string driverFolder)
    {
        var targetClean = targetDrive.TrimEnd('\\') + Path.DirectorySeparatorChar;
        var args = $"/Image:\"{targetClean}\" /Add-Driver /Driver:\"{driverFolder}\" /Recurse";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> InjectWin7UsbAndNvmeDriversAsync(string targetDrive)
    {
        var genericDriverDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "Win7Drivers");
        if (Directory.Exists(genericDriverDir))
        {
            return await AddDriversAsync(targetDrive, genericDriverDir);
        }
        return "Generic Win7 drivers directory not found at tools/Win7Drivers. (Skipped)";
    }

    public async Task<string> CreateBootFilesAsync(string targetDrive, string? bootDrive = null, string firmware = "UEFI")
    {
        var targetClean = targetDrive.TrimEnd('\\') + Path.DirectorySeparatorChar;
        var winDir = Path.Combine(targetClean, "Windows");
        var cleanBoot = bootDrive?.TrimEnd('\\', ':') ?? "";
        var bootArg = string.IsNullOrWhiteSpace(cleanBoot) ? "" : $" /s {cleanBoot}:";
        var args = $"\"{winDir}\"{bootArg} /f {firmware} /v";
        var result = await RunCommandAsync("bcdboot.exe", args);

        if (!string.IsNullOrWhiteSpace(cleanBoot) && firmware.Contains("UEFI", StringComparison.OrdinalIgnoreCase))
        {
            BootGuardHelper.EnsureWin7UefiBootFiles(cleanBoot + ":");
        }

        return result;
    }

    public async Task<(string? MountedDrive, string? WimPath, string Message)> MountIsoAndFindWimAsync(string isoPath)
    {
        if (!File.Exists(isoPath))
        {
            return (null, null, "ISO file not found.");
        }

        var psCmd = $"$img = Mount-DiskImage -ImagePath '{isoPath}' -PassThru; $vol = $img | Get-Volume; if ($vol) {{ $vol.DriveLetter }}";
        var (output, err, exitCode) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");

        var driveLetter = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(driveLetter) || driveLetter.Length != 1)
        {
            // Fallback: check all CD/DVD drives
            var allDrives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom && d.IsReady).ToList();
            foreach (var d in allDrives)
            {
                var candWim = Path.Combine(d.RootDirectory.FullName, "sources", "install.wim");
                var candEsd = Path.Combine(d.RootDirectory.FullName, "sources", "install.esd");
                if (File.Exists(candWim)) return (d.Name, candWim, $"ISO mounted at {d.Name}");
                if (File.Exists(candEsd)) return (d.Name, candEsd, $"ISO mounted at {d.Name}");
            }
            return (null, null, $"Could not determine mounted drive letter. (Output: {output} {err})");
        }

        var root = $"{driveLetter}:\\";
        var wim = Path.Combine(root, "sources", "install.wim");
        var esd = Path.Combine(root, "sources", "install.esd");

        if (File.Exists(wim)) return (root, wim, $"Successfully mounted ISO at {root} (found install.wim)");
        if (File.Exists(esd)) return (root, esd, $"Successfully mounted ISO at {root} (found install.esd)");

        return (root, null, $"ISO mounted at {root}, but sources\\install.wim was not found.");
    }

    public async Task<string> DismountIsoAsync(string isoPath)
    {
        if (string.IsNullOrWhiteSpace(isoPath)) return "No ISO path specified.";
        var psCmd = $"Dismount-DiskImage -ImagePath '{isoPath}'";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");
        return string.IsNullOrWhiteSpace(err) ? $"Dismounted {Path.GetFileName(isoPath)}" : err;
    }
    // ── Phase 2: Image Management Tools ──

    public async Task<string> CaptureImageAsync(string captureDir, string outputFile, string name, string compress = "max")
    {
        var args = $"/Capture-Image /ImageFile:\"{outputFile}\" /CaptureDir:\"{captureDir}\" /Name:\"{name}\" /Compress:{compress} /CheckIntegrity /Verify";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> ExportImageAsync(string srcFile, int srcIndex, string destFile, string compress = "max")
    {
        var args = $"/Export-Image /SourceImageFile:\"{srcFile}\" /SourceIndex:{srcIndex} /DestinationImageFile:\"{destFile}\" /Compress:{compress} /CheckIntegrity";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> SplitImageAsync(string imageFile, string swmFile, int fileSizeMB = 3800)
    {
        var args = $"/Split-Image /ImageFile:\"{imageFile}\" /SWMFile:\"{swmFile}\" /FileSize:{fileSizeMB}";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> MountWimImageAsync(string imageFile, int index, string mountDir)
    {
        Directory.CreateDirectory(mountDir);
        var args = $"/Mount-Image /ImageFile:\"{imageFile}\" /Index:{index} /MountDir:\"{mountDir}\"";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> UnmountWimImageAsync(string mountDir, bool commit)
    {
        var commitArg = commit ? "/Commit" : "/Discard";
        var args = $"/Unmount-Image /MountDir:\"{mountDir}\" {commitArg}";
        return await RunCommandAsync("dism.exe", args);
    }

    public async Task<string> CleanupMountpointsAsync()
    {
        return await RunCommandAsync("dism.exe", "/Cleanup-Mountpoints");
    }
}
