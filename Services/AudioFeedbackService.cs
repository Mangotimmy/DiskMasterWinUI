using System.Runtime.InteropServices;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Multi-tiered audible feedback engine ensuring guaranteed sound reproduction
/// across custom Windows themes, headless environments, and desktop sound configurations.
/// </summary>
public static class AudioFeedbackService
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONHAND = 0x00000010;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_ICONEXCLAMATION = 0x00000030;
    private const uint MB_ICONASTERISK = 0x00000040;

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_FILENAME = 0x00020000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private static bool? _soundEnabledOverride;

    /// <summary>
    /// Whether audible feedback is globally enabled. This follows the user's
    /// "音效回饋" setting unless a caller explicitly overrides it for this session —
    /// previously the property was never assigned, so the setting was ignored by every
    /// caller except MainWindow and the app kept beeping with feedback switched off.
    /// </summary>
    public static bool IsSoundEnabled
    {
        get
        {
            if (_soundEnabledOverride.HasValue) return _soundEnabledOverride.Value;
            try { return SettingsService.Instance.Current.EnableAudioFeedback; }
            catch { return true; }
        }
        set => _soundEnabledOverride = value;
    }

    /// <summary>
    /// Plays standard completion / success audio cue through multi-tiered fallback,
    /// adhering to the user's selected notification sound scheme in Settings.
    /// </summary>
    public static void PlaySuccess()
    {
        if (!IsSoundEnabled) return;

        var scheme = SettingsService.Instance.Current.NotificationSoundScheme ?? "Default";
        var customPath = SettingsService.Instance.Current.CustomNotificationSoundPath;

        if (scheme != "Default")
        {
            PlaySoundScheme(scheme, customPath);
            return;
        }

        Task.Run(() =>
        {
            // Each tier is a fallback for the previous one. Playing all tiers back to back
            // produced up to three overlapping sounds for a single notification.
            try
            {
                if (PlaySound("SystemNotification", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT))
                {
                    return;
                }
            }
            catch { }

            // Tier 2: Known Windows WAV file fallback
            try
            {
                string[] wavCandidates = [
                    @"C:\Windows\Media\Windows Notify System Generic.wav",
                    @"C:\Windows\Media\Windows Background.wav",
                    @"C:\Windows\Media\chimes.wav",
                    @"C:\Windows\Media\notify.wav"
                ];

                foreach (var wav in wavCandidates)
                {
                    if (File.Exists(wav))
                    {
                        PlaySound(wav, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                        return;
                    }
                }
            }
            catch { }

            // Tier 3: Native User32 MessageBeep
            try
            {
                if (MessageBeep(MB_ICONASTERISK)) return;
            }
            catch { }

            // Tier 4: Console Beep hardware fallback if no audio device responded
            try { Console.Beep(1200, 100); } catch { }
        });
    }

    public static void PlayAsterisk() => PlaySuccess();

    /// <summary>
    /// Plays an explicit notification sound scheme for preview or execution.
    /// </summary>
    public static void PlaySoundScheme(string scheme, string? customPath = null)
    {
        Task.Run(() =>
        {
            try
            {
                if (scheme == "Custom" && !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    PlaySound(customPath, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                    return;
                }

                string? wavFile = scheme switch
                {
                    "Chimes" => @"C:\Windows\Media\chimes.wav",
                    "Ding" => @"C:\Windows\Media\Windows Notify Messaging.wav",
                    "Tada" => @"C:\Windows\Media\tada.wav",
                    "Crystal" => @"C:\Windows\Media\Windows Background.wav",
                    _ => null
                };

                if (wavFile != null && File.Exists(wavFile))
                {
                    PlaySound(wavFile, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                    return;
                }

                PlaySound("SystemNotification", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT);
            }
            catch
            {
                try { MessageBeep(MB_ICONASTERISK); } catch { }
            }
        });
    }

    /// <summary>
    /// Plays warning sound cue.
    /// </summary>
    public static void PlayWarning()
    {
        if (!IsSoundEnabled) return;

        Task.Run(() =>
        {
            try { if (PlaySound("SystemExclamation", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT)) return; } catch { }
            try { if (MessageBeep(MB_ICONEXCLAMATION)) return; } catch { }
            try { Console.Beep(800, 150); } catch { }
        });
    }

    public static void PlayExclamation() => PlayWarning();

    /// <summary>
    /// Plays critical error sound cue.
    /// </summary>
    public static void PlayError()
    {
        if (!IsSoundEnabled) return;

        Task.Run(() =>
        {
            try { if (PlaySound("SystemHand", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT)) return; } catch { }
            try { if (MessageBeep(MB_ICONHAND)) return; } catch { }
            try { Console.Beep(600, 250); } catch { }
        });
    }
}
