using System.Runtime.InteropServices;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service managing the Windows notification area (System Tray) icon, minimizing to background,
/// and restoring the application window.
/// </summary>
public class TrayIconService : IDisposable
{
    private const uint WM_USER = 0x0400;
    public const uint WM_TRAYICON = WM_USER + 2048;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int SW_HIDE = 0;
    private const int SW_NORMAL = 1;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isIconAdded;
    private SubclassWndProc? _subclassDelegate;
    private readonly UIntPtr _subclassId = new UIntPtr(4096);

    public event Action? RestoreRequested;

    /// <summary>
    /// Initializes the tray icon service for the given WinUI window handle.
    /// </summary>
    public void Initialize(IntPtr hwnd, string tooltip = "DiskMaster Pro")
    {
        _hwnd = hwnd;
        if (_hwnd == IntPtr.Zero) return;

        _subclassDelegate = WindowSubclassProc;
        SetWindowSubclass(_hwnd, _subclassDelegate, _subclassId, UIntPtr.Zero);

        AddOrUpdateTrayIcon(tooltip);
    }

    private void AddOrUpdateTrayIcon(string tooltip)
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            // IDI_APPLICATION = 32512
            IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512);

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = tooltip
            };

            if (!_isIconAdded)
            {
                _isIconAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
            }
            else
            {
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
        }
        catch { }
    }

    /// <summary>
    /// Shows a system notification balloon from the tray icon.
    /// </summary>
    public void ShowNotification(string title, string message)
    {
        if (_hwnd == IntPtr.Zero || !_isIconAdded) return;

        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_INFO,
                szInfo = message,
                szInfoTitle = title,
                dwInfoFlags = NIIF_INFO
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    /// <summary>
    /// Minimizes the main window and hides it from the taskbar.
    /// </summary>
    public void MinimizeToTray()
    {
        if (_hwnd == IntPtr.Zero) return;
        ShowWindow(_hwnd, SW_HIDE);
    }

    /// <summary>
    /// Restores the main window and brings it to the foreground.
    /// </summary>
    public void RestoreFromTray()
    {
        if (_hwnd == IntPtr.Zero) return;
        ShowWindow(_hwnd, SW_RESTORE);
        SetForegroundWindow(_hwnd);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            int msg = (int)lParam;
            if (msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK || msg == WM_RBUTTONUP)
            {
                RestoreFromTray();
                RestoreRequested?.Invoke();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isIconAdded && _hwnd != IntPtr.Zero)
        {
            try
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref nid);
                _isIconAdded = false;
            }
            catch { }
        }

        if (_hwnd != IntPtr.Zero && _subclassDelegate != null)
        {
            try
            {
                RemoveWindowSubclass(_hwnd, _subclassDelegate, _subclassId);
            }
            catch { }
        }
    }
}
