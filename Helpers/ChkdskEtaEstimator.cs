namespace DiskMasterWinUI.Helpers;

public enum ChkdskMode
{
    Scan,
    SpotFix,
    Fix,
    FullRepair
}

/// <summary>
/// Produces a starting ETA for chkdsk. Windows does not emit a steady percentage for an
/// online /scan, so a volume-size heuristic is used for the first run and the last actual
/// duration is reused for subsequent runs in the same process.
/// </summary>
public static class ChkdskEtaEstimator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, TimeSpan> ActualDurations = new(StringComparer.OrdinalIgnoreCase);

    public static TimeSpan Estimate(string drive, ChkdskMode mode)
    {
        string key = BuildKey(drive, mode);
        lock (Sync)
        {
            if (ActualDurations.TryGetValue(key, out var actual) && actual > TimeSpan.FromSeconds(5))
            {
                return actual;
            }
        }

        double volumeGb = GetVolumeSizeGb(drive);
        double tb = Math.Max(0.05, volumeGb / 1024.0);
        double minutesPerTb = mode switch
        {
            ChkdskMode.Scan => 6.0,
            ChkdskMode.SpotFix => 2.0,
            ChkdskMode.Fix => 12.0,
            ChkdskMode.FullRepair => 36.0,
            _ => 8.0
        };

        double minutes = Math.Clamp(tb * minutesPerTb, 1.0, 240.0);
        return TimeSpan.FromMinutes(minutes);
    }

    public static void RecordActual(string drive, ChkdskMode mode, TimeSpan duration)
    {
        if (duration <= TimeSpan.FromSeconds(5)) return;
        lock (Sync)
        {
            ActualDurations[BuildKey(drive, mode)] = duration;
        }
    }

    private static string BuildKey(string drive, ChkdskMode mode)
    {
        string letter = string.IsNullOrWhiteSpace(drive)
            ? "?"
            : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
        return $"{letter}:{mode}";
    }

    private static double GetVolumeSizeGb(string drive)
    {
        try
        {
            string letter = drive.Trim().TrimEnd(':', '\\');
            var info = new DriveInfo(letter + ":\\");
            return info.TotalSize / (1024.0 * 1024.0 * 1024.0);
        }
        catch
        {
            return 0;
        }
    }
}
