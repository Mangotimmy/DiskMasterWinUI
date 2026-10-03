using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;

using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class AdvancedModePage : Page
{
    public AdvancedModeViewModel ViewModel { get; } = new();

    public AdvancedModePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        TabDiskPart.Header = lang switch { "zh-CN" => "DiskPart 脚本", "en-US" => "DiskPart Script", "ja-JP" => "DiskPart スクリプト", _ => "DiskPart 腳本" };
        TemplatesLabel.Text = lang switch { "zh-CN" => "模板：", "en-US" => "Templates:", "ja-JP" => "テンプレート：", _ => "範本：" };
        TplListAllBtn.Content = lang switch { "zh-CN" => "列出所有", "en-US" => "List All", "ja-JP" => "全リスト", _ => "列出所有" };
        TplCleanGptBtn.Content = lang switch { "zh-CN" => "清空+GPT", "en-US" => "Clean + GPT", "ja-JP" => "クリア+GPT", _ => "清空+GPT" };
        TplCleanMbrBtn.Content = lang switch { "zh-CN" => "清空+MBR", "en-US" => "Clean + MBR", "ja-JP" => "クリア+MBR", _ => "清空+MBR" };
        TplUsbBootBtn.Content = lang switch { "zh-CN" => "USB 启动盘", "en-US" => "USB Boot", "ja-JP" => "USB起動ディスク", _ => "USB 開機碟" };
        TplWinInstallBtn.Content = lang switch { "zh-CN" => "安装 Windows", "en-US" => "Win Install", "ja-JP" => "Windowsインストール", _ => "安裝 Windows" };
        TplStandardUefiBtn.Content = lang switch { "zh-CN" => "UEFI 4分区", "en-US" => "Standard UEFI", "ja-JP" => "標準UEFI 4分割", _ => "UEFI 4分區" };
        TplWinReBtn.Content = lang switch { "zh-CN" => "WinRE 分区", "en-US" => "WinRE Partition", "ja-JP" => "WinRE パーティション", _ => "WinRE 分割區" };
        TplFixSigBtn.Content = lang switch { "zh-CN" => "修复签名冲突", "en-US" => "Fix Sig Collision", "ja-JP" => "署名衝突修復", _ => "修復簽章衝突" };

        QuickActionsLabel.Text = lang switch { "zh-CN" => "快捷操作：", "en-US" => "Quick Actions:", "ja-JP" => "クイック操作：", _ => "快捷操作：" };
        ClearReadOnlyBtn.Content = lang switch { "zh-CN" => "🔓 清除只读", "en-US" => "🔓 Clear Read-Only", "ja-JP" => "🔓 読取専用解除", _ => "🔓 清除唯讀" };
        SetOnlineBtn.Content = lang switch { "zh-CN" => "🌐 上线磁盘", "en-US" => "🌐 Online Disk", "ja-JP" => "🌐 ディスクをオンライン", _ => "🌐 上線磁碟" };
        SetOfflineBtn.Content = lang switch { "zh-CN" => "📴 离线磁盘", "en-US" => "📴 Offline Disk", "ja-JP" => "📴 ディスクをオフライン", _ => "📴 離線磁碟" };
        GetUniqueIdBtn.Content = lang switch { "zh-CN" => "🆔 磁盘签名/ID", "en-US" => "🆔 Unique ID", "ja-JP" => "🆔 ディスク署名/ID", _ => "🆔 磁碟簽章/ID" };
        RescanDisksBtn.Content = lang switch { "zh-CN" => "🔄 重新扫描设备", "en-US" => "🔄 Rescan Devices", "ja-JP" => "🔄 デバイス再スキャン", _ => "🔄 重新掃描裝置" };

        DiskPartScriptBox.Header = lang switch { "zh-CN" => "DiskPart 脚本", "en-US" => "DiskPart Script", "ja-JP" => "DiskPart スクリプト", _ => "DiskPart 指令碼" };
        ExecuteScriptBtnText.Text = lang switch { "zh-CN" => "执行脚本", "en-US" => "Execute Script", "ja-JP" => "スクリプト実行", _ => "執行指令碼" };
        ClearDiskPartOutputBtn.Content = lang switch { "zh-CN" => "清除输出", "en-US" => "Clear Output", "ja-JP" => "出力クリア", _ => "清除輸出" };

        TabBootRepair.Header = lang switch { "zh-CN" => "引导修复", "en-US" => "Boot Repair", "ja-JP" => "ブート修復", _ => "開機修復" };
        AutoRepairBootBtnText.Text = lang switch { "zh-CN" => "全自动引导修复", "en-US" => "Full Auto Boot Repair", "ja-JP" => "全自動ブート修復", _ => "全自動引導開機修復" };

        TabBcdEdit.Header = lang switch { "zh-CN" => "BCD 项目管理", "en-US" => "BCDEdit", "ja-JP" => "BCD 項目管理", _ => "BCD 項目管理" };
        EnumBcdBtnText.Text = lang switch { "zh-CN" => "枚举 BCD", "en-US" => "Enumerate BCD", "ja-JP" => "BCD を列挙", _ => "列舉 BCD" };
        SetDefaultBcdBtn.Content = lang switch { "zh-CN" => "设为默认值", "en-US" => "Set as Default", "ja-JP" => "既定値に設定", _ => "設為預設值" };
        DeleteBcdBtn.Content = lang switch { "zh-CN" => "删除项目", "en-US" => "Delete Entry", "ja-JP" => "エントリを削除", _ => "刪除項目" };

        TabFullLog.Header = lang switch { "zh-CN" => "完整执行记录", "en-US" => "Full Log", "ja-JP" => "完全実行ログ", _ => "完整執行記錄" };
        ClearFullLogBtn.Content = lang switch { "zh-CN" => "清除记录", "en-US" => "Clear Log", "ja-JP" => "ログ消去", _ => "清除記錄" };
    }

    private void Template_ListAll(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("ListAll");

    private void Template_CleanGPT(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("CleanGPT");

    private void Template_CleanMBR(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("CleanMBR");

    private void Template_USB(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("CreateUSB");

    private void Template_WinInstall(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("WinInstall");

    private void Template_StandardUEFI(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("StandardUEFI");

    private void Template_WinRE(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("WinRERecovery");

    private void Template_FixSig(object sender, RoutedEventArgs e) =>
        ViewModel.InsertDiskPartTemplateCommand.Execute("FixSignatureCollision");
}
