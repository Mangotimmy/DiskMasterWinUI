using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        UpdateFontPreview();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.CustomFontFamily) || e.PropertyName == nameof(ViewModel.CustomFontScale))
            {
                UpdateFontPreview();
            }
        };
    }

    public void UpdateFontPreview()
    {
        var font = ViewModel.CustomFontFamily;
        if (string.IsNullOrWhiteSpace(font)) font = "Segoe UI Variable";
        double scale = (ViewModel.CustomFontScale > 0 ? ViewModel.CustomFontScale : 100.0) / 100.0;
        try
        {
            var ff = new FontFamily(font);
            PreviewTwText.FontFamily = ff;
            PreviewCnText.FontFamily = ff;
            PreviewEnText.FontFamily = ff;
            PreviewJaText.FontFamily = ff;

            PreviewTwText.FontSize = 13.0 * scale;
            PreviewCnText.FontSize = 13.0 * scale;
            PreviewEnText.FontSize = 13.0 * scale;
            PreviewJaText.FontSize = 13.0 * scale;
        }
        catch { }
    }

    public void ApplyLanguage()
    {
        SettingsTitleText.Text = LocalizationService.T("⚙️ 應用程式偏好設定", "⚙️ 应用程序偏好设置", "⚙️ Application Settings", "⚙️ アプリケーション設定");
        SettingsSubtitleText.Text = LocalizationService.T(
            "自訂 DiskMaster Pro 介面外觀、全域縮放、安全防呆確認與磁碟預設行為。",
            "自定义 DiskMaster Pro 界面外观、全局缩放、安全防呆确认与磁盘默认行为。",
            "Customize DiskMaster Pro appearance, UI scaling, safety protections, and disk defaults.",
            "DiskMaster Pro の外観、UI スケーリング、安全保護、ディスク既定動作をカスタマイズします。");

        // Section 0: Language & Region
        LanguageCardTitle.Text = LocalizationService.Instance["LanguageSelect"];
        LanguageCardDesc.Text = LocalizationService.T(
            "選擇 DiskMaster Pro 顯示語言，全域介面即時無重啟切換。",
            "选择 DiskMaster Pro 显示语言，全局界面即时无重启切换。",
            "Choose DiskMaster Pro display language with instant live reload.",
            "DiskMaster Pro の表示言語を選択します。再起動なしで即座に切り替わります。");
        LanguageBox.Header = LocalizationService.T("選擇介面語言", "选择界面语言", "UI Language", "UI 言語");

        // Section 0.1: Fonts & Typography
        FontsCardTitle.Text = LocalizationService.Instance["CustomFonts"];
        FontsCardDesc.Text = LocalizationService.T(
            "自由挑選喜愛的系統字型或手動輸入任何已安裝字型家族名稱，全視窗立即同步套用。",
            "自由挑选喜爱的系统字体或手动输入任何已安装字体家族名称，全窗口立即同步应用。",
            "Select preferred system fonts or enter any installed font family name.",
            "お好みのシステムフォントを選択するか、インストール済みフォント名を入力して全ウィンドウに適用します。");
        FontFamilyBox.Header = LocalizationService.T("精選介面字型", "精选界面字体", "Featured UI Fonts", "おすすめ UI フォント");
        FontScaleLabel.Text = LocalizationService.T("字級比例縮放:", "字级比例缩放:", "Base Font Scale:", "フォント倍率:");
        CustomFontBox.Header = LocalizationService.T("自訂字型家族名稱", "自定义字体家族名称", "Custom Font Family Name", "カスタムフォント名");
        CustomFontBox.PlaceholderText = LocalizationService.T(
            "例如: Cascadia Code, Microsoft JhengHei UI, Segoe UI Variable",
            "例如: Cascadia Code, Microsoft YaHei UI, Segoe UI Variable",
            "e.g. Cascadia Code, Segoe UI Variable",
            "例: Meiryo UI, Cascadia Code, Segoe UI Variable");
        FontPreviewHeader.Text = LocalizationService.T("字型即時預覽效果:", "字体实时预览效果:", "Typography Live Preview:", "タイポグラフィ プレビュー:");

        // Section 0.2: Workspace Presets
        WorkspaceCardTitle.Text = LocalizationService.Instance["WorkspacePresets"];
        WorkspaceCardDesc.Text = LocalizationService.T(
            "依據目前工作情境一鍵切換分頁優先級與工作區，提供如專業軟體般的佈局體驗。",
            "依据当前工作情境一键切换分页优先级与工作区，提供如专业软件般的布局体验。",
            "Quickly switch tab priorities and layouts based on your current workflow.",
            "作業内容に応じてタブの優先順位とワークスペースを素早く切り替えます。");
        WorkspacePresetBox.Header = LocalizationService.T("工作區佈局預設檔", "工作区布局预设", "Workspace Preset", "ワークスペース プリセット");

        // Section 1: Appearance & Scale
        AppearanceHeader.Text = LocalizationService.T("🎨 外觀與顯示", "🎨 外观与显示", "🎨 Appearance & Scaling", "🎨 外観とスケーリング");
        ThemeBox.Header = LocalizationService.T("應用程式主題", "应用程序主题", "Application Theme", "アプリテーマ");
        ScaleBox.Header = LocalizationService.T("介面縮放比例", "界面缩放比例", "UI Scale %", "UI スケール %");
        AutoDetectScaleBtn.Content = LocalizationService.T("⚡ 自動偵測最適縮放", "⚡ 自动检测最佳缩放", "⚡ Auto Detect Optimal Scale", "⚡ 最適スケールを自動検出");

        // Section 2: Custom Background & Companion Avatar
        BackgroundCardTitle.Text = LocalizationService.T("🎭 自訂背景圖片與桌布小助手", "🎭 自定义背景图片与壁纸小助手", "🎭 Custom Background & Companion Avatar", "🎭 カスタム背景とコンパニオン アバター");
        WallpaperTitle.Text = LocalizationService.T("自訂桌面背景圖", "自定义桌面背景图", "Custom Desktop Wallpaper", "カスタム デスクトップ壁紙");
        BackgroundImagePathBox.PlaceholderText = LocalizationService.T("請選擇圖片檔案路徑", "请选择图片文件路径", "Select image file path", "画像ファイル パスを選択");
        BrowseBackgroundBtn.Content = LocalizationService.T("📁 瀏覽圖片...", "📁 浏览图片...", "📁 Browse...", "📁 参照...");
        ClearBackgroundBtn.Content = LocalizationService.T("❌ 清除背景", "❌ 清除背景", "❌ Clear Background", "❌ 背景をクリア");
        WallpaperOpacityLabel.Text = LocalizationService.T("桌布底圖不透明度:", "壁纸底图不透明度:", "Wallpaper Opacity:", "壁紙の不透明度:");
        GlassTransparencyToggle.Header = LocalizationService.T("💎 啟用卡片磨砂半透明模式", "💎 启用卡片磨砂半透明模式", "💎 Enable Frosted Glass Transparency", "💎 すりガラス半透明モードを有効化");
        GlassTransparencyToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        GlassTransparencyToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        CardOpacityLabel.Text = LocalizationService.T("卡片不透明度:", "卡片不透明度:", "Card Opacity:", "カード不透明度:");
        CompanionAvatarToggle.Header = LocalizationService.T("啟用桌布互動小助手", "启用壁纸互动小助手", "Enable Interactive Companion Avatar", "インタラクティブ コンパニオンを有効化");
        CompanionAvatarToggle.OffContent = LocalizationService.T("已關閉", "已关闭", "Disabled", "無効");
        CompanionAvatarToggle.OnContent = LocalizationService.T("已開啟", "已开启", "Enabled", "有効");
        CompanionTypeBox.Header = LocalizationService.T("模型架構類型", "模型架构类型", "Model Architecture", "モデル アーキテクチャ");
        CompanionModelPathBox.Header = LocalizationService.T("自訂模型檔案", "自定义模型文件", "Custom Model File", "カスタム モデル ファイル");
        CompanionModelPathBox.PlaceholderText = LocalizationService.T("空白時使用內建經典小助手", "空白时使用内置经典小助手", "Leave blank to use built-in companion", "空欄の場合はビルトイン コンパニオンを使用");
        BrowseCompanionModelBtn.Content = LocalizationService.T("📂 選擇模型...", "📂 选择模型...", "📂 Select Model...", "📂 モデルを選択...");

        // Section 3: Safety & Confirmations
        SafetyHeader.Text = LocalizationService.T("🛡️ 安全與防呆機制", "🛡️ 安全与防呆机制", "🛡️ Safety & Protections", "🛡️ セキュリティと保護機能");
        SafetyConfirmationsToggle.Header = LocalizationService.T("破壞性操作二次安全確認", "破坏性操作二次安全确认", "Destructive Operation Confirmations", "破壊的操作の確認ダイアログ");
        SafetyConfirmationsToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        SafetyConfirmationsToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        SafetyDescText.Text = LocalizationService.T(
            "建議保持啟用，可防止誤點清除、刪除磁碟分區或重置 Windows 更新備份組件。",
            "建议保持启用，可防止误点清空、删除磁盘分区或重置 Windows 更新备份组件。",
            "Recommended to prevent accidental disk clearing, partition deletion, or component resets.",
            "誤操作によるディスク消去、パーティション削除、コンポーネント初期化を防止するため有効推奨。");

        // Section 4: Prompt Windows & Diagnostics
        PromptCardTitle.Text = LocalizationService.T("🔔 彈出視窗提示與系統除錯診斷", "🔔 弹出窗口提示与系统调试诊断", "🔔 Prompt Windows & System Diagnostics", "🔔 ポップアップ通知とシステム診断");
        PromptWindowsToggle.Header = LocalizationService.T("彈出式模態提示視窗", "弹出式模态提示窗口", "Modal Prompt Dialogs", "モーダル通知ダイアログ");
        PromptWindowsToggle.OffContent = LocalizationService.T("僅頂部橫幅", "仅顶部横幅", "Top Banner Only", "トップバナーのみ");
        PromptWindowsToggle.OnContent = LocalizationService.T("主動彈出對話框", "主动弹出对话框", "Pop-up Dialog", "ポップアップ表示");
        TestPromptDialogBtn.Content = LocalizationService.Instance["TestPromptDialog"];
        DiagnosticsTitle.Text = LocalizationService.T("系統深度除錯與診斷報告匯出", "系统深度调试与诊断报告导出", "Export Debug & Diagnostics Report", "システム診断レポートのエクスポート");
        DiagnosticsDesc.Text = LocalizationService.T(
            "一鍵收集本機 OS 版本、CPU、記憶體、硬碟型號、S.M.A.R.T. 健康、更新服務、TCP 堆疊與近期防護日誌，輸出為完整 .txt 診斷報告。",
            "一键收集本机 OS 版本、CPU、内存、硬盘型号、S.M.A.R.T. 健康、更新服务、TCP 协议栈与近期防护日志，输出为完整 .txt 诊断报告。",
            "Collect OS, CPU, RAM, disk specs, S.M.A.R.T., update status, and TCP stack to a diagnostic report.",
            "OS、CPU、RAM、ディスク仕様、S.M.A.R.T.、更新状態、TCP スタックを診断レポートに出力します。");
        ExportDiagnosticsBtn.Content = LocalizationService.Instance["ExportDiag"];

        // Section 5: Defaults for Partitions & Disks
        DefaultsHeader.Text = LocalizationService.T("💾 磁碟與部署預設值", "💾 磁盘与部署默认值", "💾 Disk & Deployment Defaults", "💾 ディスクおよび展開の既定値");
        PartitionStyleBox.Header = LocalizationService.T("預設分割表樣式", "默认分区表样式", "Default Partition Style", "既定のパーティション スタイル");
        FileSystemBox.Header = LocalizationService.T("預設檔案系統格式", "默认文件系统格式", "Default File System", "既定のファイルシステム");
        DefaultBackupFolderBox.Header = LocalizationService.T("預設備份與匯出目錄", "默认备份与导出目录", "Default Backup Directory", "既定のバックアップ ディレクトリ");

        // Section 6: Global Internet Download & Proxy
        DownloadCardTitle.Text = LocalizationService.T("🌐 全域網際網路下載與代理加速", "🌐 全局互联网下载与代理加速", "🌐 Global Internet Download & Proxy", "🌐 ダウンロードとプロキシ設定");
        GlobalDownloadPathBox.Header = LocalizationService.T("全域預設 ISO 下載目錄", "全局默认 ISO 下载目录", "Default ISO Download Directory", "既定の ISO ダウンロード ディレクトリ");
        BrowseGlobalDownloadBtn.Content = LocalizationService.T("📁 瀏覽目錄...", "📁 浏览目录...", "📁 Browse...", "📁 参照...");
        ParallelThreadsBox.Header = LocalizationService.T("預設並行連線數", "默认并行连接数", "Parallel Connection Threads", "並行スレッド数");
        ChunkBufferSizeBox.Header = LocalizationService.T("預設分塊快取大小", "默认分块缓存大小", "Chunk Buffer Size", "チャンク バッファ サイズ");
        HttpRangeToggle.Header = LocalizationService.T("預設啟用 HTTP Range 多線程加速", "默认启用 HTTP Range 多线程加速", "HTTP Range Multi-threaded Download", "HTTP Range マルチスレッド高速化");
        HttpRangeToggle.OffContent = LocalizationService.T("單線程", "单线程", "Single-thread", "シングルスレッド");
        HttpRangeToggle.OnContent = LocalizationService.T("多線程並行", "多线程并行", "Multi-threaded", "マルチスレッド並行");
        KeepResumeCacheToggle.Header = LocalizationService.T("保留斷點續傳快取檔", "保留断点续传缓存文件", "Keep Resume Cache Files", "レジューム キャッシュを保持");
        KeepResumeCacheToggle.OffContent = LocalizationService.T("取消時刪除", "取消时删除", "Delete on cancel", "キャンセル時に削除");
        KeepResumeCacheToggle.OnContent = LocalizationService.T("保留以供續傳", "保留以供续传", "Keep for resume", "再開用に保持");
        ProxyTitle.Text = LocalizationService.T("網路連線代理伺服器", "网络连接代理服务器", "Network Proxy Configuration", "プロキシ サーバー設定");
        ProxyModeBox.Header = LocalizationService.T("代理模式", "代理模式", "Proxy Mode", "プロキシ モード");
        CustomProxyUrlBox.Header = LocalizationService.T("自訂代理網址", "自定义代理网址", "Custom Proxy URL", "カスタム プロキシ URL");
        CustomProxyUrlBox.PlaceholderText = "http://127.0.0.1:7890";

        // Section 7: TCP Network Stack Optimization
        TcpCardTitle.Text = LocalizationService.T("⚡ TCP 網路協定堆疊最佳化", "⚡ TCP 网络协议栈优化", "⚡ TCP Network Stack Optimizer", "⚡ TCP ネットワーク スタック最適化");
        RefreshTcpBtn.Content = LocalizationService.T("🔄 重新整理狀態", "🔄 刷新状态", "🔄 Refresh Status", "🔄 状態を更新");
        TcpAutoTuningBox.Header = LocalizationService.T("接收視窗自動微調", "接收窗口自动微调", "AutoTuning Level", "受信ウィンドウ自動調整");
        TcpCongestionBox.Header = LocalizationService.T("擁塞控制演算法", "拥塞控制算法", "Congestion Provider", "輻輳制御プロバイダー");
        TcpEcnToggle.Header = LocalizationService.T("ECN 顯式擁塞通知", "ECN 显式拥塞通知", "ECN", "ECN 輻輳通知");
        TcpEcnToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        TcpEcnToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        TcpRssToggle.Header = LocalizationService.T("RSS 網卡多核心佇列分流", "RSS 网卡多核心队列分流", "RSS", "RSS ネットワーク負荷分散");
        TcpRssToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        TcpRssToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        TcpRscToggle.Header = LocalizationService.T("RSC 封包硬體分段合併卸載", "RSC 数据包硬件分段合并卸载", "RSC", "RSC パケット統合オフロード");
        TcpRscToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        TcpRscToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        TcpTimestampsToggle.Header = LocalizationService.T("關閉 RFC 1323 時間戳", "关闭 RFC 1323 时间戳", "Disable RFC 1323 Timestamps", "RFC 1323 タイムスタンプ無効化");
        TcpTimestampsToggle.OffContent = LocalizationService.T("保留時間戳", "保留时间戳", "Keep Timestamps", "タイムスタンプ保持");
        TcpTimestampsToggle.OnContent = LocalizationService.T("關閉時間戳", "关闭时间戳", "Disable Timestamps", "タイムスタンプ無効");
        ApplyTcpPresetBtn.Content = LocalizationService.T("⚡ 一鍵套用極速下載與電競網路", "⚡ 一键应用极速下载与电竞网络", "⚡ Apply High-Throughput & Gaming Preset", "⚡ 高速ダウンロード＆ゲーム用プリセット適用");
        RestoreTcpDefaultsBtn.Content = LocalizationService.T("🔄 還原 Windows 官方網路預設值", "🔄 还原 Windows 官方网络默认值", "🔄 Restore Windows Default Network Settings", "🔄 Windows 既定のネットワーク設定に復元");
        FlushDnsBtn.Content = LocalizationService.T("🌐 清除 DNS 快取", "🌐 清除 DNS 缓存", "🌐 Flush DNS Cache", "🌐 DNS キャッシュをクリア");

        // Section 7.1: System Tray & Notifications
        TrayCardTitle.Text = LocalizationService.T("📥 系統匣後台與操作反饋", "📥 系统托盘后台与操作反馈", "📥 System Tray & Notifications", "📥 システムトレイと通知");
        TrayCardDesc.Text = LocalizationService.T(
            "設定最小化至系統匣、任務完成工作列閃爍與 Windows 系統音效提示。",
            "设置最小化至系统托盘、任务完成任务栏闪烁与 Windows 系统音效提示。",
            "Configure minimize to system tray, taskbar flashing on completion, and audio feedback.",
            "システムトレイへの最小化、タスク完了時のタスクバー点滅、Windows 効果音を設定します。");
        MinimizeToTrayToggle.Header = LocalizationService.T("視窗最小化時縮小至系統匣", "窗口最小化时缩小至系统托盘", "Minimize to System Tray", "最小化時にシステムトレイへ格納");
        MinimizeToTrayToggle.OffContent = LocalizationService.T("一般最小化", "一般最小化", "Standard Minimize", "通常最小化");
        MinimizeToTrayToggle.OnContent = LocalizationService.T("縮小至系統匣", "缩小至系统托盘", "Minimize to Tray", "トレイへ最小化");
        CloseToTrayToggle.Header = LocalizationService.T("點擊關閉 (X) 時縮小至系統匣", "点击关闭 (X) 时缩小至系统托盘", "Close Button (X) Minimizes to Tray", "閉じる (X) 時にトレイへ格納");
        CloseToTrayToggle.OffContent = LocalizationService.T("直接退出", "直接退出", "Exit Application", "アプリ終了");
        CloseToTrayToggle.OnContent = LocalizationService.T("後台保留", "后台保留", "Keep in Tray", "トレイに常駐");
        TaskbarFlashToggle.Header = LocalizationService.T("任務完成或警告時閃爍工作列", "任务完成或警告时闪烁任务栏", "Flash Taskbar on Warning & Finish", "警告・完了時にタスクバーを点滅");
        TaskbarFlashToggle.OffContent = LocalizationService.T("已停用", "已停用", "Disabled", "無効");
        TaskbarFlashToggle.OnContent = LocalizationService.T("已啟用", "已启用", "Enabled", "有効");
        AudioFeedbackToggle.Header = LocalizationService.T("操作成功與完成播放提示音效", "操作成功与完成播放提示音效", "Play System Sounds on Completion", "完了時にシステム効果音を再生");
        AudioFeedbackToggle.OffContent = LocalizationService.T("靜音", "静音", "Mute", "ミュート");
        AudioFeedbackToggle.OnContent = LocalizationService.T("音效啟用", "音效启用", "Enabled", "有効");

        // Section 7.2: Auto Updater
        UpdaterCardTitle.Text = LocalizationService.T("🚀 自動更新與版本管理", "🚀 自动更新与版本管理", "🚀 Auto Updater & Version Control", "🚀 自動更新とバージョン管理");
        UpdaterCardDesc.Text = LocalizationService.T(
            "透過 GitHub Releases 檢查最新版本，可自由設定啟動自動檢查或手動點擊更新。",
            "通过 GitHub Releases 检查最新版本，可自由设定启动自动检查或手动点击更新。",
            "Check for newer releases via GitHub Releases with automatic or manual updates.",
            "GitHub Releases から最新バージョンを確認し、自動または手動でアップデートします。");
        AutoCheckUpdatesToggle.Header = LocalizationService.T("自動檢查版本更新", "自动检查版本更新", "Auto-check for Updates", "自動更新確認");
        AutoCheckUpdatesToggle.OffContent = LocalizationService.T("僅手動檢查", "仅手动检查", "Manual Only", "手動のみ");
        AutoCheckUpdatesToggle.OnContent = LocalizationService.T("自動檢查", "自动检查", "Automatic", "自動");
        UpdateFrequencyBox.Header = LocalizationService.T("檢查更新頻率", "检查更新频率", "Check Frequency", "確認頻度");
        CheckUpdateBtn.Content = LocalizationService.T("🔍 立即檢查更新", "🔍 立即检查更新", "🔍 Check for Updates", "🔍 アップデートを確認");
        DownloadUpdateBtn.Content = LocalizationService.T("🌐 下載最新版本", "🌐 下载最新版本", "🌐 Download Latest Release", "🌐 最新版をダウンロード");

        // Section 8: Maintenance & Cache
        MaintenanceHeader.Text = LocalizationService.T("🧹 維護與暫存清理", "🧹 维护与临时清理", "🧹 Maintenance & Cache Cleanup", "🧹 メンテナンスとキャッシュ削除");
        CleanTempCacheBtn.Content = LocalizationService.T("🧹 清理暫存腳本檔案", "🧹 清理临时脚本文件", "🧹 Clean Temporary Cache Files", "🧹 一時スクリプト ファイルを削除");
        ResetDefaultsBtn.Content = LocalizationService.T("🔄 恢復預設設定", "🔄 恢复默认设置", "🔄 Reset to Defaults", "🔄 既定値にリセット");

        // Section 9: About
        AboutHeader.Text = LocalizationService.T("ℹ️ 軟體與系統環境資訊", "ℹ️ 软件与系统环境信息", "ℹ️ System & Environment Info", "ℹ️ システム環境情報");

        // Save Button
        SaveSettingsBtn.Content = LocalizationService.T("💾 儲存並套用設定", "💾 保存并应用设置", "💾 Save Settings", "💾 設定を保存して適用");
    }

    private void CustomFontScaleSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CustomFontScale = e.NewValue;
            UpdateFontPreview();
        }
    }

    private async void TestPromptDialog_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (this.XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = LocalizationService.Instance["TestPromptDialog"],
            Content = LocalizationService.T(
                "這是一個彈出式模態提示視窗 (Prompt ContentDialog)！當您在設定中啟用此選項時，所有重大錯誤、高危磁碟抹除與完成通知皆會以此視窗向您主動報告。",
                "这是一个弹出式模态提示窗口 (Prompt ContentDialog)！当您在设置中启用此选项时，所有重大错误、高危磁盘清空与完成通知均会以此窗口向您主动报告。",
                "This is a modal Prompt Window (ContentDialog)! When enabled, critical errors, destructive disk warnings, and completion notifications will pop up directly.",
                "これはポップアップモーダル通知ダイアログ (Prompt ContentDialog) です！設定でこの項目を有効化すると、重大なエラーやディスク消去、完了通知が直接このダイアログで表示されます。"),
            CloseButtonText = LocalizationService.T("確定", "确定", "OK", "確定"),
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
