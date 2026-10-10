using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service managing native Windows Toast Notifications and Windows Notification Center / Action Center list integration.
/// Automatically registers AUMID (DiskMaster.Pro.Flagship) in HKCU registry and Start Menu shortcut property store.
/// </summary>
public class WindowsNotificationService
{
    public const string AppUserModelId = "DiskMaster.Pro.Flagship";
    private static readonly Lazy<WindowsNotificationService> _instance = new(() => new WindowsNotificationService());
    public static WindowsNotificationService Instance => _instance.Value;

    private Action<string?>? _onNavigate;
    private Action<string, string>? _trayFallback;
    private bool _isRegistered;
    private string _iconPath = "";

    private WindowsNotificationService()
    {
    }

    /// <summary>
    /// Initializes the notification service with window navigation and tray fallback callbacks.
    /// </summary>
    public void Initialize(Action<string?> onNavigate, Action<string, string>? trayFallback = null)
    {
        _onNavigate = onNavigate;
        _trayFallback = trayFallback;

        try
        {
            _iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            EnsureAppUserModelIdRegistration();
            _isRegistered = true;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Error("Failed to initialize WindowsNotificationService", ex, "Notification");
        }
    }

    /// <summary>
    /// Registers HKCU\Software\Classes\AppUserModelId\DiskMaster.Pro.Flagship and Start Menu shortcut.
    /// </summary>
    private void EnsureAppUserModelIdRegistration()
    {
        try
        {
            // 1. Register in HKCU for immediate Windows Settings and Action Center recognition
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}", writable: true);
            if (key != null)
            {
                key.SetValue("DisplayName", "DiskMaster Pro 旗艦版");
                if (File.Exists(_iconPath))
                {
                    key.SetValue("IconUri", _iconPath);
                }
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
            }
        }
        catch { }

        // 2. Ensure Start Menu shortcut carries PKEY_AppUserModel_ID
        try
        {
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DiskMaster Pro");
            if (!Directory.Exists(startMenuDir))
            {
                Directory.CreateDirectory(startMenuDir);
            }

            string shortcutPath = Path.Combine(startMenuDir, "DiskMaster Pro.lnk");
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "DiskMasterWinUI.exe");

            CreateOrUpdateShortcutWithAumid(shortcutPath, exePath, AppContext.BaseDirectory, _iconPath, AppUserModelId);
        }
        catch { }
    }

    /// <summary>
    /// Dispatches a rich notification into the Windows Toast & Action Center list.
    /// </summary>
    public void ShowNotification(string title, string message, string? targetTab = null)
    {
        bool useCenter = SettingsService.Instance.Current.EnableWindowsNotificationCenter;

        if (!useCenter || !_isRegistered)
        {
            _trayFallback?.Invoke(title, message);
            return;
        }

        try
        {
            // Play custom or system audio scheme asynchronously
            AudioFeedbackService.PlaySuccess();

            string iconXml = "";
            if (File.Exists(_iconPath))
            {
                var iconUri = new Uri(_iconPath).AbsoluteUri;
                iconXml = $@"<image placement=""appLogoOverride"" src=""{iconUri}"" hint-crop=""circle""/>";
            }

            string xmlPayload = $@"
<toast scenario=""default"" launch=""tab={(targetTab ?? "Default")}"">
    <visual>
        <binding template=""ToastGeneric"">
            <text>{System.Security.SecurityElement.Escape(title)}</text>
            <text>{System.Security.SecurityElement.Escape(message)}</text>
            {iconXml}
        </binding>
    </visual>
    <audio silent=""true""/>
</toast>";

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlPayload);

            var toast = new ToastNotification(xmlDoc)
            {
                Tag = targetTab ?? "DiskMaster",
                Group = "DiskMasterPro",
                ExpirationTime = DateTimeOffset.Now.AddDays(3)
            };

            toast.Activated += (s, e) =>
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    _onNavigate?.Invoke(targetTab);
                });
            };

            var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            notifier.Show(toast);
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"Toast dispatch failed, falling back to tray balloon: {ex.Message}", "Notification");
            _trayFallback?.Invoke(title, message);
        }
    }

    #region Shell Shortcut COM Interop with PKEY_AppUserModel_ID

    private static void CreateOrUpdateShortcutWithAumid(string shortcutPath, string targetPath, string workingDir, string iconPath, string aumid)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDir);
            if (File.Exists(iconPath))
            {
                link.SetIconLocation(iconPath, 0);
            }
            link.SetDescription("DiskMaster Pro - Advanced Windows Disk & Deployment Manager");

            var propertyStore = (IPropertyStore)link;
            var key = new PROPERTYKEY
            {
                fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                pid = 5
            };

            var pv = new PROPVARIANT();
            pv.Init(aumid);
            propertyStore.SetValue(ref key, ref pv);
            propertyStore.Commit();

            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, true);
        }
        catch { }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        uint GetCount(out uint cProps);
        uint GetAt(uint iProp, out PROPERTYKEY pkey);
        uint GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        uint SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        uint Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;

        public void Init(string val)
        {
            vt = 31; // VT_LPWSTR
            pwszVal = Marshal.StringToCoTaskMemUni(val);
        }
    }

    #endregion
}
