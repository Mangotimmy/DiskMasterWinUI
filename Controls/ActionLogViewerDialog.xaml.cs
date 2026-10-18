using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

public sealed partial class ActionLogViewerDialog : ContentDialog
{
    private bool _isInitialized = false;
    private readonly ObservableCollection<ActionLogItem> _displayedItems = new();
    private string _filterText = string.Empty;
    private string _categoryFilter = "ALL";
    private string _levelFilter = "ALL";

    public ObservableCollection<ActionLogItem> DisplayedItems => _displayedItems;

    public ActionLogViewerDialog()
    {
        InitializeComponent();
        _isInitialized = true;

        LogListView.ItemsSource = _displayedItems;

        CategoryFilterCombo.SelectionChanged += Filter_SelectionChanged;
        LevelFilterCombo.SelectionChanged += Filter_SelectionChanged;
        SearchBox.TextChanged += SearchBox_TextChanged;

        ApplyFiltersAndReload();

        DebugLogService.Instance.LogItemAdded += OnLogItemAdded;

        this.Closing += (_, _) =>
        {
            DebugLogService.Instance.LogItemAdded -= OnLogItemAdded;
        };

        this.Unloaded += (_, _) =>
        {
            DebugLogService.Instance.LogItemAdded -= OnLogItemAdded;
        };
    }

    private void OnLogItemAdded(ActionLogItem item)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isInitialized) return;
            if (MatchesFilter(item))
            {
                _displayedItems.Add(item);
                while (_displayedItems.Count > 2000)
                {
                    _displayedItems.RemoveAt(0);
                }
                if (AutoScrollCheck?.IsChecked == true && LogListView != null)
                {
                    LogListView.ScrollIntoView(item);
                }
            }
            UpdateCountStatus();
        });
    }

    private void ApplyFiltersAndReload()
    {
        if (!_isInitialized) return;
        var allItems = DebugLogService.Instance.GetRecentLogItems();
        _displayedItems.Clear();
        foreach (var item in allItems)
        {
            if (MatchesFilter(item))
            {
                _displayedItems.Add(item);
            }
        }
        UpdateCountStatus();
        if (AutoScrollCheck?.IsChecked == true && _displayedItems.Count > 0 && LogListView != null)
        {
            LogListView.ScrollIntoView(_displayedItems[^1]);
        }
    }

    private bool MatchesFilter(ActionLogItem item)
    {
        // Category filter
        if (_categoryFilter != "ALL")
        {
            bool catMatch = _categoryFilter switch
            {
                "Navigation" => item.Category.Contains("Nav", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("導航", StringComparison.OrdinalIgnoreCase),
                "Performance" => item.Category.Contains("Perf", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("效能", StringComparison.OrdinalIgnoreCase),
                "Optimizer" => item.Category.Contains("Opt", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("優化", StringComparison.OrdinalIgnoreCase),
                "Health" => item.Category.Contains("Health", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("健康", StringComparison.OrdinalIgnoreCase),
                "DiskTools" => item.Category.Contains("Disk", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("磁碟", StringComparison.OrdinalIgnoreCase),
                "NetworkTools" => item.Category.Contains("Net", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("網路", StringComparison.OrdinalIgnoreCase),
                "BootManager" => item.Category.Contains("Boot", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("開機", StringComparison.OrdinalIgnoreCase),
                "WimDeploy" => item.Category.Contains("Wim", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("部署", StringComparison.OrdinalIgnoreCase),
                "Privacy" => item.Category.Contains("Priv", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("隱私", StringComparison.OrdinalIgnoreCase),
                "ProcessHelper" => item.Category.Contains("ProcessHelper", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("底層", StringComparison.OrdinalIgnoreCase),
                "CrashGuard" => item.Category.Contains("Crash", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("例外", StringComparison.OrdinalIgnoreCase),
                "UndoCut" => item.Category.Contains("Undo", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("復原", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("剪下", StringComparison.OrdinalIgnoreCase),
                "Layout" => item.Category.Contains("Layout", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("版面", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("Tabs", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("分頁", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("Shortcut", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("快捷", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("工作區", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("Workspace", StringComparison.OrdinalIgnoreCase),
                "OTHER" => !item.Category.Contains("Nav", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Perf", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Opt", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Health", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Disk", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Net", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Boot", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Wim", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Priv", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("ProcessHelper", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Crash", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Undo", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Layout", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Tabs", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Shortcut", StringComparison.OrdinalIgnoreCase) &&
                           !item.Category.Contains("Workspace", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            if (!catMatch) return false;
        }

        // Level filter
        if (_levelFilter != "ALL")
        {
            bool lvlMatch = _levelFilter switch
            {
                "ACTION" => item.Level.Equals("ACTION", StringComparison.OrdinalIgnoreCase),
                "REQUEST" => item.Level.Equals("REQUEST", StringComparison.OrdinalIgnoreCase) || item.Category.Contains("ProcessHelper", StringComparison.OrdinalIgnoreCase),
                "WARN_ERROR" => item.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase) || item.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase),
                "INFO" => item.Level.Equals("INFO", StringComparison.OrdinalIgnoreCase) || item.Level.Equals("DEBUG", StringComparison.OrdinalIgnoreCase) || item.Level.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            if (!lvlMatch) return false;
        }

        // Text search filter
        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            var text = _filterText.Trim();
            bool textMatch = (item.Action?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (item.Details?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (item.Category?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                             (item.Level?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!textMatch) return false;
        }

        return true;
    }

    private void UpdateCountStatus()
    {
        if (CountStatusText == null) return;
        var total = DebugLogService.Instance.LogBufferCount;
        CountStatusText.Text = $"顯示 {_displayedItems.Count:N0} / 總計 {total:N0} 筆";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        _filterText = SearchBox.Text ?? string.Empty;
        ApplyFiltersAndReload();
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (CategoryFilterCombo?.SelectedItem is ComboBoxItem catItem && catItem.Tag is string catTag)
        {
            _categoryFilter = catTag;
        }
        if (LevelFilterCombo?.SelectedItem is ComboBoxItem lvlItem && lvlItem.Tag is string lvlTag)
        {
            _levelFilter = lvlTag;
        }
        ApplyFiltersAndReload();
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var items = LogListView.SelectedItems.OfType<ActionLogItem>().ToList();
        if (items.Count == 0)
        {
            items = _displayedItems.ToList();
        }
        if (items.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.AppendLine(item.FullExportLine);
        }
        try
        {
            var dp = new DataPackage();
            dp.SetText(sb.ToString());
            Clipboard.SetContent(dp);
        }
        catch { }
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_displayedItems.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in _displayedItems)
        {
            sb.AppendLine(item.FullExportLine);
        }
        try
        {
            var dp = new DataPackage();
            dp.SetText(sb.ToString());
            Clipboard.SetContent(dp);
        }
        catch { }
    }

    private void ClearView_Click(object sender, RoutedEventArgs e)
    {
        _displayedItems.Clear();
        UpdateCountStatus();
    }

    private async void ExportFile_Click(object sender, RoutedEventArgs e)
    {
        if (_displayedItems.Count == 0) return;

        try
        {
            var fileName = $"DiskMaster_Actions_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            var path = await FilePickerHelper.PickSaveFileAsync(fileName, ".log", "Log File (*.log)", "匯出操作日誌");
            if (!string.IsNullOrEmpty(path))
            {
                var sb = new StringBuilder();
                foreach (var item in _displayedItems)
                {
                    sb.AppendLine(item.FullExportLine);
                }
                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { }
    }

    private void OpenCmd_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.OpenLiveConsole();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.OpenLogsFolder();
    }

    private void OpenNotepad_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.ViewCurrentLog();
    }
}
