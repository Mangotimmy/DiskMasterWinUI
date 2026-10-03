using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class EasyModePage : Page
{
    public EasyModeViewModel ViewModel { get; } = new();
    private bool _hasLoadedOnce;

    public EasyModePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        this.Unloaded += (_, _) => SaveLayoutSplitters();
    }

    private void RestoreLayoutSplitters()
    {
        try
        {
            var sizes = SettingsService.Instance.Current.LayoutSplitterSizes;
            if (sizes != null)
            {
                if (sizes.TryGetValue("EasyMode_ColDisks", out var colW) && colW >= 180 && colW <= 600)
                {
                    ColDisks.Width = new GridLength(colW);
                }
                if (sizes.TryGetValue("EasyMode_RowPartitionMap", out var rowP) && rowP >= 140 && rowP <= 600)
                {
                    RowPartitionMap.Height = new GridLength(rowP);
                }
                if (sizes.TryGetValue("EasyMode_RowConsole", out var rowC) && rowC >= 50 && rowC <= 500)
                {
                    RowConsole.Height = new GridLength(rowC);
                }
            }
        }
        catch { }
    }

    private void SaveLayoutSplitters()
    {
        try
        {
            var s = SettingsService.Instance.Current;
            if (ColDisks.ActualWidth > 0)
                s.LayoutSplitterSizes["EasyMode_ColDisks"] = ColDisks.ActualWidth;
            if (RowPartitionMap.ActualHeight > 0)
                s.LayoutSplitterSizes["EasyMode_RowPartitionMap"] = RowPartitionMap.ActualHeight;
            if (RowConsole.ActualHeight > 0)
                s.LayoutSplitterSizes["EasyMode_RowConsole"] = RowConsole.ActualHeight;
            SettingsService.Instance.Save();
        }
        catch { }
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        RefreshDisksBtnText.Text = lang switch { "zh-CN" => "刷新磁盘", "en-US" => "Refresh Disks", "ja-JP" => "ディスク更新", _ => "重新整理磁碟" };
        AdminInfoBar.Title = lang switch { "zh-CN" => "未具备管理员权限", "en-US" => "Not Admin", "ja-JP" => "管理者権限なし", _ => "未具備系統管理員權限" };
        AdminInfoBar.Message = lang switch { "zh-CN" => "磁盘操作需要管理员权限。", "en-US" => "Run as Admin for disk operations.", "ja-JP" => "ディスク操作には管理者権限が必要です。", _ => "磁碟操作需要管理員權限。" };
        RestartAdminBtn.Content = lang switch { "zh-CN" => "以管理员身份重启", "en-US" => "Restart as Admin", "ja-JP" => "管理者として再起動", _ => "以管理員身分重啟" };

        HeroStatusLabel.Text = lang switch { "zh-CN" => "🛡️ 系统状态:", "en-US" => "🛡️ Health:", "ja-JP" => "🛡️ システム状態:", _ => "🛡️ 系統狀態:" };
        HeroStatusValue.Text = lang switch { "zh-CN" => "健康良好", "en-US" => "Healthy", "ja-JP" => "良好", _ => "健康良好" };
        HeroTempLabel.Text = lang switch { "zh-CN" => "🌡️ 硬盘温度:", "en-US" => "🌡️ Temp:", "ja-JP" => "🌡️ ドライブ温度:", _ => "🌡️ 硬碟溫度:" };
        HeroTempValue.Text = lang switch { "zh-CN" => "实时监控中", "en-US" => "Monitoring", "ja-JP" => "監視中", _ => "即時監控中" };
        HeroBitLockerLabel.Text = lang switch { "zh-CN" => "🔒 BitLocker:", "en-US" => "🔒 BitLocker:", "ja-JP" => "🔒 BitLocker:", _ => "🔒 BitLocker:" };
        HeroBitLockerValue.Text = lang switch { "zh-CN" => "安全守护中", "en-US" => "Protected", "ja-JP" => "保護有効", _ => "安全守護中" };

        HeroHealthCheckBtn.Content = lang switch { "zh-CN" => "⚡ 一键健康体检", "en-US" => "⚡ Health Scan", "ja-JP" => "⚡ ワンクリック診断", _ => "⚡ 一鍵健康體檢" };
        HeroBootRepairBtn.Content = lang switch { "zh-CN" => "🔧 快速修复引导", "en-US" => "🔧 Boot Repair", "ja-JP" => "🔧 ブート修復", _ => "🔧 快速修復引導" };
        HeroDiskCleanBtn.Content = lang switch { "zh-CN" => "🧹 磁盘清理", "en-US" => "🧹 Disk Cleanup", "ja-JP" => "🧹 クリーンアップ", _ => "🧹 磁碟清理" };

        DisksPanelTitle.Text = lang switch { "zh-CN" => "物理磁盘", "en-US" => "Disks", "ja-JP" => "物理ディスク", _ => "實體磁碟" };
        PartitionMapTitle.Text = lang switch { "zh-CN" => "图形化分区地图与容量调整", "en-US" => "Graphical Partition Map & Sizer", "ja-JP" => "グラフィカルパーティションマップ", _ => "圖形化分割區地圖與容量調整" };
        LegendEfiText.Text = lang switch { "zh-CN" => "EFI 系统", "en-US" => "EFI", "ja-JP" => "EFI システム", _ => "EFI 系統" };
        LegendBootText.Text = lang switch { "zh-CN" => "引导/系统", "en-US" => "Boot/OS", "ja-JP" => "ブート/OS", _ => "開機/系統" };
        LegendDataText.Text = lang switch { "zh-CN" => "数据区", "en-US" => "Data", "ja-JP" => "データ領域", _ => "資料區" };
        LegendRecoveryText.Text = lang switch { "zh-CN" => "恢复区", "en-US" => "Recovery", "ja-JP" => "回復パーティション", _ => "修復區" };
        LegendFreeText.Text = lang switch { "zh-CN" => "未分配", "en-US" => "Free", "ja-JP" => "未割り当て", _ => "未分配" };

        QuickSizeLabel.Text = lang switch { "zh-CN" => "快速设置容量：", "en-US" => "Quick Size:", "ja-JP" => "クイックサイズ：", _ => "快速設定容量：" };
        TargetSizeLabel.Text = lang switch { "zh-CN" => "目标大小", "en-US" => "Target Size", "ja-JP" => "目標サイズ", _ => "目標大小" };
        OperationDeltaLabel.Text = lang switch { "zh-CN" => "变更量", "en-US" => "Operation Delta", "ja-JP" => "変更量", _ => "變更量" };

        SuggestedCommandTitle.Text = lang switch { "zh-CN" => "推荐 DiskPart 脚本", "en-US" => "Suggested DiskPart Command", "ja-JP" => "推奨 DiskPart コマンド", _ => "推薦 DiskPart 指令碼" };
        CopyCmdBtn.Content = lang switch { "zh-CN" => "📋 复制", "en-US" => "📋 Copy", "ja-JP" => "📋 コピー", _ => "📋 複製" };
        ExecuteCmdBtn.Content = lang switch { "zh-CN" => "▶️ 执行指令", "en-US" => "▶️ Execute Command", "ja-JP" => "▶️ コマンド実行", _ => "▶️ 執行指令" };

        ToolsExpander.Header = lang switch { "zh-CN" => "分区与卷管理工具", "en-US" => "Partition & Volume Management Tools", "ja-JP" => "パーティションとボリューム管理ツール", _ => "磁碟分割區與磁碟區管理工具" };
        FsBox.Header = lang switch { "zh-CN" => "文件系统", "en-US" => "File System", "ja-JP" => "ファイルシステム", _ => "檔案系統" };
        LabelBox.Header = lang switch { "zh-CN" => "卷标签", "en-US" => "Volume Label", "ja-JP" => "ボリュームラベル", _ => "磁碟區標籤" };
        QuickFormatSwitch.OnContent = lang switch { "zh-CN" => "快速格式化", "en-US" => "Quick", "ja-JP" => "クイック", _ => "快速格式化" };
        QuickFormatSwitch.OffContent = lang switch { "zh-CN" => "完整格式化", "en-US" => "Full", "ja-JP" => "フル", _ => "完整格式化" };
        FormatBtn.Content = lang switch { "zh-CN" => "💾 格式化", "en-US" => "💾 Format", "ja-JP" => "💾 フォーマット", _ => "💾 格式化" };
        LetterBox.Header = lang switch { "zh-CN" => "驱动器号", "en-US" => "Drive Letter", "ja-JP" => "ドライブレター", _ => "磁碟機代號" };
        AssignLetterBtn.Content = lang switch { "zh-CN" => "📌 分配盘符", "en-US" => "📌 Assign", "ja-JP" => "📌 割り当て", _ => "📌 指派代號" };
        RemoveLetterBtn.Content = lang switch { "zh-CN" => "🚫 移除盘符", "en-US" => "🚫 Remove", "ja-JP" => "🚫 削除", _ => "🚫 移除代號" };
        DetailVolumeBtn.Content = lang switch { "zh-CN" => "ℹ️ 卷详情", "en-US" => "ℹ️ Detail", "ja-JP" => "ℹ️ 詳細", _ => "ℹ️ 磁區詳情" };
        SetVolReadOnlyBtn.Content = lang switch { "zh-CN" => "🔒 设为只读", "en-US" => "🔒 Read-Only", "ja-JP" => "🔒 読取専用", _ => "🔒 設為唯讀" };
        ClearVolReadOnlyBtn.Content = lang switch { "zh-CN" => "🔓 解除只读", "en-US" => "🔓 Clear R/O", "ja-JP" => "🔓 読取専用解除", _ => "🔓 解除唯讀" };
        QuickLabelsLabel.Text = lang switch { "zh-CN" => "快速标签：", "en-US" => "Quick Labels:", "ja-JP" => "クイックラベル：", _ => "快速標籤：" };

        PartSizeBox.Header = lang switch { "zh-CN" => "新建分区大小 (MB，留空=最大)", "en-US" => "New Size (MB, blank=Max)", "ja-JP" => "新規サイズ (MB、空=最大)", _ => "新增分割區大小 (MB，留空=最大)" };
        CreatePartBtn.Content = lang switch { "zh-CN" => "➕ 创建主分区", "en-US" => "➕ Create Primary", "ja-JP" => "➕ プライマリ作成", _ => "➕ 建立主要分割區" };
        ExtendVolumeBtn.Content = lang switch { "zh-CN" => "↔️ 扩展卷", "en-US" => "↔️ Extend", "ja-JP" => "↔️ 拡張", _ => "↔️ 延伸磁區" };
        DeletePartBtn.Content = lang switch { "zh-CN" => "🗑️ 删除分区", "en-US" => "🗑️ Delete Partition", "ja-JP" => "🗑️ パーティション削除", _ => "🗑️ 刪除分割區" };
        CleanDiskBtn.Content = lang switch { "zh-CN" => "🧹 清空磁盘", "en-US" => "🧹 Clean Disk", "ja-JP" => "🧹 ディスクをクリア", _ => "🧹 清除磁碟" };
        DetailDiskBtn.Content = lang switch { "zh-CN" => "ℹ️ 磁盘详情", "en-US" => "ℹ️ Detail", "ja-JP" => "ℹ️ ディスク詳細", _ => "ℹ️ 磁碟詳情" };

        ClusterBox.Header = lang switch { "zh-CN" => "簇大小", "en-US" => "Cluster Size", "ja-JP" => "クラスターサイズ", _ => "配置單元大小" };
        StdUefiLayoutBtn.Content = lang switch { "zh-CN" => "⚡ 一键标准 UEFI 4分区", "en-US" => "⚡ 1-Click Standard UEFI Layout", "ja-JP" => "⚡ ワンクリック標準UEFI 4分割", _ => "⚡ 一鍵標準 UEFI 4分區佈局" };
        CreateWinReBtn.Content = lang switch { "zh-CN" => "🩹 创建 WinRE 修复分区", "en-US" => "🩹 Create WinRE Partition", "ja-JP" => "🩹 WinRE 回復パーティション作成", _ => "🩹 建立 WinRE 修復磁區" };
        ExtendFsBtn.Content = lang switch { "zh-CN" => "↔️ 扩展文件系统", "en-US" => "↔️ Extend Filesystem", "ja-JP" => "↔️ ファイルシステム拡張", _ => "↔️ 延伸檔案系統" };
        ShrinkQueryBtn.Content = lang switch { "zh-CN" => "🔍 查询最大可缩减空间", "en-US" => "🔍 Query Max Shrink", "ja-JP" => "🔍 最大縮小量を照会", _ => "🔍 查詢最大可縮減空間" };

        WinDirBox.Header = lang switch { "zh-CN" => "Windows 系统目录", "en-US" => "Windows Directory", "ja-JP" => "Windows ディレクトリ", _ => "Windows 系統目錄" };
        AutoBootBtn.Content = lang switch { "zh-CN" => "🚀 自动修复引导", "en-US" => "🚀 Auto Boot Repair", "ja-JP" => "🚀 自動ブート修復", _ => "🚀 自動修復開機" };

        OutputLogTitle.Text = lang switch { "zh-CN" => "终端输出记录", "en-US" => "Output Log", "ja-JP" => "実行ログ出力", _ => "執行終端記錄" };
        ClearLogBtn.Content = lang switch { "zh-CN" => "清除", "en-US" => "Clear", "ja-JP" => "消去", _ => "清除" };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        RestoreLayoutSplitters();
        if (!_hasLoadedOnce || ViewModel.Disks.Count == 0)
        {
            _hasLoadedOnce = true;
            await ViewModel.RefreshDisksCommand.ExecuteAsync(null);
        }
    }

    private async void DiskContext_Detail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            ViewModel.SelectedDisk = disk;
            await ViewModel.DetailDiskCommand.ExecuteAsync(null);
        }
    }

    private async void DiskContext_Smart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            ViewModel.SelectedDisk = disk;
            await ViewModel.OneClickHealthCheckCommand.ExecuteAsync(null);
        }
    }

    private async void DiskContext_Clean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            ViewModel.SelectedDisk = disk;
            await ViewModel.CleanDiskCommand.ExecuteAsync(null);
        }
    }

    private async void DiskContext_ConvertGpt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            ViewModel.SelectedDisk = disk;
            await ViewModel.InitializeDiskGptCommand.ExecuteAsync(null);
        }
    }

    private async void DiskContext_ConvertMbr_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            ViewModel.SelectedDisk = disk;
            await ViewModel.InitializeDiskMbrCommand.ExecuteAsync(null);
        }
    }

    private void DiskContext_CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.DiskInfo disk)
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(disk.Summary);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }
    }

    private void DiskContext_OpenDiskMgmt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "diskmgmt.msc", UseShellExecute = true });
        }
        catch { }
    }

    private void LogContext_CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ViewModel.OutputLog);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
    }

    private async void LogContext_SaveAs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = System.IO.Path.Combine(desktop, $"DiskMaster_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await System.IO.File.WriteAllTextAsync(path, ViewModel.OutputLog, System.Text.Encoding.UTF8);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }
}
