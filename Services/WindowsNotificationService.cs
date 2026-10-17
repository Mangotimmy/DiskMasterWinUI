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
    /// Registers HKCU\Software\Classes\AppUserModelId\DiskMaster.Pro.Flagship for native Windows 10/11 Toast & Action Center support.
    /// Does not write .lnk files into Start Menu at runtime so Portable builds remain clean and never trigger AV behavioral heuristics.
    /// </summary>
    private void EnsureAppUserModelIdRegistration()
    {
        try
        {
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
}
