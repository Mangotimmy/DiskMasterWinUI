using System.Runtime.InteropServices;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Provides audible feedback using native Windows system sounds when tasks complete or warnings trigger.
/// </summary>
public static class AudioFeedbackService
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONHAND = 0x00000010;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_ICONEXCLAMATION = 0x00000030;
    private const uint MB_ICONASTERISK = 0x00000040;

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    /// <summary>
    /// Gets or sets whether sound effects are globally enabled.
    /// </summary>
    public static bool IsSoundEnabled { get; set; } = true;

    /// <summary>
    /// Plays the standard notification/completion sound (Asterisk).
    /// </summary>
    public static void PlaySuccess()
    {
        if (!IsSoundEnabled) return;
        try { MessageBeep(MB_ICONASTERISK); } catch { }
    }

    public static void PlayAsterisk() => PlaySuccess();

    /// <summary>
    /// Plays the warning/alert sound (Exclamation).
    /// </summary>
    public static void PlayWarning()
    {
        if (!IsSoundEnabled) return;
        try { MessageBeep(MB_ICONEXCLAMATION); } catch { }
    }

    public static void PlayExclamation() => PlayWarning();

    /// <summary>
    /// Plays the error or critical confirmation sound (Hand).
    /// </summary>
    public static void PlayError()
    {
        if (!IsSoundEnabled) return;
        try { MessageBeep(MB_ICONHAND); } catch { }
    }
}
