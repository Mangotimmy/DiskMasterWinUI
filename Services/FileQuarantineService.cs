using System.IO;
using System.Text.Json;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Manages the DiskMaster Quarantine Restore Vault (%LOCALAPPDATA%\DiskMaster_Portable\Quarantine).
/// Safely moves eligible cleaned files into timestamped sessions with JSON manifests so any user can
/// restore files to their exact original paths in 1 click, while enforcing automatic 7-day retention
/// and a 2 GB maximum quota so the vault never fills up the user's SSD.
/// </summary>
public class FileQuarantineService
{
    public const long MaxSingleFileBytes = 100L * 1024 * 1024; // 100 MB per single file

    public string VaultRootPath { get; }

    public FileQuarantineService(string? customVaultRoot = null)
    {
        VaultRootPath = string.IsNullOrWhiteSpace(customVaultRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMaster_Portable", "Quarantine")
            : customVaultRoot;
    }

    public string DriverBackupsRootPath => Path.Combine(VaultRootPath, "DriverBackups");

    /// <summary>
    /// Active writer for a single cleanup batch.
    /// </summary>
    public sealed class QuarantineSessionWriter : IDisposable
    {
        private readonly FileQuarantineService _owner;
        private readonly string _sessionDir;
        private readonly string _filesDir;
        private readonly QuarantineSessionManifest _manifest;
        private int _counter;
        private bool _disposed;

        internal QuarantineSessionWriter(FileQuarantineService owner, string categorySummary)
        {
            _owner = owner;
            var sessionId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
            _sessionDir = Path.Combine(owner.VaultRootPath, sessionId);
            _filesDir = Path.Combine(_sessionDir, "files");
            _manifest = new QuarantineSessionManifest
            {
                SessionId = sessionId,
                CreatedUtc = DateTime.UtcNow,
                CategorySummary = categorySummary
            };
        }

        public int QuarantinedFileCount => _manifest.TotalFiles;
        public long QuarantinedBytes => _manifest.TotalBytes;

        /// <summary>
        /// Attempts to move (or copy+delete across volumes) a file into this Quarantine session.
        /// Returns false if file exceeds MaxSingleFileBytes or cannot be moved.
        /// </summary>
        public bool TryQuarantineFile(FileInfo file, string categoryId)
        {
            if (_disposed) return false;
            try
            {
                if (!file.Exists) return false;
                long len = file.Length;
                if (len > MaxSingleFileBytes) return false;

                Directory.CreateDirectory(_filesDir);
                int idx = Interlocked.Increment(ref _counter);
                var safeName = SanitizeFileName(file.Name);
                var relPath = Path.Combine("files", $"{idx:D5}_{safeName}");
                var destFull = Path.Combine(_sessionDir, relPath);

                try
                {
                    File.Move(file.FullName, destFull, overwrite: true);
                }
                catch (IOException)
                {
                    // Cross-volume move or brief lock fallback
                    File.Copy(file.FullName, destFull, overwrite: true);
                    file.Delete();
                }

                lock (_manifest)
                {
                    _manifest.Entries.Add(new QuarantineFileEntry
                    {
                        OriginalPath = file.FullName,
                        QuarantinedRelativePath = relPath,
                        FileSizeBytes = len,
                        LastWriteUtc = file.LastWriteTimeUtc,
                        CategoryId = categoryId
                    });
                    _manifest.TotalFiles++;
                    _manifest.TotalBytes += len;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Copies a file into this Quarantine session WITHOUT deleting the original (for pre-operation snapshots such as OneDrive settings).
        /// </summary>
        public bool TrySnapshotCopyFile(string sourcePath, string categoryId)
        {
            if (_disposed || !File.Exists(sourcePath)) return false;
            try
            {
                var fi = new FileInfo(sourcePath);
                if (fi.Length > MaxSingleFileBytes) return false;

                Directory.CreateDirectory(_filesDir);
                int idx = Interlocked.Increment(ref _counter);
                var relPath = Path.Combine("files", $"{idx:D5}_{SanitizeFileName(fi.Name)}");
                var destFull = Path.Combine(_sessionDir, relPath);
                File.Copy(sourcePath, destFull, overwrite: true);

                lock (_manifest)
                {
                    _manifest.Entries.Add(new QuarantineFileEntry
                    {
                        OriginalPath = fi.FullName,
                        QuarantinedRelativePath = relPath,
                        FileSizeBytes = fi.Length,
                        LastWriteUtc = fi.LastWriteTimeUtc,
                        CategoryId = categoryId
                    });
                    _manifest.TotalFiles++;
                    _manifest.TotalBytes += fi.Length;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Commit()
        {
            if (_disposed) return;
            try
            {
                if (_manifest.TotalFiles == 0)
                {
                    if (Directory.Exists(_sessionDir))
                    {
                        try { Directory.Delete(_sessionDir, true); } catch { }
                    }
                    return;
                }

                Directory.CreateDirectory(_sessionDir);
                var manifestPath = Path.Combine(_sessionDir, "manifest.json");
                var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(manifestPath, json);
                _owner.EnforceRetentionAndQuota();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Commit();
            _disposed = true;
        }
    }

    public QuarantineSessionWriter BeginSession(string categorySummary) => new(this, categorySummary);

    /// <summary>
    /// Lists all restorable Quarantine sessions sorted newest-first, enforcing retention/quota first.
    /// </summary>
    public Task<(List<QuarantineSessionItem> Sessions, long TotalVaultBytes, string TotalVaultDisplay)> ListSessionsAsync()
    {
        return Task.Run(() =>
        {
            EnforceRetentionAndQuota();
            var list = new List<QuarantineSessionItem>();
            long totalBytes = 0;

            if (!Directory.Exists(VaultRootPath))
                return (list, 0L, "0 B");

            foreach (var dir in Directory.EnumerateDirectories(VaultRootPath))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var m = JsonSerializer.Deserialize<QuarantineSessionManifest>(json);
                    if (m == null || m.TotalFiles == 0) continue;

                    totalBytes += m.TotalBytes;
                    list.Add(new QuarantineSessionItem
                    {
                        SessionId = m.SessionId,
                        CreatedLocal = m.CreatedUtc.ToLocalTime(),
                        CategorySummary = m.CategorySummary,
                        FileCount = m.TotalFiles,
                        TotalBytes = m.TotalBytes,
                        SizeDisplay = FormatBytes(m.TotalBytes),
                        IsRestored = m.IsRestored
                    });
                }
                catch { }
            }

            list = list.OrderByDescending(x => x.CreatedLocal).ToList();
            return (list, totalBytes, FormatBytes(totalBytes));
        });
    }

    /// <summary>
    /// Restores every file in the given session back to its exact OriginalPath.
    /// If a newer file already exists at the destination, saves alongside as *.restored to prevent overwriting newer user data.
    /// </summary>
    public Task<(bool Success, int RestoredCount, string Message)> RestoreSessionAsync(string sessionId, Action<string>? onLog = null)
    {
        return Task.Run(() =>
        {
            var sessionDir = Path.Combine(VaultRootPath, sessionId);
            var manifestPath = Path.Combine(sessionDir, "manifest.json");
            if (!File.Exists(manifestPath))
                return (false, 0, $"找不到隔離備份批次 [{sessionId}]。");

            try
            {
                var m = JsonSerializer.Deserialize<QuarantineSessionManifest>(File.ReadAllText(manifestPath));
                if (m == null || m.Entries.Count == 0)
                    return (false, 0, "此隔離批次沒有可還原的檔案。");

                int restored = 0;
                int renamed = 0;
                foreach (var entry in m.Entries)
                {
                    var src = Path.Combine(sessionDir, entry.QuarantinedRelativePath);
                    if (!File.Exists(src) || string.IsNullOrWhiteSpace(entry.OriginalPath)) continue;

                    try
                    {
                        var destDir = Path.GetDirectoryName(entry.OriginalPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                        string destPath = entry.OriginalPath;
                        if (File.Exists(destPath))
                        {
                            destPath = $"{entry.OriginalPath}.restored";
                            renamed++;
                        }

                        File.Copy(src, destPath, overwrite: true);
                        try { File.SetLastWriteTimeUtc(destPath, entry.LastWriteUtc); } catch { }
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        onLog?.Invoke($"[WARN] 略過無法還原項目 {entry.OriginalPath}: {ex.Message}");
                    }
                }

                m.IsRestored = true;
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));

                string note = renamed > 0 ? $"（其中 {renamed} 個檔案因原路徑已有新檔，已另存為 .restored 防止覆蓋）" : "";
                string msg = $"✅ 已成功還原 {restored:N0}/{m.Entries.Count:N0} 個檔案至原始路徑！{note}";
                onLog?.Invoke(msg);
                return (restored > 0, restored, msg);
            }
            catch (Exception ex)
            {
                return (false, 0, $"還原隔離批次失敗: {ex.Message}");
            }
        });
    }

    public Task<bool> DeleteSessionAsync(string sessionId)
    {
        return Task.Run(() =>
        {
            try
            {
                var sessionDir = Path.Combine(VaultRootPath, sessionId);
                if (Directory.Exists(sessionDir))
                {
                    Directory.Delete(sessionDir, true);
                    return true;
                }
            }
            catch { }
            return false;
        });
    }

    public Task<(int RemovedSessions, string Message)> PurgeAllSessionsAsync()
    {
        return Task.Run(() =>
        {
            int removed = 0;
            try
            {
                if (Directory.Exists(VaultRootPath))
                {
                    foreach (var dir in Directory.EnumerateDirectories(VaultRootPath))
                    {
                        if (Path.GetFileName(dir).Equals("DriverBackups", StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            Directory.Delete(dir, true);
                            removed++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return (removed, $"已清空 {removed} 個隔離還原批次，釋放所有暫存備份空間。");
        });
    }

    /// <summary>
    /// Enforces automatic 7-day retention and 2 GB maximum quota by pruning expired or oldest sessions.
    /// </summary>
    public void EnforceRetentionAndQuota(int? overrideDays = null, long? overrideMaxMb = null)
    {
        try
        {
            if (!Directory.Exists(VaultRootPath)) return;

            var settings = SettingsService.Instance.Current;
            int retainDays = Math.Clamp(overrideDays ?? settings.QuarantineRetentionDays, 1, 90);
            long maxBytes = Math.Clamp(overrideMaxMb ?? settings.QuarantineMaxSizeMb, 256, 20480) * 1024L * 1024L;
            var cutoffUtc = DateTime.UtcNow.AddDays(-retainDays);

            var validSessions = new List<(string Dir, DateTime CreatedUtc, long Bytes)>();

            foreach (var dir in Directory.EnumerateDirectories(VaultRootPath))
            {
                if (Path.GetFileName(dir).Equals("DriverBackups", StringComparison.OrdinalIgnoreCase))
                    continue;

                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var m = JsonSerializer.Deserialize<QuarantineSessionManifest>(File.ReadAllText(manifestPath));
                    if (m == null || m.CreatedUtc < cutoffUtc)
                    {
                        try { Directory.Delete(dir, true); } catch { }
                        continue;
                    }
                    validSessions.Add((dir, m.CreatedUtc, m.TotalBytes));
                }
                catch { }
            }

            long total = validSessions.Sum(s => s.Bytes);
            if (total > maxBytes)
            {
                // Prune oldest sessions until within quota
                foreach (var oldest in validSessions.OrderBy(s => s.CreatedUtc))
                {
                    if (total <= maxBytes) break;
                    try
                    {
                        Directory.Delete(oldest.Dir, true);
                        total -= oldest.Bytes;
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 80 ? cleaned[^80..] : cleaned;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
