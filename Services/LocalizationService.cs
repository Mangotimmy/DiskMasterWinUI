using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Services;

/// <summary>
/// High-performance localization service decoupled into external UTF-8 JSON files (Locales/*.json).
/// Avoids source code encoding corruption while ensuring dynamic 4-culture parity and zero runtime latency.
/// </summary>
public partial class LocalizationService : ObservableObject
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event Action? LanguageChanged;

    [ObservableProperty]
    private string _currentLanguage = "zh-TW";

    public bool IsChinese => CurrentLanguage == "zh-TW" || CurrentLanguage == "zh-CN";

    public static string S(string zh, string en) => Instance.IsChinese ? zh : en;

    public static string T(string zhTw, string zhCn, string enUs, string jaJp) =>
        Instance.CurrentLanguage switch
        {
            "zh-TW" => zhTw,
            "zh-CN" => zhCn,
            "ja-JP" => jaJp,
            _ => enUs
        };

    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);

    public LocalizationService()
    {
        LoadTranslations();
    }

    /// <summary>
    /// Loads culture dictionaries from external UTF-8 JSON configuration files.
    /// </summary>
    public void LoadTranslations()
    {
        string[] supportedCultures = ["zh-TW", "zh-CN", "en-US", "ja-JP"];
        foreach (var c in supportedCultures)
        {
            if (!_translations.ContainsKey(c))
            {
                _translations[c] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        string[] candidateDirs = [
            Path.Combine(AppContext.BaseDirectory, "Locales"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales"),
            Path.Combine(Path.GetDirectoryName(typeof(LocalizationService).Assembly.Location) ?? "", "Locales"),
            Path.Combine(Directory.GetCurrentDirectory(), "Locales"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Locales")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Locales"))
        ];

        string? localesDir = candidateDirs.FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d));

        if (localesDir != null)
        {
            foreach (var culture in supportedCultures)
            {
                var filePath = Path.Combine(localesDir, $"{culture}.json");
                if (File.Exists(filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath, Encoding.UTF8);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null)
                        {
                            foreach (var (k, v) in dict)
                            {
                                _translations[culture][k] = v;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        // Guarantee fallback parity across all 4 cultures if files were missing
        SynchronizeParityAndFallback(supportedCultures);
    }

    private void SynchronizeParityAndFallback(string[] supportedCultures)
    {
        // If zh-TW loaded keys, ensure any missing keys in other cultures inherit meaningful fallback
        if (_translations.TryGetValue("zh-TW", out var twDict) && twDict.Count > 0)
        {
            foreach (var culture in supportedCultures)
            {
                if (culture == "zh-TW") continue;
                var targetDict = _translations[culture];
                foreach (var (k, v) in twDict)
                {
                    if (!targetDict.ContainsKey(k) || string.IsNullOrWhiteSpace(targetDict[k]))
                    {
                        targetDict[k] = v;
                    }
                }
            }
        }
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (_translations.TryGetValue(CurrentLanguage, out var langDict) && langDict.TryGetValue(key, out var val))
        {
            return val;
        }

        if (_translations.TryGetValue("zh-TW", out var twDict) && twDict.TryGetValue(key, out var twVal))
        {
            return twVal;
        }

        if (_translations.TryGetValue("en-US", out var enDict) && enDict.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public void SetLanguage(string language)
    {
        CurrentLanguage = language;
    }

    public void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage switch
        {
            "zh-TW" => "zh-CN",
            "zh-CN" => "en-US",
            "en-US" => "ja-JP",
            _ => "zh-TW"
        };
    }

    partial void OnCurrentLanguageChanged(string value)
    {
        LanguageChanged?.Invoke();
        OnPropertyChanged(string.Empty);
    }
}
