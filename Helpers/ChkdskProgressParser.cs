using System.Text.RegularExpressions;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Maps chkdsk stage/milestone/percentage lines to an overall 0-100 progress value.
/// The output is localised by Windows, so stage headers and completion milestones are
/// matched for English, Traditional Chinese, Simplified Chinese and Japanese.
/// </summary>
public sealed class ChkdskProgressParser
{
    private static readonly Regex StageRegex = new(
        @"(?:Stage|階段|阶段|ステージ)\s*([1-5])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PercentRegex = new(
        @"(\d{1,3}(?:[.,]\d+)?)\s*(?:%|％|percent)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly double[] StageBase = { 0, 8, 42, 70, 92, 96 };
    private static readonly double[] StageEnd = { 0, 40, 68, 88, 95, 99 };

    private int _stage;
    private double _lastPercent = -1;

    public void Reset()
    {
        _stage = 0;
        _lastPercent = -1;
    }

    public double? ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        double? candidate = null;

        if (ContainsAny(line,
                "Windows has scanned the file system",
                "Windows 已掃描檔案系統",
                "Windows 已扫描文件系统",
                "Windows はファイル システムをスキャン"))
        {
            candidate = 99;
        }
        else if (ContainsAny(line, "Usn Journal verification completed", "USN 日誌驗證完成", "USN 日志验证完成", "USN ジャーナル"))
        {
            candidate = 97;
        }
        else if (ContainsAny(line, "Security descriptor verification completed", "安全性描述元驗證完成", "安全描述符验证已完成", "セキュリティ記述子"))
        {
            candidate = 88;
        }
        else if (ContainsAny(line, "Index verification completed", "索引驗證完成", "索引验证已完成", "インデックス"))
        {
            candidate = 68;
        }
        else if (ContainsAny(line, "File verification completed", "檔案驗證完成", "文件验证已完成", "ファイルの確認が完了"))
        {
            candidate = 40;
        }

        var stageMatch = StageRegex.Match(line);
        if (stageMatch.Success && int.TryParse(stageMatch.Groups[1].Value, out int stage) && stage is >= 1 and <= 5)
        {
            _stage = stage;
            double stageStart = StageBase[stage];
            if (candidate == null || candidate.Value < stageStart) candidate = stageStart;
        }

        var percentMatch = PercentRegex.Match(line);
        if (percentMatch.Success &&
            double.TryParse(percentMatch.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double rawPercent))
        {
            rawPercent = Math.Clamp(rawPercent, 0, 100);
            double mapped;
            if (_stage >= 1 && _stage <= 5)
            {
                double start = StageBase[_stage];
                double end = StageEnd[_stage];
                mapped = start + (rawPercent / 100.0) * (end - start);
            }
            else
            {
                mapped = rawPercent;
            }
            if (candidate == null || candidate.Value < mapped) candidate = mapped;
        }

        if (candidate == null) return null;

        double value = Math.Clamp(candidate.Value, 0, 100);
        if (value <= _lastPercent) return null;
        _lastPercent = value;
        return value;
    }

    private static bool ContainsAny(string line, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
