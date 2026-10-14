using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiskMasterWinUI.Pages;

/// <summary>
/// Recovery tab ("Recovery"). Can be opened with a pre-selected target from the Explorer shell verb
/// (<c>DiskMasterWinUI.exe --tab Recovery --recover "&lt;path&gt;"</c>) through <see cref="LoadTarget"/>.
/// </summary>
public sealed partial class RecoveryPage : Page
{
    public RecoveryViewModel ViewModel { get; } = new();

    private string? _pendingTarget;
    private bool _loaded;

    public RecoveryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        try { LocalizationService.Instance.LanguageChanged += ApplyLanguage; } catch { }

        Loaded += (_, _) =>
        {
            _loaded = true;
            TryApplyPendingTarget();
        };
    }

    /// <summary>
    /// Entry point for the launcher / <c>--recover</c> argument. Safe to call right after construction
    /// (before Loaded) or at any later time; an unusable path is reported in the status area instead of throwing.
    /// </summary>
    public void LoadTarget(string path)
    {
        try
        {
            _pendingTarget = path ?? "";
            TryApplyPendingTarget();
        }
        catch (Exception ex)
        {
            try { ViewModel.StatusMessage = $"載入目標失敗：{ex.Message}"; } catch { }
        }
    }

    private void TryApplyPendingTarget()
    {
        try
        {
            if (!_loaded || _pendingTarget == null) return;
            var path = _pendingTarget;
            _pendingTarget = null;
            ViewModel.LoadTarget(path);
        }
        catch (Exception ex)
        {
            try { ViewModel.StatusMessage = $"載入目標失敗：{ex.Message}"; } catch { }
        }
    }

    private async void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WindowHelper.GetWindowHandle();
            if (hwnd != nint.Zero) InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.DestinationPath = folder.Path;
                // Warn immediately (do not wait until the user presses recover).
                var guard = new RecoveryService().CheckDestination(folder.Path, ViewModel.VisibleItems);
                if (!guard.Ok) ViewModel.StatusMessage = guard.Message;
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"選擇資料夾失敗：{ex.Message}";
        }
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        SourceTitle.Text = lang switch
        {
            "zh-CN" => "🛟 文件恢复 (File Recovery)",
            "en-US" => "🛟 File Recovery",
            "ja-JP" => "🛟 ファイル復元",
            _ => "🛟 檔案復原 (File Recovery)"
        };
        VolumeCombo.Header = lang switch { "zh-CN" => "源磁盘区", "en-US" => "Source volume", "ja-JP" => "ソース ボリューム", _ => "來源磁碟區" };
        TargetPathBox.Header = lang switch
        {
            "zh-CN" => "或输入路径（磁盘机 / 文件夹 / .img 磁盘映像）",
            "en-US" => "Or type a path (drive / folder / .img disk image)",
            "ja-JP" => "またはパスを入力（ドライブ / フォルダー / .img イメージ）",
            _ => "或輸入路徑（磁碟機 / 資料夾 / .img 磁碟映像檔）"
        };
        ScanBtn.Content = lang switch { "zh-CN" => "🔍 开始扫描", "en-US" => "🔍 Scan", "ja-JP" => "🔍 スキャン", _ => "🔍 開始掃描" };
        CancelBtn.Content = lang switch { "zh-CN" => "⏹️ 取消", "en-US" => "⏹️ Cancel", "ja-JP" => "⏹️ キャンセル", _ => "⏹️ 取消" };
        RefreshVolumesBtn.Content = lang switch { "zh-CN" => "🔄 刷新磁盘区", "en-US" => "🔄 Refresh volumes", "ja-JP" => "🔄 ボリューム更新", _ => "🔄 重新整理磁碟區" };
        SignatureCarveCheck.Content = lang switch
        {
            "zh-CN" => "深度签名雕刻（慢，仅 NTFS 可用丛集）",
            "en-US" => "Deep signature carving (slow, NTFS free clusters)",
            "ja-JP" => "署名カービング（低速、NTFS 空きクラスター）",
            _ => "深度簽章雕刻（慢，僅 NTFS 可用叢集）"
        };
        RecycleBinCheck.Content = lang switch
        {
            "zh-CN" => "包含资源回收筒残留（免管理员）",
            "en-US" => "Include Recycle Bin residue (no elevation)",
            "ja-JP" => "ごみ箱の残存ファイルを含める（管理者不要）",
            _ => "包含資源回收筒殘留（免管理員）"
        };
        FilterLabel.Text = lang switch { "zh-CN" => "筛选：", "en-US" => "Filter:", "ja-JP" => "絞り込み：", _ => "篩選：" };
        FilterBox.PlaceholderText = lang switch { "zh-CN" => "文件名 / 路径关键字", "en-US" => "Name / path keyword", "ja-JP" => "名前 / パス", _ => "檔名 / 路徑關鍵字" };
        ResultsTitle.Text = lang switch { "zh-CN" => "🕵️ 已删除项目候选清单", "en-US" => "🕵️ Deleted-item candidates", "ja-JP" => "🕵️ 削除項目候補", _ => "🕵️ 刪除項目候選清單" };
        SelectRecoverableBtn.Content = lang switch { "zh-CN" => "✅ 选取所有可恢复", "en-US" => "✅ Select recoverable", "ja-JP" => "✅ 復元可能を選択", _ => "✅ 選取所有可復原" };
        SelectAllBtn.Content = lang switch { "zh-CN" => "☑️ 全选", "en-US" => "☑️ Select all", "ja-JP" => "☑️ すべて選択", _ => "☑️ 選取全部" };
        SelectNoneBtn.Content = lang switch { "zh-CN" => "⬜ 取消全选", "en-US" => "⬜ Select none", "ja-JP" => "⬜ 選択解除", _ => "⬜ 全部取消" };
        ClearResultsBtn.Content = lang switch { "zh-CN" => "🗑️ 清除清单", "en-US" => "🗑️ Clear list", "ja-JP" => "🗑️ リストを消去", _ => "🗑️ 清除清單" };
        DestinationTitle.Text = lang switch
        {
            "zh-CN" => "💾 恢复到（必须是另一个磁盘区）",
            "en-US" => "💾 Recover to (must be another volume)",
            "ja-JP" => "💾 復元先（別ボリューム必須）",
            _ => "💾 復原至（必須是另一個磁碟區）"
        };
        DestinationHint.Text = lang switch
        {
            "zh-CN" => "安全规则：本工具以只读方式打开来源，并拒绝把恢复数据写回来源磁盘区 — 新写入的文件会重新配置尚未使用的丛集，直接覆盖掉正要抢救的内容。",
            "en-US" => "Safety rule: the source is opened read-only and writing recovered data back to the source volume is refused — new files reuse free clusters and would overwrite what you are trying to save.",
            "ja-JP" => "安全規則：ソースは読み取り専用で開き、ソース ボリュームへの書き戻しは拒否します。新規ファイルは未使用クラスターを再利用し、復元対象を上書きしてしまうためです。",
            _ => "安全規則：本工具以唯讀方式開啟來源，並拒絕把復原資料寫回來源磁碟區 — 新寫入的檔案會重新配置尚未使用的叢集，直接覆寫掉正要搶救的內容。"
        };
        DestinationBox.Header = lang switch { "zh-CN" => "目标文件夹", "en-US" => "Destination folder", "ja-JP" => "保存先フォルダー", _ => "目的資料夾" };
        BrowseDestinationBtn.Content = lang switch { "zh-CN" => "📁 选择…", "en-US" => "📁 Browse…", "ja-JP" => "📁 参照…", _ => "📁 選擇…" };
        PreservePathsCheck.Content = lang switch { "zh-CN" => "保留原始文件夹结构", "en-US" => "Keep original folder structure", "ja-JP" => "元のフォルダー構造を保持", _ => "保留原始資料夾結構" };
        RecoverBtn.Content = lang switch { "zh-CN" => "🛟 恢复选取的项目", "en-US" => "🛟 Recover selected", "ja-JP" => "🛟 選択項目を復元", _ => "🛟 復原選取的項目" };
        LogTitle.Text = lang switch { "zh-CN" => "📜 执行记录", "en-US" => "📜 Execution log", "ja-JP" => "📜 実行ログ", _ => "📜 執行記錄" };
        ClearLogBtn.Content = lang switch { "zh-CN" => "清除记录", "en-US" => "Clear log", "ja-JP" => "ログを消去", _ => "清除記錄" };
    }
}
