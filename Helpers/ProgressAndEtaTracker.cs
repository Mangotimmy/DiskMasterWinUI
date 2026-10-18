using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DiskMasterWinUI.Helpers;

public partial class ProgressAndEtaTracker
{
    // Regex for DISM progress: [================ 45.2% ================]
    [GeneratedRegex(@"\[=+?\s*([\d\.]+)%\s*=+?\]")]
    private static partial Regex DismProgressRegex();

    // Regex for SFC progress: Verification 45% complete. or 驗證 45% 已完成
    [GeneratedRegex(@"(?:Verification|驗證|验证|掃描|扫描)\s+([\d\.]+)%")]
    private static partial Regex SfcProgressRegex();

    // Regex for icacls file processing: processed 1250 files
    [GeneratedRegex(@"processed\s+(\d+)\s+files", RegexOptions.IgnoreCase)]
    private static partial Regex IcaclsProgressRegex();

    // ProcessLine is driven by Process.OutputDataReceived, which fires on ThreadPool threads, while
    // Start/Stop/Reset/UpdateProgress come from the UI thread. Stopwatch and the counters are not
    // thread-safe, so every access goes through this lock. ProgressUpdated is always raised OUTSIDE
    // the lock (on the UI thread) so a handler that reads these members cannot deadlock.
    private readonly object _sync = new();

    private readonly Stopwatch _stopwatch = new();
    private readonly ChkdskProgressParser _chkdskParser = new();
    private double _currentPercentage;
    private long _processedFiles;
    private TimeSpan? _expectedDuration;
    private volatile bool _isActive;

    public event Action<double, string, string>? ProgressUpdated;

    public bool IsActive => _isActive;
    public double CurrentPercentage { get { lock (_sync) return _currentPercentage; } }
    public TimeSpan ElapsedTime { get { lock (_sync) return _stopwatch.Elapsed; } }

    public void Start(string initialOperation = "Running...", TimeSpan? expectedDuration = null)
    {
        lock (_sync)
        {
            _currentPercentage = 0;
            _processedFiles = 0;
            _expectedDuration = expectedDuration;
            _chkdskParser.Reset();
            _stopwatch.Restart();
            _isActive = true;
        }
        NotifyUpdate(0, "00:00", "Estimating...");
    }

    public void Stop()
    {
        double percent;
        string elapsed;
        lock (_sync)
        {
            _stopwatch.Stop();
            _isActive = false;
            percent = _currentPercentage;
            elapsed = FormatTimeSpan(_stopwatch.Elapsed);
        }
        NotifyUpdate(percent, elapsed, "Completed");
    }

    /// <summary>Marks completion at exactly 100% before the status bar shows its completed state.</summary>
    public void Complete()
    {
        double elapsedSeconds;
        lock (_sync)
        {
            _currentPercentage = 100;
            _stopwatch.Stop();
            _isActive = false;
            elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        }
        NotifyUpdate(100, FormatTimeSpan(TimeSpan.FromSeconds(elapsedSeconds)), "Completed");
    }

    public void Reset()
    {
        lock (_sync)
        {
            _stopwatch.Reset();
            _currentPercentage = 0;
            _processedFiles = 0;
            _expectedDuration = null;
            _chkdskParser.Reset();
            _isActive = false;
        }
    }

    public void ProcessLine(string line)
    {
        if (!_isActive || string.IsNullOrWhiteSpace(line)) return;

        // 1. Try DISM regex
        var dismMatch = DismProgressRegex().Match(line);
        if (dismMatch.Success && double.TryParse(dismMatch.Groups[1].Value, out var dismVal))
        {
            UpdateProgress(dismVal);
            return;
        }

        // 2. Try SFC regex
        var sfcMatch = SfcProgressRegex().Match(line);
        if (sfcMatch.Success && double.TryParse(sfcMatch.Groups[1].Value, out var sfcVal))
        {
            UpdateProgress(sfcVal);
            return;
        }

        // 3. Try icacls regex
        var icaclsMatch = IcaclsProgressRegex().Match(line);
        if (icaclsMatch.Success && long.TryParse(icaclsMatch.Groups[1].Value, out var files))
        {
            TimeSpan elapsed;
            lock (_sync)
            {
                if (!_isActive) return;
                _processedFiles = files;
                elapsed = _stopwatch.Elapsed;
            }
            NotifyUpdate(0, FormatTimeSpan(elapsed), $"{files:N0} files processed");
            return;
        }

        var chkdskProgress = _chkdskParser.ProcessLine(line);
        if (chkdskProgress.HasValue)
        {
            UpdateProgress(chkdskProgress.Value);
        }
    }

    public void UpdateProgress(double percentage)
    {
        double percent;
        string elapsedStr;
        string etaStr;
        lock (_sync)
        {
            var elapsed = _stopwatch.Elapsed;
            double candidate = Math.Clamp(percentage, 0.0, 100.0);
            double timePercent = GetTimeBasedPercent(elapsed);
            // Never let a late stage line move the bar backwards after the countdown has advanced.
            _currentPercentage = Math.Max(_currentPercentage, Math.Max(candidate, timePercent));
            percent = _currentPercentage;
            elapsedStr = FormatTimeSpan(elapsed);
            etaStr = BuildEta(elapsed, percent);
        }

        NotifyUpdate(percent, elapsedStr, etaStr);
    }

    /// <summary>
    /// Live snapshot for the status-bar timer. Unlike <see cref="UpdateProgress"/>, this does not
    /// depend on a process output line arriving, so elapsed time and the ETA keep updating for
    /// operations whose output never matches a progress regex.
    /// </summary>
    public (double Percent, string Elapsed, string Eta) GetLiveSnapshot()
    {
        lock (_sync)
        {
            if (!_isActive) return (_currentPercentage, "00:00", string.Empty);

            var elapsed = _stopwatch.Elapsed;
            double timePercent = GetTimeBasedPercent(elapsed);
            _currentPercentage = Math.Max(_currentPercentage, timePercent);
            string eta = BuildEta(elapsed, _currentPercentage);
            return (_currentPercentage, FormatTimeSpan(elapsed), eta);
        }
    }

    private double GetTimeBasedPercent(TimeSpan elapsed)
    {
        if (_expectedDuration is not { TotalSeconds: > 0 } expected) return 0;
        double percent = elapsed.TotalSeconds / expected.TotalSeconds * 100.0;
        return Math.Clamp(percent, 0, 98);
    }

    private string BuildEta(TimeSpan elapsed, double percentage)
    {
        if (percentage >= 100) return "Completed";

        if (_expectedDuration is { TotalSeconds: > 0 } expected)
        {
            var remaining = expected - elapsed;
            if (remaining > TimeSpan.FromSeconds(10))
            {
                return FormatRemaining(remaining);
            }
            return "Almost done...";
        }

        return percentage > 2.0 ? CalculateEta(elapsed, percentage) : string.Empty;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalSeconds < 10) return "Almost done...";
        if (remaining.TotalHours >= 1)
            return $"~{(int)remaining.TotalHours}h {remaining.Minutes}m remaining";
        return $"~{remaining.Minutes:D2}:{remaining.Seconds:D2} remaining";
    }

    private string CalculateEta(TimeSpan elapsed, double percentage)
    {
        if (percentage >= 100.0)
            return "Completed";
        if (percentage <= 2.0)
            return "Calculating ETA...";
        if (percentage >= 99.5)
            return "Almost done...";

        var totalSeconds = elapsed.TotalSeconds / (percentage / 100.0);
        var remainingSeconds = Math.Max(0, totalSeconds - elapsed.TotalSeconds);
        return FormatRemaining(TimeSpan.FromSeconds(remainingSeconds));
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private void NotifyUpdate(double percent, string elapsed, string eta)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            ProgressUpdated?.Invoke(percent, elapsed, eta);
        });
    }
}
