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
        double dpiScale = Math.Max(1.0, dpi / 96.0);
        double logicalWidth = workArea.Width / dpiScale;
        double logicalHeight = workArea.Height / dpiScale;

        // If logical screen height in DIPs is very compact (<= 780 DIPs, e.g., 1080p at 150% DPI or 768p)
        if (logicalHeight <= 780 || logicalWidth <= 1300)
        {
            return 85;
        }

        // If logical screen height in DIPs is moderate (<= 920 DIPs, e.g., 1080p at 125% DPI)
        if (logicalHeight <= 920 || logicalWidth <= 1550)
        {
            return 90;
        }

        // Standard 1080p@100%, 2K, or 4K display with plenty of logical DIP room
        return 100;
    }

    /// <summary>
    /// Calculates optimal window dimensions (width, height, X, Y) centered on the work area.
    /// Accounts for physical pixel DPI scaling so high-DPI monitors are properly sized and centered.
    /// </summary>
    public static (PointInt32 Position, SizeInt32 Size) GetOptimalWindowBounds(IntPtr hWnd)
    {
        var work = GetWorkArea(hWnd);
        double dpiScale = Math.Max(1.0, GetDpi(hWnd) / 96.0);

        int targetWidth = (int)Math.Round(work.Width * 0.88);
        int targetHeight = (int)Math.Round(work.Height * 0.88);

        // Clamp to DPI-scaled min/max physical pixel bounds
        int minW = Math.Min(work.Width - 24, (int)Math.Round(1150 * dpiScale));
        int maxW = Math.Max(minW, (int)Math.Round(1680 * dpiScale));
        int minH = Math.Min(work.Height - 24, (int)Math.Round(720 * dpiScale));
        int maxH = Math.Max(minH, (int)Math.Round(1050 * dpiScale));

        targetWidth = Math.Clamp(targetWidth, Math.Max(640, minW), maxW);
        targetHeight = Math.Clamp(targetHeight, Math.Max(480, minH), maxH);

        // Ensure target does not exceed screen work area
        if (targetWidth > work.Width - 24) targetWidth = Math.Max(640, work.Width - 24);
        if (targetHeight > work.Height - 24) targetHeight = Math.Max(480, work.Height - 24);

        int posX = work.X + Math.Max(0, (work.Width - targetWidth) / 2);
        int posY = work.Y + Math.Max(0, (work.Height - targetHeight) / 2);

        return (new PointInt32(posX, posY), new SizeInt32(targetWidth, targetHeight));
    }

    /// <summary>
    /// Calculates robust multi-monitor floating window bounds taking into account:
    /// 1. Physical target monitor detection from drop cursor point (or parent HWND)
    /// 2. Target monitor WorkArea (excluding taskbar on left/right/top/bottom)
    /// 3. Negative screen coordinates (left-hand or upper secondary monitors)
    /// 4. Optimal proportional sizing (70% width, 72% height) clamped with DPI scale
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
        double dpiScale = Math.Max(1.0, GetDpi(parentHwnd) / 96.0);

        // Dynamically compute floating window dimensions based on target monitor resolution
        int targetWidth = (int)(work.Width * 0.70);
        int targetHeight = (int)(work.Height * 0.72);

        // DPI-scaled Min & Max clamping
        int minW = Math.Min(work.Width - 40, (int)Math.Round(720 * dpiScale));
        int maxW = Math.Max(minW, (int)Math.Round(1600 * dpiScale));
        int minH = Math.Min(work.Height - 40, (int)Math.Round(480 * dpiScale));
        int maxH = Math.Max(minH, (int)Math.Round(1050 * dpiScale));

        targetWidth = Math.Clamp(targetWidth, Math.Max(400, minW), maxW);
        targetHeight = Math.Clamp(targetHeight, Math.Max(300, minH), maxH);

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

    /// <summary>
    /// Calculates the unscaled layout width, horizontal centering offset, and bottom scroll margin
    /// for a zoom-scaled ScrollViewer viewport so content stays centered and never clips on the right
    /// at any UI scale factor (e.g. 85%, 100%, 125%, 150%).
    /// </summary>
    public static (double LayoutWidth, double HorizontalOffset, double BottomMargin, double ClampedScale) CalculateScaledViewportLayout(
        double viewportWidth,
        double viewportHeight,
        double contentActualHeight,
        double scaleFactor)
    {
        double clampedScale = Math.Clamp(scaleFactor, 0.7, 1.8);
        if (viewportWidth <= 0)
        {
            return (360.0, 0.0, 0.0, clampedScale);
        }

        // Reserve 20 DIPs for the vertical scrollbar and right-edge breathing room so card borders never clip
        const double scrollbarAndRightGutter = 20.0;
        double usableWidth = Math.Max(320.0, viewportWidth - scrollbarAndRightGutter);
        double layoutWidth = Math.Max(360.0, usableWidth / clampedScale);

        double visualWidth = layoutWidth * clampedScale;
        double centerableViewportWidth = Math.Max(visualWidth, viewportWidth - 16.0);
        double horizontalOffset = Math.Max(0.0, Math.Round((centerableViewportWidth - visualWidth) / 2.0));

        double bottomMargin = clampedScale > 1.0 && contentActualHeight > 0
            ? Math.Ceiling(contentActualHeight * (clampedScale - 1.0)) + 32.0
            : 0.0;

        return (layoutWidth, horizontalOffset, bottomMargin, clampedScale);
    }
}
