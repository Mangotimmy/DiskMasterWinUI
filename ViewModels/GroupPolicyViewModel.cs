using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class GroupPolicyViewModel : ObservableObject
{
    private readonly GroupPolicyService _service = new();

    [ObservableProperty] private string _editionName = "";
    [ObservableProperty] private string _editionId = "";
    [ObservableProperty] private bool _isHomeEdition;
    [ObservableProperty] private bool _isGpeditAvailable;
    [ObservableProperty] private bool _isInstallingGpedit;
    [ObservableProperty] private string _installProgressMessage = "";
    [ObservableProperty] private string _statusMessage = "就緒";
    [ObservableProperty] private string _consoleLog = "";

    // 8 Visual Group Policies
    [ObservableProperty] private bool _isDisableAutoReboot;
    [ObservableProperty] private bool _isDisableTelemetry;
    [ObservableProperty] private bool _isDisableSearchBoxSuggestions;
    [ObservableProperty] private bool _isDisableConsumerFeatures;
    [ObservableProperty] private bool _isDisableDeliveryOptimization;
    [ObservableProperty] private bool _isDisableUacSecureDesktop;
    [ObservableProperty] private bool _isDisableDefenderSampleSubmission;
    [ObservableProperty] private bool _isForceHags;

    public string GpeditStatusText => IsGpeditAvailable ? "🟢 本機已安裝啟用 gpedit.msc" : "⚠️ 尚未安裝 gpedit.msc (Windows 家用版)";
    public string GpeditStatusBadge => IsGpeditAvailable ? "已啟用" : "未啟用";

    public GroupPolicyViewModel()
    {
        RefreshStatus();
    }

    [RelayCommand]
    public void RefreshStatus()
    {
        var (id, name, isHome) = _service.GetWindowsEdition();
        EditionId = id;
        EditionName = name;
        IsHomeEdition = isHome;
        IsGpeditAvailable = _service.IsGpeditAvailable();

        IsDisableAutoReboot = _service.GetDisableAutoRebootPolicy();
        IsDisableTelemetry = _service.GetDisableTelemetryPolicy();
        IsDisableSearchBoxSuggestions = _service.GetDisableSearchBoxSuggestionsPolicy();
        IsDisableConsumerFeatures = _service.GetDisableConsumerFeaturesPolicy();
        IsDisableDeliveryOptimization = _service.GetDisableDeliveryOptimizationPolicy();
        IsDisableUacSecureDesktop = _service.GetDisableUacSecureDesktopPolicy();
        IsDisableDefenderSampleSubmission = _service.GetDisableDefenderSampleSubmissionPolicy();
        IsForceHags = _service.GetForceHagsPolicy();

        OnPropertyChanged(nameof(GpeditStatusText));
        OnPropertyChanged(nameof(GpeditStatusBadge));
    }

    [RelayCommand]
    public void OpenGpedit()
    {
        _service.OpenGpedit();
    }

    [RelayCommand]
    public async Task RunGpupdateAsync()
    {
        StatusMessage = "正在執行 gpupdate /force 強制更新群組原則...";
        string output = await _service.RunGpupdateAsync();
        StatusMessage = "群組原則強制重新整理完成。";
        AppendLog(output);
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task InstallGpeditAsync()
    {
        if (IsInstallingGpedit) return;
        IsInstallingGpedit = true;
        InstallProgressMessage = "正在初始化 DISM 群組原則安裝程式...";
        AppendLog("🚀 開始為 Windows 家用版安裝本機群組原則編輯器 (gpedit.msc)...");

        var progress = new Progress<string>(msg =>
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                InstallProgressMessage = msg;
                AppendLog(msg);
            });
        });

        var (ok, resultMsg) = await _service.InstallGpeditOnHomeEditionAsync(progress);
        IsInstallingGpedit = false;
        StatusMessage = resultMsg;
        AppendLog(resultMsg);

        if (ok)
        {
            AudioFeedbackService.PlaySuccess();
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
        RefreshStatus();
    }

    [RelayCommand]
    public void ToggleDisableAutoReboot()
    {
        IsDisableAutoReboot = !IsDisableAutoReboot;
        _service.SetDisableAutoRebootPolicy(IsDisableAutoReboot);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛑 禁止 Windows Update 自動重啟: {(IsDisableAutoReboot ? "已啟用" : "已關閉")}");
    }

    [RelayCommand]
    public void ToggleDisableTelemetry()
    {
        IsDisableTelemetry = !IsDisableTelemetry;
        _service.SetDisableTelemetryPolicy(IsDisableTelemetry);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛑 停用 Windows 診斷與遙測收集: {(IsDisableTelemetry ? "已停用" : "已開啟")}");
    }

    [RelayCommand]
    public void ToggleDisableSearchBoxSuggestions()
    {
        IsDisableSearchBoxSuggestions = !IsDisableSearchBoxSuggestions;
        _service.SetDisableSearchBoxSuggestionsPolicy(IsDisableSearchBoxSuggestions);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛑 關閉開始功能表 Bing 搜尋建議: {(IsDisableSearchBoxSuggestions ? "已關閉" : "已開啟")}");
    }

    [RelayCommand]
    public void ToggleDisableConsumerFeatures()
    {
        IsDisableConsumerFeatures = !IsDisableConsumerFeatures;
        _service.SetDisableConsumerFeaturesPolicy(IsDisableConsumerFeatures);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛑 停用微軟消費者體驗 (廣告與推薦APP): {(IsDisableConsumerFeatures ? "已停用" : "已開啟")}");
    }

    [RelayCommand]
    public void ToggleDisableDeliveryOptimization()
    {
        IsDisableDeliveryOptimization = !IsDisableDeliveryOptimization;
        _service.SetDisableDeliveryOptimizationPolicy(IsDisableDeliveryOptimization);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛑 停用傳遞最佳化 P2P 頻寬佔用: {(IsDisableDeliveryOptimization ? "已停用" : "已開啟")}");
    }

    [RelayCommand]
    public void ToggleDisableUacSecureDesktop()
    {
        IsDisableUacSecureDesktop = !IsDisableUacSecureDesktop;
        _service.SetDisableUacSecureDesktopPolicy(IsDisableUacSecureDesktop);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ 關閉 UAC 暗屏桌面彈窗: {(IsDisableUacSecureDesktop ? "已關閉暗屏" : "原生暗屏")}");
    }

    [RelayCommand]
    public void ToggleDisableDefenderSampleSubmission()
    {
        IsDisableDefenderSampleSubmission = !IsDisableDefenderSampleSubmission;
        _service.SetDisableDefenderSampleSubmissionPolicy(IsDisableDefenderSampleSubmission);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ 停用 Defender 雲端自動上傳樣本: {(IsDisableDefenderSampleSubmission ? "已停用" : "已開啟")}");
    }

    [RelayCommand]
    public void ToggleForceHags()
    {
        IsForceHags = !IsForceHags;
        _service.SetForceHagsPolicy(IsForceHags);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎮 強制開啟硬體加速 GPU 排程 (HAGS): {(IsForceHags ? "已啟用 (需重啟)" : "已關閉")}");
    }

    private void AppendLog(string message)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            ConsoleLog += $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
        });
    }
}
