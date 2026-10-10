using System.IO;
using System.Runtime.InteropServices;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service managing the Windows notification area (System Tray) icon, minimizing to background,
/// restoring the application window, and providing Win32 right-click Quick Action tasks.
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
    private const int SW_RESTORE = 9;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconAdded;
    private SubclassWndProc? _subclassDelegate;
    private readonly UIntPtr _subclassId = new UIntPtr(4096);

    public event Action? RestoreRequested;
    public event Action<string>? QuickActionRequested;

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
            // Load sharp custom AppIcon.ico (16x16) for high-DPI tray notification area
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            }
            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // fallback
            }

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
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

    private void ShowTrayContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            AppendMenu(hMenu, MF_STRING, (UIntPtr)101, "🟢 開啟 DiskMaster Pro");
            AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, (UIntPtr)102, "🚀 簡易體檢模式 (Easy Mode)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)103, "📊 即時效能分析 (Performance)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)104, "🌐 網路工具與診斷 (Network)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)105, "💾 磁碟工具與救援 (Disk Tools)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)106, "🛠️ 系統修復 DISM/SFC (Health)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)107, "🧹 一鍵清理快取 (Clean Cache)");
            AppendMenu(hMenu, MF_STRING, (UIntPtr)108, "⚙️ 偏好設定 (Settings)");
            AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenu(hMenu, MF_STRING, (UIntPtr)109, "❌ 結束程式 (Exit)");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);

            uint selected = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);

            switch (selected)
            {
                case 101:
                    RestoreFromTray();
                    RestoreRequested?.Invoke();
                    break;
                case 102:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("EasyMode");
                    break;
                case 103:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("SystemPerformance");
                    break;
                case 104:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("NetworkTools");
                    break;
                case 105:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("DiskTools");
                    break;
                case 106:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("SystemHealth");
                    break;
                case 107:
                    QuickActionRequested?.Invoke("CleanCache");
                    break;
                case 108:
                    RestoreFromTray();
                    QuickActionRequested?.Invoke("Settings");
                    break;
                case 109:
                    QuickActionRequested?.Invoke("Exit");
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            int msg = (int)lParam;
            if (msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK)
            {
                RestoreFromTray();
                RestoreRequested?.Invoke();
                return IntPtr.Zero;
            }
            else if (msg == WM_RBUTTONUP)
            {
                ShowTrayContextMenu();
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

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
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
