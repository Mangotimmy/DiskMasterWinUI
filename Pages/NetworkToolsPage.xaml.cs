using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Dispatching;
using System.ComponentModel;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class NetworkToolsPage : Page
{
    public NetworkToolsViewModel ViewModel { get; } = new();

    // Hosts editor: typing only restarts this debounce timer; the document is parsed after the
    // user pauses so no per-keystroke work ever runs on the UI thread.
    private DispatcherQueueTimer? _hostsEditorDebounce;
    private bool _isPushingHostsText;

    public NetworkToolsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        NavigationCacheMode = NavigationCacheMode.Required;
        DiskMasterWinUI.Helpers.TabReorderHelper.Attach(NetTabView);
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;

        // The editor text is exchanged through these two hooks instead of a two-way binding on
        // TextBox.Text, so a keystroke never round-trips the whole document through the ViewModel.
        ViewModel.HostsEditorTextProvider = () => HostsEditorTextBox.Text;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        RefreshNetBtnText.Text = lang switch
        {
            "zh-CN" => "刷新网络状态",
            "en-US" => "Refresh Network Status",
            "ja-JP" => "ネットワーク状態を更新",
            _ => "重新整理網路狀態"
        };

        TabUpnp.Header = lang switch
        {
            "zh-CN" => "🔀 UPnP 端口转发",
            "en-US" => "🔀 UPnP Port Forwarding",
            "ja-JP" => "🔀 UPnP ポート転送",
            _ => "🔀 UPnP 自動通訊埠轉發"
        };

        TabDns.Header = lang switch
        {
            "zh-CN" => "🌐 推荐 DNS 设置",
            "en-US" => "🌐 Recommended DNS",
            "ja-JP" => "🌐 推奨 DNS 設定",
            _ => "🌐 推薦 DNS 設定"
        };

        TabDiagnostic.Header = lang switch
        {
            "zh-CN" => "🩺 智能卡顿诊断",
            "en-US" => "🩺 Smart Diagnostics",
            "ja-JP" => "🩺 スマート診断",
            _ => "🩺 智慧連線卡頓診斷"
        };

        TabHosts.Header = lang switch
        {
            "zh-CN" => "📝 Hosts 文件编辑器",
            "en-US" => "📝 Hosts File Editor",
            "ja-JP" => "📝 Hosts エディタ",
            _ => "📝 Hosts 檔案編輯器"
        };

        TabNetShare.Header = lang switch
        {
            "zh-CN" => "🖧 局域网共享 (NetShare)",
            "en-US" => "🖧 LAN File Share (NetShare)",
            "ja-JP" => "🖧 共有フォルダ (NetShare)",
            _ => "🖧 網路共用精靈 (NetShare)"
        };

        TabRdp.Header = lang switch
        {
            "zh-CN" => "🖥️ 远程桌面 (RDP)",
            "en-US" => "🖥️ Remote Desktop (RDP)",
            "ja-JP" => "🖥️ リモートデスクトップ (RDP)",
            _ => "🖥️ 遠端桌面 (RDP Server)"
        };

        TabCliTools.Header = lang switch
        {
            "zh-CN" => "⚡ CMD 网络工具箱",
            "en-US" => "⚡ CMD Network Toolkit",
            "ja-JP" => "⚡ CMD ネットワークツール",
            _ => "⚡ CMD 網路工具箱"
        };
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        // Re-activation: clears any cancellation left behind by Unloaded and (re)starts the load
        // exactly once. Skipped work is retried here, so data is never stale after a tab switch.
        ViewModel.OnPageActivated();
        RunObserved(ViewModel.InitializeAsync(), "NetworkToolsPage.InitializeAsync");
    }

    /// <summary>
    /// The page is cached (NavigationCacheMode.Required). When it leaves the visual tree every
    /// page-scoped refresh loop / timer must stop, otherwise a cached page keeps touching the UI
    /// thread after the user has moved on (this was one of the freeze contributors).
    /// </summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnPageDeactivated();
    }

    /// <summary>
    /// Fire-and-forget with an explicit fault report: background work started from a UI event must
    /// never fail silently, so every fault goes to the global exception handler.
    /// </summary>
    private static async void RunObserved(Task task, string context)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            DiskMasterWinUI.Helpers.GlobalExceptionHandler.ReportException(ex, context);
        }
    }

    private void PresetMinecraft_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 25565, InternalPort = 25565, Protocol = "TCP", Description = "Minecraft Java Server"
        });
    }

    private void PresetSteam_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 27015, InternalPort = 27015, Protocol = "UDP", Description = "Steam CS2 / TF2 Server"
        });
    }

    private void PresetTerraria_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 7777, InternalPort = 7777, Protocol = "TCP", Description = "Terraria Multiplayer"
        });
    }

    private void PresetPalworld_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 8211, InternalPort = 8211, Protocol = "UDP", Description = "Palworld Dedicated Server"
        });
    }

    private void PresetRdp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 3389, InternalPort = 3389, Protocol = "TCP", Description = "Windows Remote Desktop (RDP)"
        });
    }

    private void PresetPlex_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 32400, InternalPort = 32400, Protocol = "TCP", Description = "Plex Media Server"
        });
    }

    private async void DeletePortMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is UpnpPortMappingItem item)
        {
            await ViewModel.DeletePortMappingCommand.ExecuteAsync(item);
        }
    }


    private void ShareList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is NetShareItem share)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer,
                $"{share.ShareName}, {share.AccessSummary}, {share.CachingMode}");
        }
    }

    private async void NewShare_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiskMasterWinUI.Controls.ShareEditorDialog
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
        if (dialog.Saved)
        {
            await ViewModel.RefreshSharesAsync();
            await DiskMasterWinUI.Helpers.DialogHelper.ShowOperationResultAsync(
                XamlRoot, "Share created", dialog.ResultMessage, true);
        }
    }

    private async void ManageShare_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNetShare == null) return;
        var dialog = new DiskMasterWinUI.Controls.ShareEditorDialog(ViewModel.SelectedNetShare)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
        if (dialog.Saved)
        {
            await ViewModel.RefreshSharesAsync();
            await ViewModel.LoadSelectedShareDetailsAsync();
            await DiskMasterWinUI.Helpers.DialogHelper.ShowOperationResultAsync(
                XamlRoot, "Share updated", dialog.ResultMessage, true);
        }
    }

    private async void DeleteSelectedShare_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNetShare == null) return;
        await ViewModel.DeleteShareAsync(ViewModel.SelectedNetShare);
        ViewModel.SelectedNetShare = null;
    }

    private async void TroubleshootShare_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiskMasterWinUI.Controls.ShareTroubleshootingDialog(ViewModel)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void DeleteShare_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is NetShareItem item)
        {
            await ViewModel.DeleteShareCommand.ExecuteAsync(item);
        }
    }

    private async void DisconnectOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OpenSharedFileItem item)
        {
            await ViewModel.DisconnectOpenFileCommand.ExecuteAsync(item);
        }
    }

    private async void OpenSmartDiagDialog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Controls.SmartNetworkDiagnosticDialog
        {
            XamlRoot = this.XamlRoot
        };
        await dlg.ShowAsync();
    }

    private async void NetstatPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int port))
        {
            await ViewModel.ApplyPortPresetAsync(port);
        }
    }

    private async void TcpLevelPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string level && !string.IsNullOrWhiteSpace(level))
        {
            await ViewModel.ApplyTcpAutoTuningLevelAsync(level);
        }
    }

    public void LoadShareFolderTarget(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        try
        {
            // 七個工具分頁已收進三個類別的第二層 TabView：先切到「檔案與系統」類別，
            // 再選取其中的「網路共用」工具分頁，最後才載入指定的共用資料夾。
            NetTabView.SelectedItem = TabCategoryFiles;
            FileSystemTabView.SelectedItem = TabNetShare;
            ViewModel.LoadShareFolderTarget(folderPath);
        }
        catch { }
    }

    private async void KillNetstatRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int pid && pid > 0)
        {
            await ViewModel.KillProcessPidAsync(pid);
        }
    }

    private async void DisconnectSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RdpSessionItem item)
        {
            await ViewModel.DisconnectSessionCommand.ExecuteAsync(item);
        }
    }

    private async void LogoffSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RdpSessionItem item)
        {
            await ViewModel.LogoffSessionCommand.ExecuteAsync(item);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Hosts editor: Notepad-smooth plumbing
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Loads a document pushed by the ViewModel (initial load / refresh / format / deduplicate /
    /// restore). Explicit user actions only — never part of a keystroke.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NetworkToolsViewModel.RawHostsText)) return;
        if (HostsEditorTextBox == null) return;

        string text = ViewModel.RawHostsText ?? "";
        // One full copy of the document is created by this comparison; acceptable for an explicit
        // action, which is exactly why it is not on the typing path.
        if (HostsEditorTextBox.Text == text) return;

        _isPushingHostsText = true;
        try
        {
            HostsEditorTextBox.Text = text;
            HostsEditorTextBox.SelectionStart = 0;
        }
        finally
        {
            _isPushingHostsText = false;
        }

        ScheduleHostsEditorRefresh();
    }

    /// <summary>
    /// Per-keystroke path: O(1) and allocation-free. TextBox.Text is deliberately NOT read here
    /// (every read marshals a full managed copy of the document out of the control), the document
    /// is never split or scanned, and the ViewModel is notified only when its state actually flips.
    /// </summary>
    private void HostsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isPushingHostsText && !ViewModel.IsModifiedUnsaved)
        {
            ViewModel.IsModifiedUnsaved = true;
            ViewModel.SaveStatusDisplay = "🟡 已修改未儲存";
        }

        ScheduleHostsEditorRefresh();
    }

    /// <summary>
    /// O(log n) caret mapping against the cached line index — the previous implementation walked
    /// every character from index 0 to the caret on every caret move and every keystroke.
    /// </summary>
    private void HostsTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            ViewModel.UpdateCursorPosition(tb.SelectionStart);
        }
    }

    /// <summary>Restarts the debounce timer. The parse/highlight work only runs once typing pauses.</summary>
    private void ScheduleHostsEditorRefresh()
    {
        var timer = _hostsEditorDebounce;
        if (timer == null)
        {
            var queue = DiskMasterWinUI.Helpers.DispatcherHelper.UIDispatcher ?? DispatcherQueue.GetForCurrentThread();
            if (queue == null) return;

            timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.IsRepeating = false;
            timer.Tick += HostsEditorDebounce_Tick;
            _hostsEditorDebounce = timer;
        }

        timer.Stop();
        timer.Start();
    }

    private void HostsEditorDebounce_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        string snapshot = HostsEditorTextBox.Text; // one copy, only after the user stopped typing
        RunObserved(ViewModel.ApplyEditorSnapshotAsync(snapshot), "NetworkToolsPage.HostsEditorDebounce");
    }

    private void ToggleWordWrap_Click(object sender, RoutedEventArgs e)
    {
        if (HostsEditorTextBox != null)
        {
            HostsEditorTextBox.TextWrapping = HostsEditorTextBox.TextWrapping == TextWrapping.Wrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
            ViewModel.IsWordWrapEnabled = HostsEditorTextBox.TextWrapping == TextWrapping.Wrap;
        }
    }

    private void CopyAllHosts_Click(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(HostsEditorTextBox.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ViewModel.StatusMessage = "已將全部 Hosts 內容複製到剪貼簿。";
        AudioFeedbackService.PlaySuccess();
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HostsBackupItem item)
        {
            await ViewModel.RestoreHostsBackupCommand.ExecuteAsync(item);
        }
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HostsBackupItem item)
        {
            await ViewModel.DeleteHostsBackupCommand.ExecuteAsync(item);
        }
    }

    private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DiskMaster", "HostsBackups");
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}
