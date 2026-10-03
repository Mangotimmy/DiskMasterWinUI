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
        bool isZh = LocalizationService.Instance.IsChinese;
        SettingsTitleText.Text = isZh ? "⚙️ 應用程式偏好設定" : "⚙️ Application Settings";
        SettingsSubtitleText.Text = isZh
            ? "自訂 DiskMaster Pro 介面外觀、全域縮放、安全防呆確認與磁碟預設行為。"
            : "Customize DiskMaster Pro appearance, UI scaling, safety protections, and disk defaults.";

        // Section 0: Language & Region
        LanguageCardTitle.Text = LocalizationService.Instance["LanguageSelect"] ?? (isZh ? "🌐 介面語言與地區" : "🌐 Language & Region");
        LanguageCardDesc.Text = isZh ? "選擇 DiskMaster Pro 顯示語言，全域介面即時無重啟切換。" : "Choose DiskMaster Pro display language with instant live reload.";
        LanguageBox.Header = isZh ? "選擇介面語言" : "UI Language";

        // Section 0.1: Fonts & Typography
        FontsCardTitle.Text = LocalizationService.Instance["CustomFonts"] ?? (isZh ? "🔤 自訂介面字型與排版" : "🔤 Custom UI Fonts & Typography");
        FontsCardDesc.Text = isZh ? "自由挑選喜愛的系統字型或手動輸入任何已安裝字型家族名稱，全視窗立即同步套用。" : "Select preferred system fonts or enter any installed font family name.";
        FontFamilyBox.Header = isZh ? "精選介面字型" : "Featured UI Fonts";
        FontScaleLabel.Text = isZh ? "字級比例縮放:" : "Base Font Scale:";
        CustomFontBox.Header = isZh ? "自訂字型家族名稱" : "Custom Font Family Name";
        CustomFontBox.PlaceholderText = isZh ? "例如: Cascadia Code, Microsoft JhengHei UI, Segoe UI Variable" : "e.g. Cascadia Code, Segoe UI Variable";
        FontPreviewHeader.Text = isZh ? "字型即時預覽效果:" : "Typography Live Preview:";

        // Section 0.2: Workspace Presets
        WorkspaceCardTitle.Text = LocalizationService.Instance["WorkspacePresets"] ?? (isZh ? "🗂️ 工作區佈局預設" : "🗂️ Workspace Layout Presets");
        WorkspaceCardDesc.Text = isZh ? "依據目前工作情境一鍵切換分頁優先級與工作區，提供如專業軟體般的佈局體驗。" : "Quickly switch tab priorities and layouts based on your current workflow.";
        WorkspacePresetBox.Header = isZh ? "工作區佈局預設檔" : "Workspace Preset";

        // Section 1: Appearance & Scale
        AppearanceHeader.Text = isZh ? "🎨 外觀與顯示" : "🎨 Appearance & Scaling";
        ThemeBox.Header = isZh ? "應用程式主題" : "Application Theme";
        ScaleBox.Header = isZh ? "介面縮放比例" : "UI Scale %";
        AutoDetectScaleBtn.Content = isZh ? "⚡ 自動偵測最適縮放" : "⚡ Auto Detect Optimal Scale";

        // Section 2: Custom Background & Companion Avatar
        BackgroundCardTitle.Text = isZh ? "🎭 自訂背景圖片與桌布小助手" : "🎭 Custom Background & Companion Avatar";
        WallpaperTitle.Text = isZh ? "自訂桌面背景圖" : "Custom Desktop Wallpaper";
        BackgroundImagePathBox.PlaceholderText = isZh ? "請選擇圖片檔案路徑" : "Select image file path";
        BrowseBackgroundBtn.Content = isZh ? "📁 瀏覽圖片..." : "📁 Browse...";
        ClearBackgroundBtn.Content = isZh ? "❌ 清除背景" : "❌ Clear Background";
        WallpaperOpacityLabel.Text = isZh ? "桌布底圖不透明度:" : "Wallpaper Opacity:";
        GlassTransparencyToggle.Header = isZh ? "💎 啟用卡片磨砂半透明模式" : "💎 Enable Frosted Glass Transparency";
        GlassTransparencyToggle.OffContent = isZh ? "已停用" : "Disabled";
        GlassTransparencyToggle.OnContent = isZh ? "已啟用" : "Enabled";
        CardOpacityLabel.Text = isZh ? "卡片不透明度:" : "Card Opacity:";
        CompanionAvatarToggle.Header = isZh ? "啟用桌布互動小助手" : "Enable Interactive Companion Avatar";
        CompanionAvatarToggle.OffContent = isZh ? "已關閉" : "Disabled";
        CompanionAvatarToggle.OnContent = isZh ? "已開啟" : "Enabled";
        CompanionTypeBox.Header = isZh ? "模型架構類型" : "Model Architecture";
        CompanionModelPathBox.Header = isZh ? "自訂模型檔案" : "Custom Model File";
        CompanionModelPathBox.PlaceholderText = isZh ? "空白時使用內建經典小助手" : "Leave blank to use built-in companion";
        BrowseCompanionModelBtn.Content = isZh ? "📂 選擇模型..." : "📂 Select Model...";

        // Section 3: Safety & Confirmations
        SafetyHeader.Text = isZh ? "🛡️ 安全與防呆機制" : "🛡️ Safety & Protections";
        SafetyConfirmationsToggle.Header = isZh ? "破壞性操作二次安全確認" : "Destructive Operation Confirmations";
        SafetyConfirmationsToggle.OffContent = isZh ? "已停用" : "Disabled";
        SafetyConfirmationsToggle.OnContent = isZh ? "已啟用" : "Enabled";
        SafetyDescText.Text = isZh ? "建議保持啟用，可防止誤點清除、刪除磁碟分區或重置 Windows 更新備份組件。" : "Recommended to prevent accidental disk clearing, partition deletion, or component resets.";

        // Section 4: Prompt Windows & Diagnostics
        PromptCardTitle.Text = isZh ? "🔔 彈出視窗提示與系統除錯診斷" : "🔔 Prompt Windows & System Diagnostics";
        PromptWindowsToggle.Header = isZh ? "彈出式模態提示視窗" : "Modal Prompt Dialogs";
        PromptWindowsToggle.OffContent = isZh ? "僅頂部橫幅" : "Top Banner Only";
        PromptWindowsToggle.OnContent = isZh ? "主動彈出對話框" : "Pop-up Dialog";
        TestPromptDialogBtn.Content = isZh ? "🧪 測試提示視窗" : "🧪 Test Dialog";
        DiagnosticsTitle.Text = isZh ? "系統深度除錯與診斷報告匯出" : "Export Debug & Diagnostics Report";
        DiagnosticsDesc.Text = isZh ? "一鍵收集本機 OS 版本、CPU、記憶體、硬碟型號、S.M.A.R.T. 健康、更新服務、TCP 堆疊與近期防護日誌，輸出為完整 .txt 診斷報告。" : "Collect OS, CPU, RAM, disk specs, S.M.A.R.T., update status, and TCP stack to a diagnostic report.";
        ExportDiagnosticsBtn.Content = isZh ? "📤 匯出系統除錯診斷日誌" : "📤 Export System Diagnostics";

        // Section 5: Defaults for Partitions & Disks
        DefaultsHeader.Text = isZh ? "💾 磁碟與部署預設值" : "💾 Disk & Deployment Defaults";
        PartitionStyleBox.Header = isZh ? "預設分割表樣式" : "Default Partition Style";
        FileSystemBox.Header = isZh ? "預設檔案系統格式" : "Default File System";
        DefaultBackupFolderBox.Header = isZh ? "預設備份與匯出目錄" : "Default Backup Directory";

        // Section 6: Global Internet Download & Proxy
        DownloadCardTitle.Text = isZh ? "🌐 全域網際網路下載與代理加速" : "🌐 Global Internet Download & Proxy";
        GlobalDownloadPathBox.Header = isZh ? "全域預設 ISO 下載目錄" : "Default ISO Download Directory";
        BrowseGlobalDownloadBtn.Content = isZh ? "📁 瀏覽目錄..." : "📁 Browse...";
        ParallelThreadsBox.Header = isZh ? "預設並行連線數" : "Parallel Connection Threads";
        ChunkBufferSizeBox.Header = isZh ? "預設分塊快取大小" : "Chunk Buffer Size";
        HttpRangeToggle.Header = isZh ? "預設啟用 HTTP Range 多線程加速" : "HTTP Range Multi-threaded Download";
        HttpRangeToggle.OffContent = isZh ? "單線程" : "Single-thread";
        HttpRangeToggle.OnContent = isZh ? "多線程並行" : "Multi-threaded";
        KeepResumeCacheToggle.Header = isZh ? "保留斷點續傳快取檔" : "Keep Resume Cache Files";
        KeepResumeCacheToggle.OffContent = isZh ? "取消時刪除" : "Delete on cancel";
        KeepResumeCacheToggle.OnContent = isZh ? "保留以供續傳" : "Keep for resume";
        ProxyTitle.Text = isZh ? "網路連線代理伺服器" : "Network Proxy Configuration";
        ProxyModeBox.Header = isZh ? "代理模式" : "Proxy Mode";
        CustomProxyUrlBox.Header = isZh ? "自訂代理網址" : "Custom Proxy URL";
        CustomProxyUrlBox.PlaceholderText = "http://127.0.0.1:7890";

        // Section 7: TCP Network Stack Optimization
        TcpCardTitle.Text = isZh ? "⚡ TCP 網路協定堆疊最佳化" : "⚡ TCP Network Stack Optimizer";
        RefreshTcpBtn.Content = isZh ? "🔄 重新整理狀態" : "🔄 Refresh Status";
        TcpAutoTuningBox.Header = isZh ? "接收視窗自動微調" : "AutoTuning Level";
        TcpCongestionBox.Header = isZh ? "擁塞控制演算法" : "Congestion Provider";
        TcpEcnToggle.Header = isZh ? "ECN 顯式擁塞通知" : "ECN";
        TcpEcnToggle.OffContent = isZh ? "已停用" : "Disabled";
        TcpEcnToggle.OnContent = isZh ? "已啟用" : "Enabled";
        TcpRssToggle.Header = isZh ? "RSS 網卡多核心佇列分流" : "RSS";
        TcpRssToggle.OffContent = isZh ? "已停用" : "Disabled";
        TcpRssToggle.OnContent = isZh ? "已啟用" : "Enabled";
        TcpRscToggle.Header = isZh ? "RSC 封包硬體分段合併卸載" : "RSC";
        TcpRscToggle.OffContent = isZh ? "已停用" : "Disabled";
        TcpRscToggle.OnContent = isZh ? "已啟用" : "Enabled";
        TcpTimestampsToggle.Header = isZh ? "關閉 RFC 1323 時間戳" : "Disable RFC 1323 Timestamps";
        TcpTimestampsToggle.OffContent = isZh ? "保留時間戳" : "Keep Timestamps";
        TcpTimestampsToggle.OnContent = isZh ? "關閉時間戳" : "Disable Timestamps";
        ApplyTcpPresetBtn.Content = isZh ? "⚡ 一鍵套用極速下載與電競網路" : "⚡ Apply High-Throughput & Gaming Preset";
        RestoreTcpDefaultsBtn.Content = isZh ? "🔄 還原 Windows 官方網路預設值" : "🔄 Restore Windows Default Network Settings";
        FlushDnsBtn.Content = isZh ? "🌐 清除 DNS 快取" : "🌐 Flush DNS Cache";

        // Section 8: Maintenance & Cache
        MaintenanceHeader.Text = isZh ? "🧹 維護與暫存清理" : "🧹 Maintenance & Cache Cleanup";
        CleanTempCacheBtn.Content = isZh ? "🧹 清理暫存腳本檔案" : "🧹 Clean Temporary Cache Files";
        ResetDefaultsBtn.Content = isZh ? "🔄 恢復預設設定" : "🔄 Reset to Defaults";

        // Section 9: About
        AboutHeader.Text = isZh ? "ℹ️ 軟體與系統環境資訊" : "ℹ️ System & Environment Info";

        // Save Button
        SaveSettingsBtn.Content = isZh ? "💾 儲存並套用設定" : "💾 Save Settings";
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
            Content = LocalizationService.Instance.IsChinese
                ? "這是一個彈出式模態提示視窗 (Prompt ContentDialog)！當您在設定中啟用此選項時，所有重大錯誤、高危磁碟抹除與完成通知皆會以此視窗向您主動報告。"
                : "This is a modal Prompt Window (ContentDialog)! When enabled, critical errors, destructive disk warnings, and completion notifications will pop up directly.",
            CloseButtonText = LocalizationService.Instance.IsChinese ? "確定" : "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
