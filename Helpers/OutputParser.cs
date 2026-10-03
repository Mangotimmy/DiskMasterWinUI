using System.Text.RegularExpressions;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Helpers;

public static partial class OutputParser
{
    // Matches English / Traditional Chinese / Simplified Chinese disk lines
    // e.g.: "  Disk 0    Online       476 GB     0 B     *"
    //       "  磁碟 0    連線         476 GB     0 B     *"
    //       "  磁盘 0    联机         476 GB     0 B     *"
    [GeneratedRegex(@"^\s*(?:Disk|磁碟|磁盘)\s+(\d+)\s+(\S+)\s+(\d+(?:\.\d+)?\s+[A-Za-z\u4e00-\u9fa5]+)\s+(\d+(?:\.\d+)?\s+[A-Za-z\u4e00-\u9fa5]+)\s*(\*?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DiskLineRegex();

    [GeneratedRegex(@"^\s*(?:Partition|磁碟分割|分区)\s+(\d+)\s+(\S.*?)\s{2,}(\d+(?:\.\d+)?\s+[A-Za-z\u4e00-\u9fa5]+)\s+(\d+(?:\.\d+)?\s+[A-Za-z\u4e00-\u9fa5]+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PartitionLineRegex();

    public static List<DiskInfo> ParseDisks(string output)
    {
        var disks = new List<DiskInfo>();
        var matches = DiskLineRegex().Matches(output);
        foreach (Match m in matches)
        {
            var isGpt = m.Groups[5].Value.Trim() == "*";
            var disk = new DiskInfo
            {
                Number = int.Parse(m.Groups[1].Value),
                Status = m.Groups[2].Value.Trim(),
                SizeDisplay = m.Groups[3].Value.Trim(),
                FreeDisplay = m.Groups[4].Value.Trim(),
                GptOrMbr = isGpt ? "GPT" : "MBR",
                IsGpt = isGpt
            };
            disks.Add(disk);
        }

        // Fallback: line-by-line scanning if regex didn't catch due to formatting differences
        if (disks.Count == 0)
        {
            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if ((trimmed.StartsWith("Disk", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("磁碟", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("磁盘", StringComparison.OrdinalIgnoreCase)) &&
                    !trimmed.Contains("###"))
                {
                    var parts = Regex.Split(trimmed, @"\s{2,}");
                    if (parts.Length >= 4)
                    {
                        var firstParts = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (firstParts.Length >= 2 && int.TryParse(firstParts[1], out var num))
                        {
                            var hasGpt = trimmed.EndsWith("*");
                            disks.Add(new DiskInfo
                            {
                                Number = num,
                                Status = parts.Length > 1 ? parts[1].Trim() : "Online",
                                SizeDisplay = parts.Length > 2 ? parts[2].Trim() : "",
                                FreeDisplay = parts.Length > 3 ? parts[3].Trim() : "",
                                GptOrMbr = hasGpt ? "GPT" : "MBR",
                                IsGpt = hasGpt
                            });
                        }
                    }
                }
            }
        }

        return disks;
    }

    public static List<VolumeInfo> ParseVolumes(string output)
    {
        var volumes = new List<VolumeInfo>();

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if ((trimmed.StartsWith("Volume", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("磁碟區", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("卷", StringComparison.OrdinalIgnoreCase)) &&
                !trimmed.Contains("###"))
            {
                var match = Regex.Match(trimmed, @"^(?:Volume|磁碟區|卷)\s+(\d+)\s+(.*)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var volNumber = int.Parse(match.Groups[1].Value);
                    var rest = match.Groups[2].Value;

                    // Check drive letter (single letter followed by spaces or rest)
                    var letter = "";
                    var letterMatch = Regex.Match(rest, @"^([A-Za-z])\s+(.*)$");
                    if (letterMatch.Success)
                    {
                        letter = letterMatch.Groups[1].Value;
                        rest = letterMatch.Groups[2].Value;
                    }

                    var parts = Regex.Split(rest.Trim(), @"\s{2,}");
                    var label = parts.Length > 0 ? parts[0] : "";
                    var fs = parts.Length > 1 ? parts[1] : "";
                    var type = parts.Length > 2 ? parts[2] : "";
                    var size = parts.Length > 3 ? parts[3] : "";
                    var status = parts.Length > 4 ? parts[4] : "Healthy";
                    var info = parts.Length > 5 ? parts[5] : "";

                    // If parts don't align cleanly (e.g. no label), adjust
                    if (label.Equals("NTFS", StringComparison.OrdinalIgnoreCase) ||
                        label.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ||
                        label.Equals("exFAT", StringComparison.OrdinalIgnoreCase) ||
                        label.Equals("RAW", StringComparison.OrdinalIgnoreCase))
                    {
                        info = status;
                        status = size;
                        size = type;
                        type = fs;
                        fs = label;
                        label = "";
                    }

                    volumes.Add(new VolumeInfo
                    {
                        Number = volNumber,
                        Letter = letter,
                        Label = label,
                        FileSystem = fs,
                        Type = type,
                        SizeDisplay = size,
                        Status = status,
                        Info = info
                    });
                }
            }
        }

        return volumes;
    }

    public static List<PartitionInfo> ParsePartitions(string output)
    {
        var partitions = new List<PartitionInfo>();
        var matches = PartitionLineRegex().Matches(output);
        foreach (Match m in matches)
        {
            var part = new PartitionInfo
            {
                Number = int.Parse(m.Groups[1].Value),
                Type = m.Groups[2].Value.Trim(),
                SizeDisplay = m.Groups[3].Value.Trim(),
                OffsetDisplay = m.Groups[4].Value.Trim()
            };
            partitions.Add(part);
        }

        if (partitions.Count == 0)
        {
            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if ((trimmed.StartsWith("Partition", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("磁碟分割", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("分区", StringComparison.OrdinalIgnoreCase)) &&
                    !trimmed.Contains("###"))
                {
                    var match = Regex.Match(trimmed, @"^(?:Partition|磁碟分割|分区)\s+(\d+)\s+(\S.*?)\s{2,}(\d+(?:\.\d+)?\s+\S+)\s+(\d+(?:\.\d+)?\s+\S+)$", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        partitions.Add(new PartitionInfo
                        {
                            Number = int.Parse(match.Groups[1].Value),
                            Type = match.Groups[2].Value.Trim(),
                            SizeDisplay = match.Groups[3].Value.Trim(),
                            OffsetDisplay = match.Groups[4].Value.Trim()
                        });
                    }
                }
            }
        }

        return partitions;
    }

    public static List<BcdEntry> ParseBcdEdit(string output)
    {
        var entries = new List<BcdEntry>();
        var blocks = Regex.Split(output, @"(?=^[\w\s]+\r?\n-+)", RegexOptions.Multiline);

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            var idMatch = Regex.Match(block, @"identifier\s+(.+)", RegexOptions.IgnoreCase);
            if (!idMatch.Success) continue;

            var entry = new BcdEntry
            {
                Identifier = idMatch.Groups[1].Value.Trim(),
                RawText = block.Trim()
            };

            var descMatch = Regex.Match(block, @"description\s+(.+)", RegexOptions.IgnoreCase);
            if (descMatch.Success) entry.Description = descMatch.Groups[1].Value.Trim();

            var devMatch = Regex.Match(block, @"device\s+(.+)", RegexOptions.IgnoreCase);
            if (devMatch.Success) entry.Device = devMatch.Groups[1].Value.Trim();

            var pathMatch = Regex.Match(block, @"path\s+(.+)", RegexOptions.IgnoreCase);
            if (pathMatch.Success) entry.Path = pathMatch.Groups[1].Value.Trim();

            entries.Add(entry);
        }
        return entries;
    }

    public static List<WindowsFeatureItem> ParseFeatures(string output)
    {
        var list = new List<WindowsFeatureItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed.StartsWith("Feature Name", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("功能名稱", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("功能名称", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("---") ||
                trimmed.StartsWith("==="))
                continue;

            // Check table format: Name | State
            if (trimmed.Contains('|'))
            {
                var parts = trimmed.Split('|');
                if (parts.Length >= 2)
                {
                    var name = parts[0].Trim();
                    var state = parts[1].Trim();
                    if (!string.IsNullOrEmpty(name) &&
                        !name.Equals("Feature Name", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("功能名稱", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("功能名称", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("-") && !name.StartsWith("="))
                    {
                        bool isEnabled = state.Contains("Enable", StringComparison.OrdinalIgnoreCase) ||
                                         state.Contains("已啟用", StringComparison.OrdinalIgnoreCase) ||
                                         state.Contains("已启用", StringComparison.OrdinalIgnoreCase) ||
                                         state.Equals("啟用", StringComparison.OrdinalIgnoreCase) ||
                                         state.Equals("启用", StringComparison.OrdinalIgnoreCase);
                        list.Add(new WindowsFeatureItem
                        {
                            FeatureName = name,
                            State = state,
                            IsEnabled = isEnabled
                        });
                    }
                }
            }
            // Check colon format: Feature Name : xxx
            else if (trimmed.StartsWith("Feature Name", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("功能名稱", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("功能名称", StringComparison.OrdinalIgnoreCase))
            {
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0 && colonIdx + 1 < trimmed.Length)
                {
                    var name = trimmed.Substring(colonIdx + 1).Trim();
                    var stateLine = reader.ReadLine()?.Trim() ?? "";
                    while (string.IsNullOrWhiteSpace(stateLine) && (line = reader.ReadLine()) != null)
                    {
                        stateLine = line.Trim();
                    }
                    var stateColon = stateLine.IndexOf(':');
                    var state = stateColon >= 0 ? stateLine.Substring(stateColon + 1).Trim() : stateLine;
                    bool isEnabled = state.Contains("Enable", StringComparison.OrdinalIgnoreCase) ||
                                     state.Contains("已啟用", StringComparison.OrdinalIgnoreCase) ||
                                     state.Contains("已启用", StringComparison.OrdinalIgnoreCase) ||
                                     state.Equals("啟用", StringComparison.OrdinalIgnoreCase) ||
                                     state.Equals("启用", StringComparison.OrdinalIgnoreCase);
                    list.Add(new WindowsFeatureItem
                    {
                        FeatureName = name,
                        State = state,
                        IsEnabled = isEnabled
                    });
                }
            }
        }
        return list;
    }

    public static List<WindowsPackageItem> ParsePackages(string output)
    {
        var list = new List<WindowsPackageItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        // 1. Check table format with '|'
        if (output.Contains('|'))
        {
            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("---") || trimmed.StartsWith("==="))
                    continue;

                if (trimmed.Contains('|'))
                {
                    var parts = trimmed.Split('|');
                    if (parts.Length >= 2)
                    {
                        var identity = parts[0].Trim();
                        var state = parts.Length > 1 ? parts[1].Trim() : "";
                        var relType = parts.Length > 2 ? parts[2].Trim() : "";
                        var installTime = parts.Length > 3 ? parts[3].Trim() : "";

                        if (!string.IsNullOrEmpty(identity) &&
                            !identity.StartsWith("Package Identity", StringComparison.OrdinalIgnoreCase) &&
                            !identity.StartsWith("套件識別", StringComparison.OrdinalIgnoreCase) &&
                            !identity.StartsWith("套件身分", StringComparison.OrdinalIgnoreCase) &&
                            !identity.StartsWith("-") && !identity.StartsWith("="))
                        {
                            var kbMatch = Regex.Match(identity, @"(KB\d+)", RegexOptions.IgnoreCase);
                            list.Add(new WindowsPackageItem
                            {
                                PackageIdentity = identity,
                                KbArticle = kbMatch.Success ? kbMatch.Groups[1].Value.ToUpper() : "",
                                State = state,
                                ReleaseType = relType,
                                InstallTime = installTime
                            });
                        }
                    }
                }
            }

            if (list.Count > 0) return list;
        }

        // 2. Colon block format
        var blocks = Regex.Split(output, @"(?=Package Identity|套件識別碼|套件識別身分|套件身分識別|软件包标识)", RegexOptions.IgnoreCase);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var idMatch = Regex.Match(block, @"(?:Package Identity|套件識別碼|套件識別身分|套件身分識別|软件包标识)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            if (!idMatch.Success) continue;

            var identity = idMatch.Groups[1].Value.Trim();
            var stateMatch = Regex.Match(block, @"(?:State|狀態|状态)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            var typeMatch = Regex.Match(block, @"(?:Release Type|發行版本類型|版本類型|发行版本类型)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            var timeMatch = Regex.Match(block, @"(?:Install Time|安裝時間|安装时间)\s*:\s*(.+)", RegexOptions.IgnoreCase);

            var kbMatch = Regex.Match(identity, @"(KB\d+)", RegexOptions.IgnoreCase);

            list.Add(new WindowsPackageItem
            {
                PackageIdentity = identity,
                KbArticle = kbMatch.Success ? kbMatch.Groups[1].Value.ToUpper() : "",
                State = stateMatch.Success ? stateMatch.Groups[1].Value.Trim() : "",
                ReleaseType = typeMatch.Success ? typeMatch.Groups[1].Value.Trim() : "",
                InstallTime = timeMatch.Success ? timeMatch.Groups[1].Value.Trim() : ""
            });
        }
        return list;
    }

    public static List<OemDriverItem> ParseOemDrivers(string output)
    {
        var list = new List<OemDriverItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        var blocks = Regex.Split(output, @"(?=(?:Published Name|發佈名稱|发布名称|公開名)\s*:)", RegexOptions.IgnoreCase);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var pubMatch = Regex.Match(block, @"(?:Published Name|發佈名稱|发布名称|公開名)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (!pubMatch.Success) continue;

            var item = new OemDriverItem
            {
                PublishedName = pubMatch.Groups[1].Value.Trim()
            };

            var lines = block.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                var colonIdx = line.IndexOf(':');
                if (colonIdx <= 0) continue;

                var key = line.Substring(0, colonIdx).Trim();
                var val = line.Substring(colonIdx + 1).Trim();

                if (MatchesDriverKey(key, "Original Name", "原始名稱", "原始名称", "元の名前"))
                {
                    item.OriginalFileName = val;
                }
                else if (MatchesDriverKey(key, "Provider Name", "驅動程式套件提供者", "驱动程序程序包提供商",
                                         "ドライバー パッケージ プロバイダー", "ドライバーパッケージプロバイダー",
                                         "提供者名稱", "提供商名称", "提供程序名称", "プロバイダー名",
                                         "提供者", "提供商", "プロバイダー"))
                {
                    item.ProviderName = val;
                }
                else if (MatchesDriverKey(key, "Class Name", "Driver Class", "類別名稱", "类别名称",
                                         "类名称", "クラス名", "類別", "类别", "类", "クラス"))
                {
                    item.DriverClass = val;
                }
                else if (MatchesDriverKey(key, "Signer Name", "Signer", "簽署者名稱", "簽署人名稱",
                                         "签名者名称", "签名人名称", "署名者名", "簽署者", "签名者", "署名者"))
                {
                    item.SignerName = val;
                }
                else if (MatchesDriverKey(key, "Driver Date and Version", "Driver Date & Version",
                                         "驅動程式日期和版本", "驱动程序日期和版本",
                                         "ドライバーの日付とバージョン", "Date and Version", "日期和版本"))
                {
                    var parts = val.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && string.IsNullOrWhiteSpace(item.Date))
                    {
                        item.Date = parts[0].Trim();
                    }
                    if (parts.Length > 1 && string.IsNullOrWhiteSpace(item.Version))
                    {
                        item.Version = parts[1].Trim();
                    }
                }
                else if (MatchesDriverKey(key, "Driver Date", "Date", "驅動程式日期", "驱动程序日期", "ドライバーの日付", "日期"))
                {
                    if (string.IsNullOrWhiteSpace(item.Date))
                    {
                        item.Date = val;
                    }
                }
                else if (MatchesDriverKey(key, "Driver Version", "Version", "驅動程式版本", "驱动程序版本",
                                         "ドライバーのバージョン", "ドライバーバージョン", "版本"))
                {
                    if (string.IsNullOrWhiteSpace(item.Version))
                    {
                        var parts = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && (parts[0].Contains("/") || parts[0].Contains("-") || parts[0].Contains(".")))
                        {
                            if (string.IsNullOrWhiteSpace(item.Date))
                            {
                                item.Date = parts[0].Trim();
                            }
                            item.Version = string.Join(" ", parts.Skip(1)).Trim();
                        }
                        else
                        {
                            item.Version = val;
                        }
                    }
                }
            }

            // Fallback for fields if single-line or non-standard formatting was passed
            if (string.IsNullOrWhiteSpace(item.OriginalFileName))
            {
                var m = Regex.Match(block, @"(?:Original Name|原始名稱|原始名称|元の名前)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m.Success) item.OriginalFileName = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(item.ProviderName))
            {
                var m = Regex.Match(block, @"(?:Provider Name|驅動程式套件提供者|驱动程序程序包提供商|ドライバー\s*パッケージ\s*プロバイダー|提供者名稱|提供商名称|提供程序名称|プロバイダー名|提供者|提供商|プロバイダー)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m.Success) item.ProviderName = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(item.DriverClass))
            {
                var m = Regex.Match(block, @"(?:Class Name|Driver Class|類別名稱|类别名称|类名称|クラス名|類別|类别|类|クラス)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m.Success) item.DriverClass = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(item.SignerName))
            {
                var m = Regex.Match(block, @"(?:Signer Name|Signer|簽署者名稱|簽署人名稱|签名者名称|签名人名称|署名者名|簽署者|签名者|署名者)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m.Success) item.SignerName = m.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(item.Date) && string.IsNullOrWhiteSpace(item.Version))
            {
                var m = Regex.Match(block, @"(?:Driver Date and Version|Driver Date & Version|驅動程式日期和版本|驱动程序日期和版本|ドライバーの日付とバージョン|Date and Version|日期和版本)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var parts = m.Groups[1].Value.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && string.IsNullOrWhiteSpace(item.Date)) item.Date = parts[0].Trim();
                    if (parts.Length > 1 && string.IsNullOrWhiteSpace(item.Version)) item.Version = parts[1].Trim();
                }
            }

            list.Add(item);
        }
        return list;
    }

    private static bool MatchesDriverKey(string key, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.Equals(key, c, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static List<VssShadowItem> ParseVssShadows(string output)
    {
        var list = new List<VssShadowItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        var blocks = Regex.Split(output, @"(?=Shadow Copy ID|陰影複製識別碼|卷影副本 ID)", RegexOptions.IgnoreCase);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var idMatch = Regex.Match(block, @"(?:Shadow Copy ID|陰影複製識別碼|卷影副本 ID)\s*:\s*\{?([a-fA-F0-9\-]+)\}?", RegexOptions.IgnoreCase);
            if (!idMatch.Success) continue;

            var volMatch = Regex.Match(block, @"(?:Original Volume|原始磁碟區|原始卷)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            var timeMatch = Regex.Match(block, @"(?:Creation Time|建立時間|创建时间)\s*:\s*(.+)", RegexOptions.IgnoreCase);
            var attrMatch = Regex.Match(block, @"(?:Attributes|屬性|属性)\s*:\s*(.+)", RegexOptions.IgnoreCase);

            list.Add(new VssShadowItem
            {
                ShadowId = "{" + idMatch.Groups[1].Value.Trim() + "}",
                OriginalVolume = volMatch.Success ? volMatch.Groups[1].Value.Trim() : "",
                CreationTime = timeMatch.Success ? timeMatch.Groups[1].Value.Trim() : "",
                Attributes = attrMatch.Success ? attrMatch.Groups[1].Value.Trim() : ""
            });
        }
        return list;
    }
}
