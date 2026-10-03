using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class BootManagerPage : Page
{
    public BootManagerViewModel ViewModel { get; } = new();

    public BootManagerPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        RefreshBcdBtnText.Text = lang switch
        {
            "zh-CN" => "刷新引导项目",
            "en-US" => "Refresh BCD Entries",
            "ja-JP" => "ブート項目を更新",
            _ => "重新整理開機項目"
        };
        DeepScanBtnText.Text = lang switch
        {
            "zh-CN" => "🔍 全盘探测并挂载引导",
            "en-US" => "🔍 Deep Scan & Mount Boot",
            "ja-JP" => "🔍 ディープスキャンとブートマウント",
            _ => "🔍 全盤探測並掛載開機"
        };
        MountEspBtn.Content = lang switch
        {
            "zh-CN" => "💿 挂载 ESP",
            "en-US" => "💿 Mount ESP",
            "ja-JP" => "💿 ESP をマウント",
            _ => "💿 掛載 ESP"
        };
        UnmountEspBtn.Content = lang switch
        {
            "zh-CN" => "⏏️ 卸载 ESP",
            "en-US" => "⏏️ Unmount ESP",
            "ja-JP" => "⏏️ ESP をアンマウント",
            _ => "⏏️ 卸載 ESP"
        };
        BcdExportBtn.Content = lang switch
        {
            "zh-CN" => "💾 备份 BCD",
            "en-US" => "💾 Backup BCD",
            "ja-JP" => "💾 BCD をバックアップ",
            _ => "💾 備份 BCD"
        };
        BcdImportBtn.Content = lang switch
        {
            "zh-CN" => "📥 还原 BCD",
            "en-US" => "📥 Restore BCD",
            "ja-JP" => "📥 BCD を復元",
            _ => "📥 還原 BCD"
        };

        TabBootEntries.Header = lang switch
        {
            "zh-CN" => "引导项目管理",
            "en-US" => "Boot Entries",
            "ja-JP" => "ブート項目管理",
            _ => "開機項目管理"
        };
        TabBuildBoot.Header = lang switch
        {
            "zh-CN" => "全新分区重建引导",
            "en-US" => "Build New Boot Partition",
            "ja-JP" => "新規パーティションにブート再構築",
            _ => "全新分割區建置開機引導"
        };
        TabGlobalSettings.Header = lang switch
        {
            "zh-CN" => "全局引导设置",
            "en-US" => "Global Settings",
            "ja-JP" => "グローバルブート設定",
            _ => "全域開機設定"
        };

        SearchBox.PlaceholderText = lang switch
        {
            "zh-CN" => "搜索系统名称、UUID、磁盘设备...",
            "en-US" => "Search by OS Name, UUID, Device...",
            "ja-JP" => "OS名、UUID、デバイスで検索...",
            _ => "搜尋作業系統名稱、UUID、磁碟裝置..."
        };
        EditEntryTitle.Text = lang switch
        {
            "zh-CN" => "编辑引导项目",
            "en-US" => "Edit Boot Entry",
            "ja-JP" => "ブート項目の編集",
            _ => "編輯開機項目"
        };
        DescBox.Header = lang switch
        {
            "zh-CN" => "引导菜单显示名称",
            "en-US" => "Description (Display Name in Boot Menu)",
            "ja-JP" => "ブートメニュー表示名",
            _ => "開機選單顯示名稱"
        };
        SaveDescBtn.Content = lang switch
        {
            "zh-CN" => "💾 保存名称",
            "en-US" => "💾 Save Description",
            "ja-JP" => "💾 名前を保存",
            _ => "💾 儲存名稱"
        };
        SetDefaultBtn.Content = lang switch
        {
            "zh-CN" => "⭐ 设为默认系统",
            "en-US" => "⭐ Set as Default OS",
            "ja-JP" => "⭐ 既定のOSに設定",
            _ => "⭐ 設為預設系統"
        };
        CloneEntryBtn.Content = lang switch
        {
            "zh-CN" => "📋 复制项目",
            "en-US" => "📋 Clone Entry",
            "ja-JP" => "📋 エントリを複製",
            _ => "📋 複製項目"
        };
        DeleteEntryBtn.Content = lang switch
        {
            "zh-CN" => "🗑️ 删除项目",
            "en-US" => "🗑️ Delete Entry",
            "ja-JP" => "🗑️ エントリを削除",
            _ => "🗑️ 刪除項目"
        };

        BuildBootBtnText.Text = lang switch
        {
            "zh-CN" => "🚀 全新分区重建引导",
            "en-US" => "🚀 Build New Boot Manager on Partition",
            "ja-JP" => "🚀 新規パーティションにブートマネージャ構築",
            _ => "🚀 全新分割區建置開機引導"
        };
        ApplyGlobalSettingsBtn.Content = lang switch
        {
            "zh-CN" => "💾 应用全局设置",
            "en-US" => "💾 Apply Global Settings",
            "ja-JP" => "💾 グローバル設定を適用",
            _ => "💾 套用全域設定"
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        await ViewModel.RefreshEntriesCommand.ExecuteAsync(null);
    }
    private void AddToBcd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string windowsPath)
        {
            ViewModel.AddToBcdCommand.Execute(windowsPath);
        }
    }
}
