using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace DiskMasterWinUI.Models;

public partial class DiskInfo : ObservableObject
{
    [ObservableProperty] private int _number;
    [ObservableProperty] private string _friendlyName = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private string _sizeDisplay = "";
    [ObservableProperty] private string _freeDisplay = "";
    [ObservableProperty] private string _gptOrMbr = "";
    [ObservableProperty] private bool _isGpt;
    [ObservableProperty] private bool _isDynamic;

    public ObservableCollection<VolumeInfo> Volumes { get; } = new();
    public ObservableCollection<PartitionInfo> Partitions { get; } = new();
    public ObservableCollection<PartitionBlock> VisualBlocks { get; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName)
        ? $"Disk {Number}"
        : $"Disk {Number}: {FriendlyName}";

    public string Summary => $"{SizeDisplay} \u2014 {GptOrMbr} \u2014 {Status}";

    public void RefreshVisualBlocks(IEnumerable<PartitionInfo> partitions, IEnumerable<VolumeInfo> allVolumes)
    {
        VisualBlocks.Clear();
        var partList = partitions.ToList();
        var volList = allVolumes.ToList();

        if (partList.Count == 0)
        {
            // If raw or no partitions, show single unallocated block
            VisualBlocks.Add(new PartitionBlock
            {
                PartitionNumber = 0,
                TypeDescription = "Unallocated",
                SizeDisplay = string.IsNullOrWhiteSpace(SizeDisplay) ? "Full Disk" : SizeDisplay,
                ProportionalWeight = 10.0,
                BlockType = PartitionBlockType.Unallocated,
                IsUnallocated = true
            });
            return;
        }

        // Sort partitions by physical offset or partition number
        partList.Sort((a, b) => a.OffsetBytes > 0 && b.OffsetBytes > 0 ? a.OffsetBytes.CompareTo(b.OffsetBytes) : a.Number.CompareTo(b.Number));

        foreach (var p in partList)
        {
            var pType = p.Type.ToLowerInvariant();
            var bType = PartitionBlockType.PrimaryData;

            if (pType.Contains("system") || pType.Contains("efi") || pType.Contains("系統"))
                bType = PartitionBlockType.EfiSystem;
            else if (pType.Contains("recovery") || pType.Contains("恢復") || pType.Contains("復原"))
                bType = PartitionBlockType.Recovery;
            else if (pType.Contains("reserved") || pType.Contains("msr") || pType.Contains("保留"))
                bType = PartitionBlockType.EfiSystem;

            // Attempt to correlate with a volume strictly by drive letter
            VolumeInfo? matchedVol = null;
            var letterToMatch = p.DriveLetter;
            if (string.IsNullOrEmpty(letterToMatch))
            {
                var matchLetter = Regex.Match(p.Type, @"\(([A-Za-z]):\)");
                if (matchLetter.Success) letterToMatch = matchLetter.Groups[1].Value;
            }

            if (!string.IsNullOrEmpty(letterToMatch))
            {
                matchedVol = volList.FirstOrDefault(v => v.Letter.Equals(letterToMatch, StringComparison.OrdinalIgnoreCase));
            }

            var letter = matchedVol?.Letter ?? (string.IsNullOrEmpty(letterToMatch) ? "" : letterToMatch);
            var label = matchedVol?.Label ?? "";
            var fs = matchedVol?.FileSystem ?? "";
            var volNum = matchedVol?.Number ?? -1;

            if (!string.IsNullOrEmpty(letter) && (letter.Equals("C", StringComparison.OrdinalIgnoreCase) || (matchedVol?.Info.Contains("Boot", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                bType = PartitionBlockType.WindowsBoot;
            }

            var weight = ParseWeight(p.SizeDisplay);

            VisualBlocks.Add(new PartitionBlock
            {
                PartitionNumber = p.Number,
                VolumeNumber = volNum,
                DriveLetter = letter,
                Label = label,
                FileSystem = fs,
                TypeDescription = p.Type,
                SizeDisplay = p.SizeDisplay,
                ProportionalWeight = Math.Max(1.0, weight),
                BlockType = bType,
                IsUnallocated = false
            });
        }

        // Check if there is notable unallocated space (e.g. FreeDisplay > 0)
        if (!string.IsNullOrWhiteSpace(FreeDisplay) && !FreeDisplay.StartsWith("0 B", StringComparison.OrdinalIgnoreCase))
        {
            var freeWeight = ParseWeight(FreeDisplay);
            if (freeWeight > 0.05)
            {
                VisualBlocks.Add(new PartitionBlock
                {
                    PartitionNumber = -1,
                    TypeDescription = "Unallocated Free Space",
                    SizeDisplay = FreeDisplay,
                    ProportionalWeight = Math.Max(1.0, freeWeight),
                    BlockType = PartitionBlockType.Unallocated,
                    IsUnallocated = true
                });
            }
        }

        // Calculate proportional DisplayWidth: 16 MB -> 115px min, up to 340px for multi-TB partitions
        foreach (var block in VisualBlocks)
        {
            var logWeight = Math.Log10(Math.Max(1.0, block.ProportionalWeight));
            var width = Math.Clamp(115.0 + (logWeight / 3.4) * 225.0, 115.0, 340.0);
            block.DisplayWidth = Math.Round(width);
            block.IsResizable = !block.IsUnallocated && block.BlockType != PartitionBlockType.EfiSystem;
        }
    }

    private static double ParseWeight(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr)) return 1.0;
        var match = Regex.Match(sizeStr.Trim(), @"^([\d\.]+)\s*([A-Za-z]+)?");
        if (!match.Success) return 1.0;

        if (!double.TryParse(match.Groups[1].Value, out var val)) return 1.0;
        var unit = match.Groups[2].Value.ToUpperInvariant();

        return unit switch
        {
            "TB" => val * 1024.0,
            "GB" => val,
            "MB" => val / 1024.0,
            "KB" => val / (1024.0 * 1024.0),
            _ => val
        };
    }
}
