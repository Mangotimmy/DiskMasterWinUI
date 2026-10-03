using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class NativeStorageService
{
    public async Task<(List<DiskInfo> Disks, List<VolumeInfo> Volumes, string LogOutput)> GetDisksAndVolumesAsync()
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var log = new StringBuilder();

            try
            {
                var result = QueryStorageWmi(log);
                if (result.Disks.Count > 0)
                {
                    sw.Stop();
                    log.AppendLine($"\n[Native Query Completed in {sw.ElapsedMilliseconds} ms]");
                    return (result.Disks, result.Volumes, log.ToString());
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"[Storage WMI Warning: {ex.Message}] Falling back to CIMV2...");
            }

            try
            {
                var result = QueryCimv2(log);
                if (result.Disks.Count > 0)
                {
                    sw.Stop();
                    log.AppendLine($"\n[CIMV2 Query Completed in {sw.ElapsedMilliseconds} ms]");
                    return (result.Disks, result.Volumes, log.ToString());
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"[CIMV2 Query Warning: {ex.Message}]");
            }

            sw.Stop();
            log.AppendLine($"\n[Query finished with 0 disks in {sw.ElapsedMilliseconds} ms]");
            return (new List<DiskInfo>(), new List<VolumeInfo>(), log.ToString());
        });
    }

    private (List<DiskInfo> Disks, List<VolumeInfo> Volumes) QueryStorageWmi(StringBuilder log)
    {
        var disks = new List<DiskInfo>();
        var volumes = new List<VolumeInfo>();

        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        scope.Connect();

        log.AppendLine("=== Storage WMI Query ===");

        // 1. Query MSFT_Disk (projected properties for 3x faster marshaling)
        using (var diskSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Number, FriendlyName, Size, AllocatedSize, PartitionStyle, IsOffline FROM MSFT_Disk")))
        using (var diskCollection = diskSearcher.Get())
        {
            foreach (ManagementObject obj in diskCollection)
            {
                var number = Convert.ToInt32(obj["Number"] ?? 0);
                var friendlyName = obj["FriendlyName"]?.ToString() ?? "";
                var size = Convert.ToInt64(obj["Size"] ?? 0L);
                var allocatedSize = Convert.ToInt64(obj["AllocatedSize"] ?? 0L);
                var partitionStyle = Convert.ToInt32(obj["PartitionStyle"] ?? 0); // 1 = MBR, 2 = GPT
                var isOffline = Convert.ToBoolean(obj["IsOffline"] ?? false);

                var freeBytes = Math.Max(0L, size - allocatedSize);
                var isGpt = partitionStyle == 2;
                var gptOrMbr = partitionStyle == 2 ? "GPT" : partitionStyle == 1 ? "MBR" : "RAW";
                var status = isOffline ? "Offline" : "Online";

                var diskInfo = new DiskInfo
                {
                    Number = number,
                    FriendlyName = friendlyName,
                    Status = status,
                    SizeBytes = size,
                    SizeDisplay = FormatBytes(size),
                    FreeDisplay = FormatBytes(freeBytes),
                    GptOrMbr = gptOrMbr,
                    IsGpt = isGpt,
                    IsDynamic = false
                };

                disks.Add(diskInfo);
                log.AppendLine($"Disk {number}: {friendlyName} | {diskInfo.SizeDisplay} | {gptOrMbr} | {status}");
            }
        }

        disks.Sort((a, b) => a.Number.CompareTo(b.Number));

        // 2. Query MSFT_Partition (projected properties)
        try
        {
            using var partSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, PartitionNumber, Size, Offset, GptType, DriveLetter FROM MSFT_Partition"));
            using var partCollection = partSearcher.Get();
            foreach (ManagementObject obj in partCollection)
            {
                var diskNumber = Convert.ToInt32(obj["DiskNumber"] ?? 0);
                var partNumber = Convert.ToInt32(obj["PartitionNumber"] ?? 0);
                var size = Convert.ToInt64(obj["Size"] ?? 0L);
                var offset = Convert.ToInt64(obj["Offset"] ?? 0L);
                var gptType = obj["GptType"]?.ToString() ?? "";
                var driveLetterObj = obj["DriveLetter"];
                var driveLetter = driveLetterObj != null && Convert.ToChar(driveLetterObj) != '\0'
                    ? Convert.ToChar(driveLetterObj).ToString()
                    : "";

                var typeDisplay = ResolveGptType(gptType);
                if (!string.IsNullOrEmpty(driveLetter))
                    typeDisplay += $" ({driveLetter}:)";

                var partInfo = new PartitionInfo
                {
                    Number = partNumber,
                    DiskNumber = diskNumber,
                    DriveLetter = driveLetter,
                    OffsetBytes = offset,
                    Type = typeDisplay,
                    SizeDisplay = FormatBytes(size),
                    OffsetDisplay = FormatBytes(offset)
                };

                var targetDisk = disks.FirstOrDefault(d => d.Number == diskNumber);
                targetDisk?.Partitions.Add(partInfo);
            }
        }
        catch (Exception ex)
        {
            log.AppendLine($"[Partition Query Warning: {ex.Message}]");
        }

        // 3. Query MSFT_Volume (projected properties)
        try
        {
            using var volSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, FileSystemLabel, FileSystem, DriveType, Size, HealthStatus FROM MSFT_Volume"));
            using var volCollection = volSearcher.Get();
            int volIndex = 0;
            foreach (ManagementObject obj in volCollection)
            {
                var driveLetterObj = obj["DriveLetter"];
                var letter = driveLetterObj != null && Convert.ToChar(driveLetterObj) != '\0'
                    ? Convert.ToChar(driveLetterObj).ToString()
                    : "";
                var label = obj["FileSystemLabel"]?.ToString() ?? "";
                var fileSystem = obj["FileSystem"]?.ToString() ?? "";
                var driveType = Convert.ToInt32(obj["DriveType"] ?? 3);
                var size = Convert.ToInt64(obj["Size"] ?? 0L);
                var healthStatus = Convert.ToInt32(obj["HealthStatus"] ?? 0);

                var status = healthStatus == 0 ? "Healthy" : healthStatus == 1 ? "Warning" : "Unhealthy";
                var type = driveType == 3 ? "Partition" : driveType == 2 ? "Removable" : "Fixed";

                var volInfo = new VolumeInfo
                {
                    Number = volIndex++,
                    Letter = letter,
                    Label = label,
                    FileSystem = fileSystem,
                    Type = type,
                    SizeDisplay = FormatBytes(size),
                    Status = status,
                    Info = !string.IsNullOrEmpty(letter) && letter.Equals("C", StringComparison.OrdinalIgnoreCase) ? "Boot" : ""
                };

                volumes.Add(volInfo);
                log.AppendLine($"  Volume {volInfo.Number}: [{letter}] {label} ({fileSystem}) - {volInfo.SizeDisplay} - {status}");
            }
        }
        catch (Exception ex)
        {
            log.AppendLine($"[Volume Query Warning: {ex.Message}]");
        }

        return (disks, volumes);
    }

    private (List<DiskInfo> Disks, List<VolumeInfo> Volumes) QueryCimv2(StringBuilder log)
    {
        var disks = new List<DiskInfo>();
        var volumes = new List<VolumeInfo>();

        log.AppendLine("=== CIMV2 Fallback Query ===");

        using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
        using (var collection = searcher.Get())
        {
            foreach (ManagementObject obj in collection)
            {
                var index = Convert.ToInt32(obj["Index"] ?? 0);
                var model = obj["Model"]?.ToString() ?? "";
                var size = Convert.ToInt64(obj["Size"] ?? 0L);
                var status = obj["Status"]?.ToString() ?? "OK";
                var partitions = Convert.ToInt32(obj["Partitions"] ?? 0);

                var disk = new DiskInfo
                {
                    Number = index,
                    FriendlyName = model,
                    Status = status == "OK" ? "Online" : status,
                    SizeBytes = size,
                    SizeDisplay = FormatBytes(size),
                    FreeDisplay = "N/A",
                    GptOrMbr = "GPT",
                    IsGpt = true,
                    IsDynamic = false
                };
                disks.Add(disk);
            }
        }

        int volNum = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var vol = new VolumeInfo
            {
                Number = volNum++,
                Letter = drive.Name.TrimEnd('\\', ':'),
                Label = drive.VolumeLabel,
                FileSystem = drive.DriveFormat,
                Type = drive.DriveType.ToString(),
                SizeDisplay = FormatBytes(drive.TotalSize),
                Status = "Healthy",
                Info = drive.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "Boot" : ""
            };
            volumes.Add(vol);
        }

        return (disks, volumes);
    }

    private static string ResolveGptType(string gptGuid)
    {
        if (string.IsNullOrWhiteSpace(gptGuid)) return "Basic Data";

        var clean = gptGuid.Trim('{', '}').ToLowerInvariant();
        return clean switch
        {
            "c12a7328-f81f-11d2-ba4b-00a0c93ec93b" => "EFI System",
            "e3c9e316-0b5c-4db8-817d-f92df00215ae" => "MSR (Reserved)",
            "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" => "Basic Data",
            "de94bba4-06d1-4d40-a16a-bfd50179d6ac" => "Recovery",
            "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3" => "Storage Spaces",
            "0fc63daf-8483-4772-8e79-3d69d8477de4" => "OEM Support",
            _ => "Partition"
        };
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024.0 && i < suffixes.Length - 1)
        {
            d /= 1024.0;
            i++;
        }
        return $"{d:0.#} {suffixes[i]}";
    }
}
