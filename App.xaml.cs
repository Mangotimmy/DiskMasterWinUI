using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private static System.Threading.Mutex? s_singleInstanceMutex;
    
    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        try
        {
            s_singleInstanceMutex = new System.Threading.Mutex(true, @"Global\DiskMasterWinUI_SingleInstanceMutex", out _);
        }
        catch { }

        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
        catch { }

        // Initialize DebugLogService from persistent settings
        try
        {
            Services.DebugLogService.Instance.Initialize(Services.SettingsService.Instance.Current.EnableDebugLogging);
        }
        catch { }

        // 1. UI Thread unhandled exceptions
        this.UnhandledException += (s, e) =>
        {
            Services.DebugLogService.Instance.Error($"[WinUI UnhandledException] {e.Message}", e.Exception, "CrashGuard");
            GlobalExceptionHandler.ReportException(e.Exception, $"[WinUI UnhandledException] {e.Message}");
            e.Handled = true;
        };

        // 2. Background Task unobserved exceptions (prevents app death on faulted Task)
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Services.DebugLogService.Instance.Error("[TaskScheduler UnobservedTaskException]", e.Exception, "CrashGuard");
            GlobalExceptionHandler.ReportException(e.Exception, "[TaskScheduler UnobservedTaskException]");
            e.SetObserved();
        };

        // 3. AppDomain general exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Services.DebugLogService.Instance.Error("[AppDomain UnhandledException]", ex, "CrashGuard");
                GlobalExceptionHandler.ReportException(ex, "[AppDomain UnhandledException]");
            }
        };

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            GlobalExceptionHandler.Initialize(_window.DispatcherQueue);
            _window.Activate();
        }
        catch (Exception ex)
        {
            var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(logPath, $"[OnLaunched Exception] {DateTime.Now}\n{ex}");
            throw;
        }
    }
}
