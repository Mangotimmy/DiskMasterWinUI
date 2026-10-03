using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

/// <summary>
/// ViewModel for the Beginner Starter Hub (Easy Mode), providing 1-click health check,
/// cleaning, game boosting, system integrity repair, hibernation space reclaim, and OneDrive uninstall.
/// </summary>
public partial class StarterViewModel : ObservableObject
{
    private readonly SystemOptimizerService _optimizer = new();
    private readonly PowerCfgService _powerCfg = new();
    private readonly OneDriveService _oneDriveService = new();

    [ObservableProperty] private int _healthScore = 85;
    [ObservableProperty] private string _healthRatingText = "系統狀態良好";
    [ObservableProperty] private string _healthSummaryText = "點擊「一鍵電腦全面體檢」評估系統效能與健康指標。";
    [ObservableProperty] private bool _isOperating;
    [ObservableProperty] private string _operationStatus = "就緒";

    // ── OneDrive Status in Starter Page ──
    [ObservableProperty] private bool _isOneDriveInstalled;
    [ObservableProperty] private string _oneDriveStatusText = "正在檢測 OneDrive...";

    // ── Quick Metrics ──
    [ObservableProperty] private string _cpuStatusText = "標準排程";
    [ObservableProperty] private string _ramStatusText = "運作中";
    [ObservableProperty] private string _diskReclaimedText = "0 GB";

    public StarterViewModel()
    {
        _ = RefreshStatusAsync();
    }

    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        try
        {
            IsOperating = true;
            OperationStatus = "正在評估系統整體健康狀態...";

            var odStatus = await _oneDriveService.DetectOneDriveStatusAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsOneDriveInstalled = odStatus.IsInstalled;
                OneDriveStatusText = odStatus.IsInstalled
                    ? "☁️ 偵測到已安裝 OneDrive (佔用背景與桌面路徑)"
                    : "🟢 系統未安裝 OneDrive (乾淨極速)";
            });

            // Calculate Score
            int score = 100;
            if (odStatus.IsInstalled) score -= 10;
            if (odStatus.IsFoldersRedirected) score -= 5;
            if (!_optimizer.GetNetworkThrottlingDisabled()) score -= 5;
            if (_optimizer.GetSystemResponsiveness() > 0) score -= 5;

            HealthScore = Math.Clamp(score, 20, 100);
            HealthRatingText = HealthScore >= 90 ? "🟢 極致最佳化 (Optimal)" : HealthScore >= 75 ? "🟡 狀態良好 (Good)" : "🟠 建議調優 (Needs Tuning)";
            HealthSummaryText = $"電腦健康評分: {HealthScore} 分。已檢查排程、MMCSS、登錄檔與雲端綁架狀態。";
            OperationStatus = "體檢完成。";
        }
        catch (Exception ex)
        {
            OperationStatus = $"體檢發生例外: {ex.Message}";
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task OneClickGameBoostAsync()
    {
        try
        {
            IsOperating = true;
            OperationStatus = "正在套用一鍵電競極致加速方案...";
            var log = await _optimizer.ApplyGamingProfileAsync(38);
            AudioFeedbackService.PlaySuccess();
            OperationStatus = "電競加速方案套用完成！所有前台資源已全數釋放。";
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"加速失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task OneClickFreeSsdAsync()
    {
        try
        {
            IsOperating = true;
            OperationStatus = "正在清除休眠檔案以釋放 SSD 空間...";
            var (ok, gb, msg) = await _powerCfg.PurgeHibernationFileAndFreeSpaceAsync();
            OperationStatus = msg;
            if (ok)
            {
                DiskReclaimedText = $"{gb:F1} GB";
                AudioFeedbackService.PlaySuccess();
            }
            else AudioFeedbackService.PlayWarning();
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"釋放 SSD 空間失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task OneClickUninstallOneDriveAsync()
    {
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "新手一鍵徹底卸載 OneDrive",
            "即將徹底移除 OneDrive、關閉背景程序、自動將桌面/文件安全移回本機 %USERPROFILE% 原生目錄，並封鎖自動重新安裝。\n此操作安全且具備防護，確定執行嗎？",
            "UNINSTALL_ONEDRIVE");
        if (!confirmed) return;

        try
        {
            IsOperating = true;
            OperationStatus = "正在深度解除安裝 OneDrive 並還原原生路徑...";
            var (ok, msg) = await _oneDriveService.DeepUninstallOneDriveAsync(
                restoreShellFolders: true,
                cleanResiduals: true,
                blockReinstall: true);
            OperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"卸載失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task OneClickSystemRepairAsync()
    {
        try
        {
            IsOperating = true;
            OperationStatus = "正在執行 Windows 系統完整性修復 (SFC)...";
            var (stdout, _, code) = await ProcessHelper.RunProcessAsync("sfc.exe", "/scannow");
            OperationStatus = code == 0 ? "系統檔案檢查完成，無完整性違規或已修復。" : "SFC 掃描完成。";
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            OperationStatus = $"系統修復例外: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsOperating = false;
        }
    }
}
