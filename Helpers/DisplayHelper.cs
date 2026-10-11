using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace DiskMasterWinUI.Helpers;

public static class DisplayHelper
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Gets the current mouse cursor position in screen coordinates.
    /// </summary>
    public static PointInt32 GetCurrentCursorPosition()
    {
        if (GetCursorPos(out var pt))
        {
            return new PointInt32(pt.X, pt.Y);
        }
        return new PointInt32(100, 100);
    }

    /// <summary>
    /// Gets the current system DPI for the specified window handle (standard is 96 = 100%).
    /// </summary>
    public static uint GetDpi(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return 96;
        try
        {
            var dpi = GetDpiForWindow(hWnd);
            return dpi > 0 ? dpi : 96;
        }
        catch
        {
            return 96;
        }
    }

    /// <summary>
    /// Gets the monitor's display work area (excluding Windows taskbar).
    /// </summary>
    public static RectInt32 GetWorkArea(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    return displayArea.WorkArea;
                }
            }
            catch { }
        }

        // Fallback default
        return new RectInt32(0, 0, 1920, 1080);
    }

    /// <summary>
    /// Automatically calculates the recommended WinUI scale percentage (85%, 90%, 100%)
    /// based on display work area resolution and Windows DPI scale.
    /// </summary>
    public static int GetRecommendedScalePercent(IntPtr hWnd)
    {
        var workArea = GetWorkArea(hWnd);
        var dpi = GetDpi(hWnd);

        // If Windows already applies 150%+ display scaling (144 DPI) or screen height is compact (<= 850px)
        if (dpi >= 144 || workArea.Height <= 850)
        {
            return 85;
        }

        // If Windows applies 125% scaling (120 DPI) or standard 1080p screen (<= 1000px height)
        if (dpi >= 120 || workArea.Height <= 1000)
        {
            return 90;
        }

        // High resolution 2K/4K display with plenty of room
        return 100;
    }

    /// <summary>
    /// Calculates optimal window dimensions (width, height, X, Y) centered on the work area.
    /// </summary>
    public static (PointInt32 Position, SizeInt32 Size) GetOptimalWindowBounds(IntPtr hWnd)
    {
        var work = GetWorkArea(hWnd);

        int targetWidth = (int)(work.Width * 0.88);
        int targetHeight = (int)(work.Height * 0.88);

        // Clamp to sensible min/max bounds
        targetWidth = Math.Clamp(targetWidth, 1150, 1680);
        targetHeight = Math.Clamp(targetHeight, 720, 1050);

        // Ensure target does not exceed screen work area
        if (targetWidth > work.Width) targetWidth = work.Width - 40;
        if (targetHeight > work.Height) targetHeight = work.Height - 40;

        int posX = work.X + Math.Max(0, (work.Width - targetWidth) / 2);
        int posY = work.Y + Math.Max(0, (work.Height - targetHeight) / 2);

        return (new PointInt32(posX, posY), new SizeInt32(targetWidth, targetHeight));
    }

    /// <summary>
    /// Calculates robust multi-monitor floating window bounds taking into account:
    /// 1. Physical target monitor detection from drop cursor point (or parent HWND)
    /// 2. Target monitor WorkArea (excluding taskbar on left/right/top/bottom)
    /// 3. Negative screen coordinates (left-hand or upper secondary monitors)
    /// 4. Optimal proportional sizing (70% width, 72% height) clamped between 720x480 and 1600x1050
    /// 5. Strict boundary clamping so the window never bleeds off-screen.
    /// </summary>
    public static RectInt32 CalculateFloatingWindowBounds(PointInt32? dropCursorPos, IntPtr parentHwnd = default)
    {
        DisplayArea? targetDisplay = null;

        if (dropCursorPos.HasValue)
        {
            try
            {
                targetDisplay = DisplayArea.GetFromPoint(dropCursorPos.Value, DisplayAreaFallback.Nearest);
            }
            catch { }
        }

        if (targetDisplay == null && parentHwnd != IntPtr.Zero)
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(parentHwnd);
                targetDisplay = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            }
            catch { }
        }

        targetDisplay ??= DisplayArea.Primary;
        var work = targetDisplay?.WorkArea ?? new RectInt32(0, 0, 1920, 1080);

        // Dynamically compute floating window dimensions based on target monitor resolution
        int targetWidth = (int)(work.Width * 0.70);
        int targetHeight = (int)(work.Height * 0.72);

        // Min & Max clamping
        targetWidth = Math.Clamp(targetWidth, 720, 1600);
        targetHeight = Math.Clamp(targetHeight, 480, 1050);

        // Safe bounds if monitor is smaller than standard
        if (targetWidth > work.Width - 40) targetWidth = Math.Max(400, work.Width - 40);
        if (targetHeight > work.Height - 40) targetHeight = Math.Max(300, work.Height - 40);

        int posX;
        int posY;

        if (dropCursorPos.HasValue)
        {
            // Position under cursor: place title bar grab region near cursor
            var cursor = dropCursorPos.Value;
            posX = cursor.X - (targetWidth / 2);
            posY = cursor.Y - 24; // 24px below title bar top
        }
        else
        {
            // Center in work area
            posX = work.X + (work.Width - targetWidth) / 2;
            posY = work.Y + (work.Height - targetHeight) / 2;
        }

        // Strict clamp to monitor work area boundaries (fully handles negative coordinates on left/top monitors!)
        int minX = work.X;
        int maxX = work.X + Math.Max(0, work.Width - targetWidth);
        int minY = work.Y;
        int maxY = work.Y + Math.Max(0, work.Height - targetHeight);

        posX = Math.Clamp(posX, minX, maxX);
        posY = Math.Clamp(posY, minY, maxY);

        return new RectInt32(posX, posY, targetWidth, targetHeight);
    }

    /// <summary>
    /// Returns human-readable diagnostic description of detected display metrics.
    /// </summary>
    public static string GetDisplayMetricsDescription(IntPtr hWnd)
    {
        var work = GetWorkArea(hWnd);
        var dpi = GetDpi(hWnd);
        int dpiPercent = (int)Math.Round(dpi / 96.0 * 100.0);
        int recommended = GetRecommendedScalePercent(hWnd);

        var label = DiskMasterWinUI.Services.LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "建议比例",
            "en-US" => "Recommended",
            "ja-JP" => "推奨スケール",
            _ => "建議比例"
        };
        return $"{work.Width} × {work.Height} (DPI: {dpiPercent}%) → {label}: {recommended}%";
    }
}
