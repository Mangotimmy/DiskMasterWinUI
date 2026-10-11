using System.Diagnostics;
using System.IO;
using System.Text;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing reading, editing, backing up, and restoring the Windows Hosts file.
/// </summary>
public class HostsService
{
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "drivers", "etc", "hosts");

    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "drivers", "etc", "hosts.bak");

    /// <summary>
    /// Gets the path to the Windows Hosts file.
    /// </summary>
    public string GetHostsFilePath() => HostsPath;

    /// <summary>
    /// Reads the entire raw text of the Hosts file.
    /// </summary>
    public async Task<string> ReadRawHostsAsync()
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(HostsPath))
            {
                return "# Windows Hosts file not found.";
            }

            try
            {
                return File.ReadAllText(HostsPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return $"# Error reading Hosts file: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// Parses hosts text content into structured HostsEntryItem objects.
    /// </summary>
    public List<HostsEntryItem> ParseHosts(string rawContent)
    {
        var list = new List<HostsEntryItem>();
        if (string.IsNullOrWhiteSpace(rawContent)) return list;

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            bool isCommented = trimmed.StartsWith("#");
            string work = isCommented ? trimmed.TrimStart('#').Trim() : trimmed;

            var parts = work.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && (parts[0].Contains('.') || parts[0].Contains(':')))
            {
                string ip = parts[0];
                string host = parts[1];
                string comment = "";
                int commentIdx = work.IndexOf('#');
                if (commentIdx >= 0)
                {
                    comment = work.Substring(commentIdx).Trim();
                }

                list.Add(new HostsEntryItem
                {
                    IpAddress = ip,
                    HostName = host,
                    Comment = comment,
                    IsEnabled = !isCommented
                });
            }
        }
        return list;
    }

    /// <summary>
    /// Generates hosts file text representation from structured entries.
    /// </summary>
    public string GenerateHostsText(IEnumerable<HostsEntryItem> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            string prefix = entry.IsEnabled ? "" : "# ";
            string line = $"{prefix}{entry.IpAddress,-16} {entry.HostName}";
            if (!string.IsNullOrWhiteSpace(entry.Comment))
            {
                line += $" {entry.Comment}";
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses the Hosts file into structured HostsEntryItem objects.
    /// </summary>
    public async Task<List<HostsEntryItem>> ReadHostsAsync()
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(HostsPath)) return new List<HostsEntryItem>();
            try
            {
                var text = File.ReadAllText(HostsPath, Encoding.UTF8);
                return ParseHosts(text);
            }
            catch
            {
                return new List<HostsEntryItem>();
            }
        });
    }

    private static readonly string BackupsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DiskMaster", "HostsBackups");

    public HostsService()
    {
        try
        {
            if (!Directory.Exists(BackupsDirectory))
            {
                Directory.CreateDirectory(BackupsDirectory);
            }
        }
        catch { }
    }

    /// <summary>
    /// Saves the raw content to the Hosts file with automatic timestamped multi-version backup and DNS flush.
    /// </summary>
    public async Task<(bool Success, string Message)> SaveRawHostsAsync(string rawContent, bool createBackup = true, string backupNote = "自動儲存")
    {
        return await Task.Run(() =>
        {
            try
            {
                if (createBackup && File.Exists(HostsPath))
                {
                    try
                    {
                        // 1. Classic single backup for system compatibility
                        File.Copy(HostsPath, BackupPath, true);

                        // 2. Timestamped multi-version snapshot
                        Directory.CreateDirectory(BackupsDirectory);
                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string snapshotPath = Path.Combine(BackupsDirectory, $"hosts_{timeStamp}.bak");
                        File.Copy(HostsPath, snapshotPath, true);

                        // Prune older backups if exceeding 50 items
                        var oldBackups = Directory.GetFiles(BackupsDirectory, "hosts_*.bak")
                            .OrderByDescending(f => new FileInfo(f).CreationTime)
                            .Skip(50);
                        foreach (var old in oldBackups)
                        {
                            try { File.Delete(old); } catch { }
                        }
                    }
                    catch { }
                }

                // Clear read-only attribute if present
                if (File.Exists(HostsPath))
                {
                    var attrs = File.GetAttributes(HostsPath);
                    if (attrs.HasFlag(FileAttributes.ReadOnly))
                    {
                        File.SetAttributes(HostsPath, attrs & ~FileAttributes.ReadOnly);
                    }
                }

                File.WriteAllText(HostsPath, rawContent, new UTF8Encoding(false));

                // Flush DNS cache
                FlushDnsCache();

                return (true, "Hosts 檔案已成功儲存並同步重新整理系統 DNS 快取。");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "權限不足：寫入 Hosts 檔案需要以系統管理員身分執行。");
            }
            catch (Exception ex)
            {
                return (false, $"儲存 Hosts 檔案時發生錯誤: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Creates an explicit manual backup snapshot of the current Hosts file.
    /// </summary>
    public async Task<(bool Success, string Message)> CreateManualBackupAsync(string note = "手動備份")
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(HostsPath)) return (false, "Hosts 檔案不存在，無法建立備份。");

                Directory.CreateDirectory(BackupsDirectory);
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeNote = string.IsNullOrWhiteSpace(note) ? "手動備份" : note.Trim().Replace(" ", "_");
                string snapshotPath = Path.Combine(BackupsDirectory, $"hosts_{timeStamp}_{safeNote}.bak");
                File.Copy(HostsPath, snapshotPath, true);

                return (true, $"已成功建立備份: {Path.GetFileName(snapshotPath)}");
            }
            catch (Exception ex)
            {
                return (false, $"建立備份失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Gets all historical backup snapshots.
    /// </summary>
    public async Task<List<HostsBackupItem>> GetBackupHistoryAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<HostsBackupItem>();
            try
            {
                if (!Directory.Exists(BackupsDirectory)) return list;

                var files = Directory.GetFiles(BackupsDirectory, "*.bak")
                    .OrderByDescending(f => new FileInfo(f).CreationTimeUtc);

                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    string name = fi.Name;
                    string note = "自動備份";
                    var parts = name.Replace(".bak", "").Split('_');
                    if (parts.Length >= 3)
                    {
                        note = string.Join(" ", parts.Skip(2));
                    }

                    int count = 0;
                    try
                    {
                        var lines = File.ReadAllLines(f);
                        count = lines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"));
                    }
                    catch { }

                    list.Add(new HostsBackupItem
                    {
                        FileName = name,
                        FilePath = f,
                        CreatedAt = fi.LastWriteTime,
                        FileSizeBytes = fi.Length,
                        Note = note,
                        EntryCount = count
                    });
                }
            }
            catch { }
            return list;
        });
    }

    /// <summary>
    /// Restores a selected historical backup snapshot to the active Hosts file.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreBackupAsync(string backupFilePath)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(backupFilePath)) return (false, "指定之備份檔案不存在。");
                string content = await File.ReadAllTextAsync(backupFilePath, Encoding.UTF8);
                return await SaveRawHostsAsync(content, createBackup: true, backupNote: "還原前自動快照");
            }
            catch (Exception ex)
            {
                return (false, $"還原備份失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Deletes a historical backup file.
    /// </summary>
    public bool DeleteBackup(string backupFilePath)
    {
        try
        {
            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Launches the Windows system Notepad with the active Hosts file.
    /// </summary>
    public void OpenInSystemNotepad()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", HostsPath) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>
    /// Runs a comprehensive structural and security analysis on the Hosts content.
    /// </summary>
    public HostsAnalysisReport AnalyzeHosts(string rawContent)
    {
        var report = new HostsAnalysisReport();
        if (string.IsNullOrWhiteSpace(rawContent)) return report;

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        report.TotalLines = lines.Length;

        var domainToIps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] highRiskTargets = ["microsoft.com", "windowsupdate.com", "live.com", "google.com", "apple.com", "github.com"];

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Check managed blocks
            string lower = trimmed.ToLowerInvariant();
            if (lower.Contains("docker") && !report.ManagedSections.Contains("Docker Desktop"))
                report.ManagedSections.Add("Docker Desktop");
            if (lower.Contains("tailscale") && !report.ManagedSections.Contains("Tailscale MagicDNS"))
                report.ManagedSections.Add("Tailscale MagicDNS");
            if (lower.Contains("kubernetes") && !report.ManagedSections.Contains("Kubernetes Internal"))
                report.ManagedSections.Add("Kubernetes Internal");
            if (lower.Contains("wsl") && !report.ManagedSections.Contains("WSL Subsystem"))
                report.ManagedSections.Add("WSL Subsystem");

            if (trimmed.StartsWith("#"))
            {
                report.CommentedEntriesCount++;
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                report.SyntaxErrors.Add($"格式無效 (缺少主機名): {trimmed}");
                continue;
            }

            string ip = parts[0];
            string domain = parts[1];

            // Validate IP
            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                report.SyntaxErrors.Add($"IP 位址無效: {ip} ({domain})");
                continue;
            }

            report.ActiveEntriesCount++;
            if (ip == "0.0.0.0" || ip == "127.0.0.1")
            {
                report.BlocklistEntriesCount++;
            }

            // High risk hijack check
            if (highRiskTargets.Any(t => domain.EndsWith(t, StringComparison.OrdinalIgnoreCase)))
            {
                if (ip != "127.0.0.1" && ip != "0.0.0.0" && !ip.StartsWith("10.") && !ip.StartsWith("192.168."))
                {
                    report.HighRiskEntries.Add($"{domain} -> {ip} (關鍵服務轉發)");
                }
            }

            // Conflict / Duplicate check
            if (!domainToIps.ContainsKey(domain))
            {
                domainToIps[domain] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                report.DuplicateDomains.Add(domain);
            }

            domainToIps[domain].Add(ip);
        }

        foreach (var kvp in domainToIps)
        {
            if (kvp.Value.Count > 1)
            {
                report.ConflictingEntries.Add($"{kvp.Key} 對應多個相異 IP: [{string.Join(", ", kvp.Value)}]");
            }
        }

        report.DuplicateDomains = report.DuplicateDomains.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return report;
    }

    /// <summary>
    /// Beautifies and aligns Hosts file columns neatly.
    /// </summary>
    public string FormatAndAlignHosts(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return rawContent;

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                sb.AppendLine();
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                sb.AppendLine(trimmed);
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && System.Net.IPAddress.TryParse(parts[0], out _))
            {
                string ip = parts[0];
                string host = parts[1];
                string comment = "";
                int hashIdx = trimmed.IndexOf('#');
                if (hashIdx >= 0)
                {
                    comment = " " + trimmed.Substring(hashIdx);
                }

                int padWidth = ip.Contains(':') ? 40 : 16;
                sb.AppendLine($"{ip.PadRight(padWidth)} {host}{comment}");
            }
            else
            {
                sb.AppendLine(trimmed);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Deduplicates entries for the same domain while preserving comments and ordering.
    /// </summary>
    public string DeduplicateHosts(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return rawContent;

        var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
            {
                sb.AppendLine(rawLine);
                continue;
            }

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && System.Net.IPAddress.TryParse(parts[0], out _))
            {
                string domain = parts[1];
                if (seenDomains.Contains(domain))
                {
                    // Skip duplicate entry
                    continue;
                }
                seenDomains.Add(domain);
                sb.AppendLine(rawLine);
            }
            else
            {
                sb.AppendLine(rawLine);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Restores the default pristine Windows Hosts file template.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreDefaultHostsAsync()
    {
        const string defaultHosts =
@"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# This file contains the mappings of IP addresses to host names. Each
# entry should be kept on an individual line. The IP address should
# be placed in the first column followed by the corresponding host name.
# The IP address and the host name should be separated by at least one
# space.
#
# Additionally, comments (such as these) may be inserted on individual
# lines or following the machine name denoted by a '#' symbol.
#
# For example:
#
#      102.54.94.97     rhino.acme.com          # source server
#       38.25.63.10     x.acme.com              # x client host

# localhost name resolution is handled within DNS itself.
#	127.0.0.1       localhost
#	::1             localhost
";
        return await SaveRawHostsAsync(defaultHosts, createBackup: true, backupNote: "還原官方預設");
    }

    /// <summary>
    /// Flushes the Windows DNS resolver cache.
    /// </summary>
    public static void FlushDnsCache()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }
}
