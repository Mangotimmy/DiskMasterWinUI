using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class SystemHealthPage : Page
{
    public SystemHealthViewModel ViewModel { get; } = new();

    public SystemHealthPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        AdminInfoBar.Title = lang switch { "zh-CN" => "未具备管理员权限", "en-US" => "Not Admin", "ja-JP" => "管理者権限なし", _ => "未具備系統管理員權限" };
        AdminInfoBar.Message = lang switch { "zh-CN" => "SFC 与 DISM 修复需要管理员权限。", "en-US" => "SFC and DISM require Administrator privileges.", "ja-JP" => "SFCとDISMには管理者権限が必要です。", _ => "SFC 與 DISM 修復需要管理員權限。" };
        RestartAdminBtn.Content = lang switch { "zh-CN" => "以管理员身份重启", "en-US" => "Restart as Admin", "ja-JP" => "管理者として再起動", _ => "以管理員身分重啟" };

        OneClickTitleText.Text = lang switch { "zh-CN" => "🚀 一键全自动 Windows 健康修复", "en-US" => "1-Click Complete Windows Health Repair", "ja-JP" => "🚀 ワンクリック Windows 自動修復", _ => "🚀 一鍵全自動 Windows 健康度修復" };
        StartRepairBtnText.Text = lang switch { "zh-CN" => "开始全自动健康修复", "en-US" => "Start Full Health Repair", "ja-JP" => "自動修復を開始", _ => "開始全自動健康修復" };
        SfcTitleText.Text = lang switch { "zh-CN" => "系统文件检查程序 (SFC)", "en-US" => "System File Checker (SFC)", "ja-JP" => "システムファイルチェッカー (SFC)", _ => "系統檔案檢查程式 (SFC)" };
        DismTitleText.Text = lang switch { "zh-CN" => "部署映像与维护服务 (DISM)", "en-US" => "Deployment Image Servicing (DISM)", "ja-JP" => "展開イメージ サービスと管理 (DISM)", _ => "映像部署與維護服務 (DISM)" };

        AdvCleanupExpander.Header = lang switch { "zh-CN" => "🧹 高级清理", "en-US" => "🧹 Advanced Cleanup", "ja-JP" => "🧹 高度クリーンアップ", _ => "🧹 進階清理" };
        AnalyzeStoreBtn.Content = lang switch { "zh-CN" => "📊 分析组件存储", "en-US" => "📊 Analyze Component Store", "ja-JP" => "📊 コンポーネントストア分析", _ => "📊 分析元件存放區" };
        ResetBaseBtn.Content = lang switch { "zh-CN" => "🗑️ 深度清理 /ResetBase (不可逆)", "en-US" => "🗑️ Deep Cleanup /ResetBase (Irreversible)", "ja-JP" => "🗑️ 深度クリア /ResetBase (不可逆)", _ => "🗑️ 深度清理 /ResetBase (不可逆)" };
        SourceRepairExpander.Header = lang switch { "zh-CN" => "🔧 指定修复来源", "en-US" => "🔧 Repair with Source", "ja-JP" => "🔧 修復ソース指定", _ => "🔧 指定修復來源" };
        RepairSourceBox.Header = lang switch { "zh-CN" => "修复来源路径", "en-US" => "Repair Source Path", "ja-JP" => "修復ソースパス", _ => "修復來源路徑" };
        RestoreWithSourceBtn.Content = lang switch { "zh-CN" => "🛠️ 使用来源修复", "en-US" => "🛠️ RestoreHealth with Source", "ja-JP" => "🛠️ ソースから修復", _ => "🛠️ 使用來源修復" };
        FeaturesExpander.Header = lang switch { "zh-CN" => "⚙️ Windows 功能管理", "en-US" => "⚙️ Windows Features", "ja-JP" => "⚙️ Windows 機能管理", _ => "⚙️ Windows 功能管理" };
        ListFeaturesBtnText.Text = lang switch { "zh-CN" => "加载/刷新功能列表", "en-US" => "Load/Refresh Features", "ja-JP" => "機能リスト読込/更新", _ => "載入/重整功能清單" };
        FeatureNameBox.Header = lang switch { "zh-CN" => "功能名称", "en-US" => "Feature Name", "ja-JP" => "機能名", _ => "功能名稱" };
        FeatureSourceBox.Header = lang switch { "zh-CN" => "来源路径 (可选)", "en-US" => "Source Path (Optional)", "ja-JP" => "ソースパス (省略可)", _ => "來源路徑 (選填)" };
        EnableFeatureBtn.Content = lang switch { "zh-CN" => "✅ 启用功能", "en-US" => "✅ Enable Feature", "ja-JP" => "✅ 機能を有効化", _ => "✅ 啟用功能" };
        DisableFeatureBtn.Content = lang switch { "zh-CN" => "❌ 禁用功能", "en-US" => "❌ Disable Feature", "ja-JP" => "❌ 機能を無効化", _ => "❌ 停用功能" };
        PackagesExpander.Header = lang switch { "zh-CN" => "📦 已安装更新管理", "en-US" => "📦 Package Management", "ja-JP" => "📦 インストール済み更新管理", _ => "📦 已安裝更新管理" };
        ListPackagesBtnText.Text = lang switch { "zh-CN" => "加载/刷新更新列表", "en-US" => "Load/Refresh Packages", "ja-JP" => "パッケージリスト読込/更新", _ => "載入/重整更新清單" };
        PackageNameBox.Header = lang switch { "zh-CN" => "包名称", "en-US" => "Package Name", "ja-JP" => "パッケージ名", _ => "套件名稱" };
        RemovePackageBtn.Content = lang switch { "zh-CN" => "🗑️ 删除包", "en-US" => "🗑️ Remove Package", "ja-JP" => "🗑️ パッケージ削除", _ => "🗑️ 移除套件" };
        DriverExpander.Header = lang switch { "zh-CN" => "💾 驱动程序备份", "en-US" => "💾 Driver Export", "ja-JP" => "💾 ドライバーエクスポート", _ => "💾 驅動程式備份" };
        DriverExportBox.Header = lang switch { "zh-CN" => "输出目录", "en-US" => "Output Directory", "ja-JP" => "出力ディレクトリ", _ => "輸出目錄" };
        ExportDriversBtn.Content = lang switch { "zh-CN" => "💾 导出所有驱动", "en-US" => "💾 Export All Drivers", "ja-JP" => "💾 全ドライバーエクスポート", _ => "💾 匯出所有驅動" };

        OfflineRepairExpander.Header = lang switch { "zh-CN" => "🧰 WinPE 离线系统修复", "en-US" => "🧰 WinPE Offline System Repair", "ja-JP" => "🧰 WinPE オフラインシステム修復", _ => "🧰 WinPE 離線系統修復" };
        OfflineWinDirBox.Header = lang switch { "zh-CN" => "离线 Windows 目录", "en-US" => "Offline Windows Directory", "ja-JP" => "オフライン Windows ディレクトリ", _ => "離線 Windows 目錄" };
        OfflineBootDirBox.Header = lang switch { "zh-CN" => "离线引导磁盘卷", "en-US" => "Offline Boot Directory", "ja-JP" => "オフライン ブートボリューム", _ => "離線開機磁碟區" };
        OfflineDismBtn.Content = lang switch { "zh-CN" => "🛠️ 离线 DISM 修复", "en-US" => "🛠️ Offline DISM Restore", "ja-JP" => "🛠️ オフライン DISM 修復", _ => "🛠️ 離線 DISM 修復" };
        OfflineSfcBtn.Content = lang switch { "zh-CN" => "🔍 离线 SFC 扫描修复", "en-US" => "🔍 Offline SFC Scan", "ja-JP" => "🔍 オフライン SFC スキャン", _ => "🔍 離線 SFC 掃描修復" };

        WindowsUpdateExpander.Header = lang switch { "zh-CN" => "🔄 Windows Update 修复与更新管理", "en-US" => "🔄 Windows Update Repair & Management", "ja-JP" => "🔄 Windows Update 修復と更新管理", _ => "🔄 Windows Update 修復與更新管理" };
        TempCleanExpander.Header = lang switch { "zh-CN" => "🧹 系统缓存与垃圾深度清理", "en-US" => "🧹 System Temp & Deep Cache Cleanup", "ja-JP" => "🧹 システム一時ファイル・キャッシュ徹底削除", _ => "🧹 系統暫存與垃圾深度清理" };
    }

    private void FeatureToggle_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DiskMasterWinUI.Models.WindowsFeatureItem item)
        {
            ViewModel.ToggleFeatureStateCommand.Execute(item);
        }
    }

    private void PackageRemove_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DiskMasterWinUI.Models.WindowsPackageItem item)
        {
            ViewModel.RemovePackageDirectCommand.Execute(item);
        }
    }

    private async void WindowsUpdate_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
        {
            // If the user manually toggled, sync with ViewModel
            if (ts.IsOn != ViewModel.WuInfo.IsDisabled)
            {
                await ViewModel.ToggleWindowsUpdateCommand.ExecuteAsync(null);
            }
        }
    }

    private void SelectAllTemp_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.ToggleSelectAllTempCommand.Execute(null);
    }

    private async void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ApplyLanguage();
        await ViewModel.InitializeAsync();
    }
}
