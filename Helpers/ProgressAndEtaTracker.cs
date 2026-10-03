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

    private readonly Stopwatch _stopwatch = new();
    private double _currentPercentage;
    private long _processedFiles;

    public event Action<double, string, string>? ProgressUpdated;

    public bool IsActive { get; private set; }
    public double CurrentPercentage => _currentPercentage;
    public TimeSpan ElapsedTime => _stopwatch.Elapsed;

    public void Start(string initialOperation = "Running...")
    {
        _currentPercentage = 0;
        _processedFiles = 0;
        _stopwatch.Restart();
        IsActive = true;
        NotifyUpdate(0, "00:00", "Estimating...");
    }

    public void Stop()
    {
        _stopwatch.Stop();
        IsActive = false;
        NotifyUpdate(_currentPercentage, FormatTimeSpan(_stopwatch.Elapsed), "Completed");
    }

    public void Reset()
    {
        _stopwatch.Reset();
        _currentPercentage = 0;
        _processedFiles = 0;
        IsActive = false;
    }

    public void ProcessLine(string line)
    {
        if (!IsActive || string.IsNullOrWhiteSpace(line)) return;

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
            _processedFiles = files;
            var elapsedStr = FormatTimeSpan(_stopwatch.Elapsed);
            NotifyUpdate(0, elapsedStr, $"{_processedFiles:N0} files processed");
        }
    }

    public void UpdateProgress(double percentage)
    {
        _currentPercentage = Math.Clamp(percentage, 0.0, 100.0);
        var elapsed = _stopwatch.Elapsed;
        var elapsedStr = FormatTimeSpan(elapsed);
        var etaStr = CalculateEta(elapsed, _currentPercentage);

        NotifyUpdate(_currentPercentage, elapsedStr, etaStr);
    }

    private string CalculateEta(TimeSpan elapsed, double percentage)
    {
        if (percentage <= 2.0)
            return "Calculating ETA...";
        if (percentage >= 99.5)
            return "Almost done...";

        var totalSeconds = elapsed.TotalSeconds / (percentage / 100.0);
        var remainingSeconds = Math.Max(0, totalSeconds - elapsed.TotalSeconds);
        var remainingTime = TimeSpan.FromSeconds(remainingSeconds);

        if (remainingTime.TotalHours >= 1)
            return $"~{remainingTime.Hours}h {remainingTime.Minutes}m remaining";
        return $"~{remainingTime.Minutes:D2}:{remainingTime.Seconds:D2} remaining";
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
