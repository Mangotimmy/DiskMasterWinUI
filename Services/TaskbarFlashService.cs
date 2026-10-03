using System.Runtime.InteropServices;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Provides taskbar flashing functionality using native Win32 FlashWindowEx to alert users of warnings or completions.
/// </summary>
public static class TaskbarFlashService
{
    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_CAPTION = 1;
    private const uint FLASHW_TRAY = 2;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMER = 4;
    private const uint FLASHW_TIMERNOFG = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    /// <summary>
    /// Flashes the window taskbar button continuously until the user brings the window to the foreground.
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    /// <param name="flashTimes">Number of times to flash, or 0 to flash until foreground</param>
    public static void Flash(IntPtr hwnd, uint flashTimes = 0)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = flashTimes == 0 ? FLASHW_ALL | FLASHW_TIMERNOFG : FLASHW_ALL,
                uCount = flashTimes == 0 ? 5 : flashTimes,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);
        }
        catch { }
    }

    /// <summary>
    /// Flashes the window taskbar button. Alias for Flash.
    /// </summary>
    public static void FlashWindow(IntPtr hwnd, uint count = 0) => Flash(hwnd, count);

    /// <summary>
    /// Stops flashing the window taskbar button.
    /// </summary>
    public static void StopFlash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = FLASHW_STOP,
                uCount = 0,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);
        }
        catch { }
    }
}
