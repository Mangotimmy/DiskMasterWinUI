using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class NtfsPermissionsPage : Page
{
    public NtfsPermissionsViewModel ViewModel { get; } = new();

    public NtfsPermissionsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        DiskMasterWinUI.Helpers.TabReorderHelper.Attach(NtfsTabView);
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        TabQuickActions.Header = lang switch { "zh-CN" => "⚡ 常用快捷操作 (Quick Actions)", "en-US" => "⚡ Quick Actions", "ja-JP" => "⚡ クイックアクション", _ => "⚡ 常用快捷操作 (Quick Actions)" };
        TabNtfsExplorer.Header = lang switch { "zh-CN" => "🔍 NTFS 深度特性探索", "en-US" => "🔍 NTFS Deep Explorer", "ja-JP" => "🔍 NTFS 詳細エクスプローラー", _ => "🔍 NTFS 深度特性探索" };
        TabRuleBuilder.Header = lang switch { "zh-CN" => "🛠️ 自定义 icacls 规则生成器", "en-US" => "🛠️ Custom icacls Builder", "ja-JP" => "🛠️ カスタム icacls ビルダー", _ => "🛠️ 自訂 icacls 規則建置器" };
        TabTerminal.Header = lang switch { "zh-CN" => "💻 执行记录终端", "en-US" => "💻 Execution Terminal", "ja-JP" => "💻 実行ログターミナル", _ => "💻 執行記錄終端機" };

        TargetPathBox.Header = lang switch { "zh-CN" => "目标磁盘或目录路径", "en-US" => "Target Drive or Directory", "ja-JP" => "対象ドライブまたはディレクトリ", _ => "目標磁碟區或目錄路徑" };
        TargetPathBox.PlaceholderText = lang switch { "zh-CN" => "例如 D:\\ 或 C:\\Users\\Public", "en-US" => "e.g. D:\\ or C:\\Users\\Public", "ja-JP" => "例: D:\\ または C:\\Users\\Public", _ => "例如 D:\\ 或 C:\\Users\\Public" };
        BrowseFolderBtn.Content = lang switch { "zh-CN" => "📁 浏览文件夹", "en-US" => "📁 Browse Folder", "ja-JP" => "📁 フォルダ参照", _ => "📁 瀏覽資料夾" };

        AdminInfoBar.Title = lang switch { "zh-CN" => "未具备管理员权限", "en-US" => "Not Admin", "ja-JP" => "管理者権限なし", _ => "未具備系統管理員權限" };
        AdminInfoBar.Message = lang switch { "zh-CN" => "取得所有权与修改 ACL 需要管理员权限。", "en-US" => "Taking ownership and modifying ACLs requires Administrator.", "ja-JP" => "所有権取得とACL変更には管理者権限が必要です。", _ => "取得擁有權與修改 ACL 需要系統管理員權限。" };
        RestartAdminBtn.Content = lang switch { "zh-CN" => "以管理员身份重启", "en-US" => "Restart as Admin", "ja-JP" => "管理者として再起動", _ => "以管理員身分重啟" };

        BatchPresetsTitle.Text = lang switch { "zh-CN" => "快速批量预设动作 (递归 /T /C)", "en-US" => "Quick Batch Presets (Recursive /T /C)", "ja-JP" => "クイックバッチプリセット (再帰 /T /C)", _ => "快速批次預設動作 (遞迴 /T /C)" };
        TakeOwnershipTitle.Text = lang switch { "zh-CN" => "🛡️ 取得所有权", "en-US" => "🛡️ Take Ownership", "ja-JP" => "🛡️ 所有権を取得", _ => "🛡️ 取得擁有權" };
        TakeOwnershipDesc.Text = lang switch { "zh-CN" => "修复「访问被拒」问题", "en-US" => "Fixes 'Access is Denied'.", "ja-JP" => "「アクセス拒否」を修復", _ => "修復「存取被拒」問題" };

        GrantEveryoneTitle.Text = lang switch { "zh-CN" => "👥 授予所有人权限", "en-US" => "👥 Grant Everyone", "ja-JP" => "👥 全員にアクセス付与", _ => "👥 賦予所有人權限" };
        GrantEveryoneDesc.Text = lang switch { "zh-CN" => "完全读写控制访问", "en-US" => "Full Read/Write access.", "ja-JP" => "完全な読み書きアクセス", _ => "完全讀寫控制存取權" };

        ResetDefaultsTitle.Text = lang switch { "zh-CN" => "🔄 重置为默认继承", "en-US" => "🔄 Reset Defaults", "ja-JP" => "🔄 デフォルト継承にリセット", _ => "🔄 重設為預設繼承" };
        ResetDefaultsDesc.Text = lang switch { "zh-CN" => "继承上层目录 ACL 规则", "en-US" => "Inherit parent ACLs.", "ja-JP" => "親ディレクトリのACLを継承", _ => "繼承上層目錄 ACL 規則" };

        AdminOnlyTitle.Text = lang switch { "zh-CN" => "🔒 仅限管理员", "en-US" => "🔒 Admin Only", "ja-JP" => "🔒 管理者のみ", _ => "🔒 僅限管理員" };
        AdminOnlyDesc.Text = lang switch { "zh-CN" => "移除普通用户权限", "en-US" => "Strip non-admin rights.", "ja-JP" => "一般ユーザー権限を削除", _ => "移除一般使用者權限" };

        CustomRuleBuilderTitle.Text = lang switch { "zh-CN" => "自定义 icacls 规则生成器", "en-US" => "Custom icacls Rule Builder", "ja-JP" => "カスタム icacls ルールビルダー", _ => "自訂 icacls 規則建置器" };
        UserPrincipalCombo.Header = lang switch { "zh-CN" => "用户 / 组主体", "en-US" => "User / Principal", "ja-JP" => "ユーザー / プリンシパル", _ => "使用者 / 群組主體" };
        AccessLevelCombo.Header = lang switch { "zh-CN" => "权限级别", "en-US" => "Access Level", "ja-JP" => "アクセスレベル", _ => "權限等級" };
        InheritanceScopeCombo.Header = lang switch { "zh-CN" => "继承作用范围", "en-US" => "Inheritance Scope", "ja-JP" => "継承スコープ", _ => "繼承作用範圍" };

        RecursiveCheck.Content = lang switch { "zh-CN" => "递归应用子目录与文件 (/T)", "en-US" => "Recursive (/T)", "ja-JP" => "サブディレクトリに再帰適用 (/T)", _ => "遞迴套用子目錄與檔案 (/T)" };
        ContinueOnErrorCheck.Content = lang switch { "zh-CN" => "遇到错误时继续执行 (/C)", "en-US" => "Continue on Error (/C)", "ja-JP" => "エラー時も継続 (/C)", _ => "遇到錯誤時繼續執行 (/C)" };

        GeneratedPreviewBox.Header = lang switch { "zh-CN" => "生成的 icacls 命令预览", "en-US" => "Generated icacls Command Preview", "ja-JP" => "生成された icacls コマンドプレビュー", _ => "產生的 icacls 命令預覽" };
        RunBatchApplyBtn.Content = lang switch { "zh-CN" => "▶️ 批量应用权限", "en-US" => "▶️ Run Batch Apply", "ja-JP" => "▶️ バッチ適用実行", _ => "▶️ 批次套用權限" };

        StopNtfsBtn.Content = lang switch { "zh-CN" => "⏹️ 停止", "en-US" => "⏹️ Stop", "ja-JP" => "⏹️ 停止", _ => "⏹️ 停止" };
        ClearTerminalBtn.Content = lang switch { "zh-CN" => "清除终端记录", "en-US" => "Clear Output", "ja-JP" => "出力クリア", _ => "清除終端記錄" };
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WindowHelper.GetWindowHandle();
        if (hwnd != nint.Zero)
        {
            InitializeWithWindow.Initialize(folderPicker, hwnd);
        }

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            ViewModel.TargetPath = folder.Path;
        }
    }
}
