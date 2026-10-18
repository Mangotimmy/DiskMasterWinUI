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
    public const string SingleInstanceMutexName = @"Global\DiskMasterWinUI_SingleInstanceMutex";
    public const string ActivateInstanceMessageName = "DiskMasterWinUI_ActivateInstance_Msg";
    public static readonly uint WM_ACTIVATE_INSTANCE = RegisterWindowMessage(ActivateInstanceMessageName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);
    private const uint MSGFLT_ADD = 1;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

    /// <summary>
    /// Path passed by the Explorer "Recover with DiskMaster" shell verb (--recover "&lt;path&gt;").
    /// Consumed by MainWindow when it opens the Recovery page.
    /// </summary>
    public static string? PendingRecoverPath { get; set; }
    
    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        try
        {
            if (WM_ACTIVATE_INSTANCE != 0)
            {
                ChangeWindowMessageFilter(WM_ACTIVATE_INSTANCE, MSGFLT_ADD);
            }
        }
        catch { }

        bool isNewInstance = true;
        try
        {
            s_singleInstanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            isNewInstance = true;
        }
        catch
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            try
            {
                PostMessage(HWND_BROADCAST, WM_ACTIVATE_INSTANCE, IntPtr.Zero, IntPtr.Zero);

                int currentPid = Environment.ProcessId;
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("DiskMasterWinUI"))
                {
                    if (proc.Id != currentPid)
                    {
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(proc.MainWindowHandle);
                            PostMessage(proc.MainWindowHandle, WM_ACTIVATE_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                        }
                        else
                        {
                            EnumWindows((hWnd, lParam) =>
                            {
                                GetWindowThreadProcessId(hWnd, out uint windowPid);
                                if (windowPid == proc.Id)
                                {
                                    var sb = new System.Text.StringBuilder(256);
                                    GetWindowText(hWnd, sb, 256);
                                    string title = sb.ToString();
                                    if (title.Contains("DiskMaster", StringComparison.OrdinalIgnoreCase))
                                    {
                                        ShowWindow(hWnd, SW_RESTORE);
                                        SetForegroundWindow(hWnd);
                                        PostMessage(hWnd, WM_ACTIVATE_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                                        return false; // Break enumeration once primary window is found
                                    }
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                        break;
                    }
                }
            }
            catch { }

            Environment.Exit(0);
            return;
        }

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
            try
            {
                SetCurrentProcessExplicitAppUserModelID("DiskMaster.Pro.Flagship");
            }
            catch { }

            var cmd = Environment.GetCommandLineArgs();
            bool startInBackground = false;
            string? tabTag = null;
            string? ntfsPath = null;
            string? sharePath = null;
            string? imagePath = null;
            string? drivePath = null;

            for (int i = 0; i < cmd.Length; i++)
            {
                if (cmd[i].Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                    cmd[i].Equals("/background", StringComparison.OrdinalIgnoreCase) ||
                    cmd[i].Equals("-background", StringComparison.OrdinalIgnoreCase) ||
                    cmd[i].Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    cmd[i].Equals("/minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startInBackground = true;
                }
                else if (cmd[i] == "--tab" && i + 1 < cmd.Length)
                {
                    tabTag = cmd[++i];
                }
                else if (cmd[i] == "--recover" && i + 1 < cmd.Length)
                {
                    PendingRecoverPath = cmd[++i];
                }
                else if (cmd[i] == "--path" && i + 1 < cmd.Length)
                {
                    ntfsPath = cmd[++i];
                }
                else if (cmd[i] == "--share" && i + 1 < cmd.Length)
                {
                    sharePath = cmd[++i];
                }
                else if (cmd[i] == "--image" && i + 1 < cmd.Length)
                {
                    imagePath = cmd[++i];
                }
                else if (cmd[i] == "--drive" && i + 1 < cmd.Length)
                {
                    drivePath = cmd[++i];
                }
            }

            _window = new MainWindow();
            GlobalExceptionHandler.Initialize(_window.DispatcherQueue);
            _window.Activate();

            if (startInBackground && string.IsNullOrEmpty(PendingRecoverPath) && string.IsNullOrEmpty(ntfsPath) && string.IsNullOrEmpty(sharePath) && string.IsNullOrEmpty(imagePath) && string.IsNullOrEmpty(drivePath) && string.IsNullOrEmpty(tabTag))
            {
                _window.MinimizeToTrayOnStartup();
            }

            _ = Services.JumpListService.InitializeAsync();

            if (!string.IsNullOrEmpty(PendingRecoverPath))
            {
                var target = PendingRecoverPath;
                PendingRecoverPath = null;
                _window.NavigateToTag("Recovery");
                if (_window.GetOrCreatePageForTag("Recovery") is DiskMasterWinUI.Pages.RecoveryPage recoveryPage)
                {
                    recoveryPage.LoadTarget(target);
                }
            }
            else if (!string.IsNullOrEmpty(ntfsPath))
            {
                _window.NavigateToTag("NtfsPermissions");
                if (_window.GetOrCreatePageForTag("NtfsPermissions") is DiskMasterWinUI.Pages.NtfsPermissionsPage ntfsPage)
                {
                    ntfsPage.ViewModel.TargetPath = ntfsPath;
                }
            }
            else if (!string.IsNullOrEmpty(sharePath))
            {
                _window.NavigateToTag("NetworkTools");
                if (_window.GetOrCreatePageForTag("NetworkTools") is DiskMasterWinUI.Pages.NetworkToolsPage netPage)
                {
                    netPage.LoadShareFolderTarget(sharePath);
                }
            }
            else if (!string.IsNullOrEmpty(imagePath))
            {
                _window.NavigateToTag("WimDeploy");
                if (_window.GetOrCreatePageForTag("WimDeploy") is DiskMasterWinUI.Pages.WimDeployPage wimPage)
                {
                    wimPage.ViewModel.ImagePath = imagePath;
                    _ = wimPage.ViewModel.LoadImageInfoCommand.ExecuteAsync(null);
                }
            }
            else if (!string.IsNullOrEmpty(drivePath))
            {
                _window.NavigateToTag("DiskTools");
                if (_window.GetOrCreatePageForTag("DiskTools") is DiskMasterWinUI.Pages.DiskToolsPage diskPage)
                {
                    diskPage.ViewModel.ChkdskDrive = drivePath;
                    diskPage.ViewModel.OptimizeDriveLetter = drivePath;
                }
            }
            else if (!string.IsNullOrEmpty(tabTag))
            {
                _window.NavigateToTag(tabTag);
            }
        }
        catch (Exception ex)
        {
            // Never let the diagnostic write replace the real startup failure.
            try
            {
                var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
                System.IO.File.WriteAllText(logPath, $"[OnLaunched Exception] {DateTime.Now}\n{ex}");
            }
            catch { }
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);
}
