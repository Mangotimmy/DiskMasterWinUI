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

    /// <summary>
    /// Saves the raw content to the Hosts file with backup and DNS flush.
    /// </summary>
    public async Task<(bool Success, string Message)> SaveRawHostsAsync(string rawContent, bool createBackup = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (createBackup && File.Exists(HostsPath))
                {
                    try
                    {
                        File.Copy(HostsPath, BackupPath, true);
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

                return (true, "Hosts file saved and DNS cache flushed successfully.");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Permission denied: Administrator privileges required to write to the Hosts file.");
            }
            catch (Exception ex)
            {
                return (false, $"Error saving Hosts file: {ex.Message}");
            }
        });
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
        return await SaveRawHostsAsync(defaultHosts, createBackup: true);
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
