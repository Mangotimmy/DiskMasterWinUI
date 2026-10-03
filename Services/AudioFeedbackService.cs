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

    /// <summary>
    /// Gets or sets whether audible feedback effects are globally enabled.
    /// </summary>
    public static bool IsSoundEnabled { get; set; } = true;

    /// <summary>
    /// Plays standard completion / success audio cue through multi-tiered fallback.
    /// </summary>
    public static void PlaySuccess()
    {
        if (!IsSoundEnabled) return;

        Task.Run(() =>
        {
            bool played = false;

            // Tier 1: WinMM PlaySound system notification alias
            try
            {
                if (PlaySound("SystemNotification", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT))
                {
                    played = true;
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
                        played = true;
                        break;
                    }
                }
            }
            catch { }

            // Tier 3: Native User32 MessageBeep
            try
            {
                MessageBeep(MB_ICONASTERISK);
            }
            catch { }

            // Tier 4: Console Beep hardware fallback if no audio device responded
            if (!played)
            {
                try { Console.Beep(1200, 100); } catch { }
            }
        });
    }

    public static void PlayAsterisk() => PlaySuccess();

    /// <summary>
    /// Plays warning sound cue.
    /// </summary>
    public static void PlayWarning()
    {
        if (!IsSoundEnabled) return;

        Task.Run(() =>
        {
            try { PlaySound("SystemExclamation", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT); } catch { }
            try { MessageBeep(MB_ICONEXCLAMATION); } catch { }
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
            try { PlaySound("SystemHand", IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT); } catch { }
            try { MessageBeep(MB_ICONHAND); } catch { }
            try { Console.Beep(600, 250); } catch { }
        });
    }
}
