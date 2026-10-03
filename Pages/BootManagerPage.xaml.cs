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

        TabSafeBoot.Header = lang switch
        {
            "zh-CN" => "🛡️ 安全引导与旗标",
            "en-US" => "🛡️ Safe Boot & Flags",
            "ja-JP" => "🛡️ セーフブートとフラグ",
            _ => "🛡️ 安全開機與旗標"
        };
        SafeBootTitleText.Text = lang switch
        {
            "zh-CN" => "MSConfig 系统安全引导模式与高级引导旗标",
            "en-US" => "MSConfig Safe Boot Modes & Advanced Boot Flags",
            "ja-JP" => "MSConfig セーフブートモードと詳細ブートフラグ",
            _ => "MSConfig 系統安全開機模式與進階開機旗標"
        };
        SafeBootSubtitleText.Text = lang switch
        {
            "zh-CN" => "直接调用 Windows BCD 控制底层启动参数，支持 Minimal、Network、AlternateShell、DsRepair 安全模式，以及无 GUI 引导、引导日志与测试签名。",
            "en-US" => "Directly controls low-level Windows BCD startup parameters. Supports Minimal, Network, AlternateShell, DsRepair safe boot modes, No GUI boot, Boot log, and Test signing.",
            "ja-JP" => "Windows BCDを制御して起動パラメータを設定します。最小構成、ネットワーク、コマンドプロンプト、ディレクトリ復旧のセーフモード、No GUI起動、ブートログなどをサポートします。",
            _ => "直接調用 Windows BCD 控制底層啟動參數，支援 Minimal、Network、AlternateShell、DsRepair 安全模式，以及無 GUI 開機、開機記錄檔與測試簽署。"
        };
        LoadSafeBootBtnText.Text = lang switch
        {
            "zh-CN" => "🔄 读取当前设置",
            "en-US" => "🔄 Read Settings",
            "ja-JP" => "🔄 設定を再読込",
            _ => "🔄 讀取當前設定"
        };
        ApplySafeBootBtnText.Text = lang switch
        {
            "zh-CN" => "💾 应用安全引导设置",
            "en-US" => "💾 Apply Safe Boot Settings",
            "ja-JP" => "💾 セーフブート設定を適用",
            _ => "💾 套用安全開機設定"
        };
        SafeBootModeHeader.Text = lang switch
        {
            "zh-CN" => "安全引导模式 (Safe Boot Mode)",
            "en-US" => "Safe Boot Mode",
            "ja-JP" => "セーフブートモード (Safe Boot Mode)",
            _ => "安全開機模式 (Safe Boot Mode)"
        };
        AdvancedFlagsHeader.Text = lang switch
        {
            "zh-CN" => "高级引导旗标与诊断 (Advanced Boot Flags)",
            "en-US" => "Advanced Boot Flags & Diagnostics",
            "ja-JP" => "詳細ブートフラグと診断 (Advanced Boot Flags)",
            _ => "進階開機旗標與診斷開機 (Advanced Boot Flags)"
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        await ViewModel.RefreshEntriesCommand.ExecuteAsync(null);
        await ViewModel.LoadSafeBootConfigCommand.ExecuteAsync(null);
    }
    private void AddToBcd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string windowsPath)
        {
            ViewModel.AddToBcdCommand.Execute(windowsPath);
        }
    }
}
