using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using WinRT.Interop;

namespace DiskMasterWinUI.Controls;

/// <summary>
/// Compact, always-available live console that tails DebugLogService's in-memory ring buffer.
/// Producer threads only enqueue items; all UI mutations happen on the dispatcher in batched ticks.
/// </summary>
public sealed partial class LiveConsoleWindow : Window
{
    public static LiveConsoleWindow? CurrentInstance { get; private set; }

    private const int MaxVisibleItems = 2000;
    // Open with the entire current ring buffer, not just the newest 300 rows.
    private const int InitialItems = MaxVisibleItems;
    private const int MaxPendingItems = 5000;

    private readonly ObservableCollection<ActionLogItem> _visibleItems = new();
    private readonly ConcurrentQueue<ActionLogItem> _pending = new();
    private readonly DispatcherQueueTimer _flushTimer;
    private readonly IntPtr _hwnd;

    private string _levelFilter = "ALL";
    private bool _isPaused;
    private bool _isUpdatingFilter;
    private bool _isClosed;

    public LiveConsoleWindow()
    {
        InitializeComponent();
        CurrentInstance = this;
        _hwnd = WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ConsoleTitleBar);
        LogList.ItemsSource = _visibleItems;

        ConfigureWindow();
        ApplyLanguage();
        LoadInitialItems();

        _flushTimer = DispatcherQueue.CreateTimer();
        _flushTimer.Interval = TimeSpan.FromMilliseconds(150);
        _flushTimer.IsRepeating = true;
        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();

        DebugLogService.Instance.LogItemAdded += OnLogItemAdded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        this.Closed += (_, _) =>
        {
            _isClosed = true;
            DebugLogService.Instance.LogItemAdded -= OnLogItemAdded;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _flushTimer.Stop();
            if (ReferenceEquals(CurrentInstance, this))
            {
                CurrentInstance = null;
            }
        };
    }

    public static void Toggle()
    {
        if (CurrentInstance != null)
        {
            try { CurrentInstance.Close(); } catch { }
            CurrentInstance = null;
            return;
        }

        try
        {
            var window = new LiveConsoleWindow();
            window.Activate();
            DebugLogService.Instance.LogAction("使用者介面 (UI)", "開啟即時主控台迷你視窗");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Error("即時主控台視窗啟動失敗", ex, "LiveConsole");
            CurrentInstance = null;
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = false;
                presenter.IsResizable = true;
                presenter.IsMinimizable = true;
                presenter.IsMaximizable = true;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                try { appWindow.SetIcon(iconPath); } catch { }
            }

            double dpiScale = Math.Max(1.0, DisplayHelper.GetDpi(_hwnd) / 96.0);
            int width = (int)Math.Round(780 * dpiScale);
            int height = (int)Math.Round(460 * dpiScale);
            try
            {
                appWindow.ResizeClient(new SizeInt32(width, height));
            }
            catch
            {
                appWindow.Resize(new SizeInt32(width, height));
            }

            var workArea = DisplayHelper.GetWorkArea(_hwnd);
            int x;
            int y;

            var main = DiskMasterWinUI.MainWindow.CurrentInstance;
            if (main != null)
            {
                var mainHwnd = WindowNative.GetWindowHandle(main);
                var mainWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(mainHwnd));
                x = mainWindow.Position.X + Math.Max(0, (mainWindow.Size.Width - width) / 2);
                y = mainWindow.Position.Y + Math.Max(0, (mainWindow.Size.Height - height) / 2);
            }
            else
            {
                x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
                y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            }

            x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - width));
            y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - height));
            appWindow.Move(new PointInt32(x, y));
        }
        catch { }
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(ApplyLanguage);
    }

    private void ApplyLanguage()
    {
        if (_isClosed) return;

        var loc = LocalizationService.Instance;
        string title = loc.CurrentLanguage switch
        {
            "zh-CN" => "DiskMaster Pro — 实时控制台 (Live Console)",
            "ja-JP" => "DiskMaster Pro — ライブコンソール (Live Console)",
            "en-US" => "DiskMaster Pro — Live Console",
            _ => "DiskMaster Pro — 即時主控台 (Live Console)"
        };
        Title = title;
        ConsoleTitleBar.Title = title;

        PauseText.Text = _isPaused
            ? LocalizationService.T("繼續", "继续", "Resume", "再開")
            : LocalizationService.T("暫停", "暂停", "Pause", "一時停止");
        PauseIcon.Glyph = _isPaused ? "\uE768" : "\uE769";

        AutoScrollCheck.Content = LocalizationService.T("自動滾動", "自动滚动", "Auto-scroll", "自動スクロール");
        CopyBtn.Content = LocalizationService.T("複製", "复制", "Copy", "コピー");
        ClearBtn.Content = LocalizationService.T("清空", "清空", "Clear", "クリア");
        OpenFolderBtn.Content = LocalizationService.T("日誌資料夾", "日志文件夹", "Log Folder", "ログフォルダー");
        OpenCmdBtn.Content = LocalizationService.T("外部 CMD", "外部 CMD", "External CMD", "外部 CMD");
        EmptyStateText.Text = LocalizationService.T("目前尚無日誌紀錄", "当前尚无日志记录", "No log entries yet", "ログはまだありません");

        BuildLevelFilterItems();
        UpdateEmptyState();
        UpdateFooter();
    }

    private void BuildLevelFilterItems()
    {
        if (LevelFilterCombo == null) return;

        _isUpdatingFilter = true;
        try
        {
            LevelFilterCombo.Items.Clear();
            AddFilterItem("ALL", LocalizationService.T("全部等級", "全部等级", "All levels", "すべてのレベル"));
            AddFilterItem("WARN_ERROR", LocalizationService.T("錯誤與警告", "错误与警告", "Errors & warnings", "エラーと警告"));
            AddFilterItem("REQUEST", LocalizationService.T("請求", "请求", "Requests", "リクエスト"));
            AddFilterItem("ACTION", LocalizationService.T("操作", "操作", "Actions", "操作"));
            AddFilterItem("OUTPUT", LocalizationService.T("CMD 輸出", "CMD 输出", "CMD output", "CMD 出力"));

            if (LevelFilterCombo.Items.Count > 0)
            {
                LevelFilterCombo.SelectedIndex = 0;
                for (int i = 0; i < LevelFilterCombo.Items.Count; i++)
                {
                    if (LevelFilterCombo.Items[i] is ComboBoxItem item && item.Tag is string tag && tag == _levelFilter)
                    {
                        LevelFilterCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        finally
        {
            _isUpdatingFilter = false;
        }
    }

    private void AddFilterItem(string tag, string text)
    {
        LevelFilterCombo.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
    }

    private bool MatchesFilter(ActionLogItem item)
    {
        return _levelFilter switch
        {
            "WARN_ERROR" => item.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase) ||
                            item.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase),
            "REQUEST" => item.Level.Equals("REQUEST", StringComparison.OrdinalIgnoreCase),
            "ACTION" => item.Level.Equals("ACTION", StringComparison.OrdinalIgnoreCase),
            "OUTPUT" => item.Level.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void OnLogItemAdded(ActionLogItem item)
    {
        if (_isClosed) return;
        _pending.Enqueue(item);
        while (_pending.Count > MaxPendingItems)
        {
            _pending.TryDequeue(out _);
        }
    }

    private void FlushPending()
    {
        if (_isClosed) return;

        bool changed = false;
        while (_pending.TryDequeue(out var item))
        {
            if (_isPaused || !MatchesFilter(item)) continue;
            _visibleItems.Add(item);
            changed = true;
        }

        if (_visibleItems.Count > MaxVisibleItems)
        {
            while (_visibleItems.Count > MaxVisibleItems)
            {
                _visibleItems.RemoveAt(0);
            }
            changed = true;
        }

        if (!changed) return;

        if (AutoScrollCheck?.IsChecked == true && _visibleItems.Count > 0)
        {
            try { LogList.ScrollIntoView(_visibleItems[^1]); } catch { }
        }

        UpdateEmptyState();
        UpdateFooter();
    }

    private void LoadInitialItems()
    {
        _visibleItems.Clear();
        var recent = DebugLogService.Instance.GetRecentLogItems();
        int start = Math.Max(0, recent.Count - InitialItems);
        for (int i = start; i < recent.Count; i++)
        {
            if (MatchesFilter(recent[i])) AddVisible(recent[i]);
        }
        UpdateEmptyState();
        UpdateFooter();
    }

    private void ReloadFromBuffer()
    {
        _visibleItems.Clear();
        while (_pending.TryDequeue(out _)) { }

        var recent = DebugLogService.Instance.GetRecentLogItems();
        int start = Math.Max(0, recent.Count - MaxVisibleItems);
        for (int i = start; i < recent.Count; i++)
        {
            if (MatchesFilter(recent[i])) AddVisible(recent[i]);
        }

        if (AutoScrollCheck?.IsChecked == true && _visibleItems.Count > 0)
        {
            try { LogList.ScrollIntoView(_visibleItems[^1]); } catch { }
        }

        UpdateEmptyState();
        UpdateFooter();
    }

    private void AddVisible(ActionLogItem item)
    {
        _visibleItems.Add(item);
        while (_visibleItems.Count > MaxVisibleItems)
        {
            _visibleItems.RemoveAt(0);
        }
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        ApplyLanguage();
        if (!_isPaused) ReloadFromBuffer();
    }

    private void LevelFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFilter) return;
        if (LevelFilterCombo?.SelectedItem is ComboBoxItem item && item.Tag is string filter && filter != _levelFilter)
        {
            _levelFilter = filter;
            ReloadFromBuffer();
        }
    }

    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleItems.Count == 0) return;

        var builder = new StringBuilder();
        foreach (var item in _visibleItems)
        {
            builder.AppendLine(item.FullExportLine);
        }

        try
        {
            var package = new DataPackage();
            package.SetText(builder.ToString());
            Clipboard.SetContent(package);
        }
        catch { }
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        _visibleItems.Clear();
        while (_pending.TryDequeue(out _)) { }
        UpdateEmptyState();
        UpdateFooter();
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.OpenLogsFolder();
    }

    private void OpenCmdBtn_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.OpenLiveConsole();
    }

    private void UpdateEmptyState()
    {
        if (EmptyStateText != null)
        {
            EmptyStateText.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateFooter()
    {
        if (FooterStateText == null || BufferCountText == null) return;

        FooterStateText.Text = _isPaused
            ? LocalizationService.T("已暫停接收（繼續後會重新載入最近紀錄）", "已暂停接收（继续后会重新载入最近记录）", "Paused (recent entries reload on resume)", "一時停止中（再開時に最近のログを再読み込み）")
            : LocalizationService.T("即時接收中", "实时接收中", "Live", "ライブ受信中");

        BufferCountText.Text = string.Format(
            LocalizationService.T("顯示 {0} / 緩衝 {1} 筆", "显示 {0} / 缓冲 {1} 条", "{0} shown / {1} buffered", "表示 {0} / バッファー {1} 件"),
            _visibleItems.Count,
            DebugLogService.Instance.LogBufferCount);
    }
}
