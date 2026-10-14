using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

/// <summary>
/// Drives the Recovery page: volume picker, bounded scan (NTFS $MFT / signature carve / Recycle Bin),
/// result list with honest confidence ratings, destination guard, recover + cancel, and a log area.
/// </summary>
public partial class RecoveryViewModel : ObservableObject
{
    private readonly RecoveryService _service = new();
    private readonly List<RecoveredFileItem> _allItems = new();
    private readonly HashSet<RecoveredFileItem> _subscribed = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private RecoveryVolumeInfo? _selectedVolume;
    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string _destinationPath = "";
    [ObservableProperty] private string _statusMessage = "選擇要掃描的磁碟區（或映像檔），然後按「開始掃描」。";
    [ObservableProperty] private string _terminalOutput = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isRecovering;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _capabilityText = RecoveryService.CapabilitySummary;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _enableSignatureCarve;
    [ObservableProperty] private bool _preserveOriginalPaths = true;
    [ObservableProperty] private bool _includeRecycleBinFallback = true;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectedSizeText = "0 B";
    [ObservableProperty] private bool _isNotificationOpen;
    [ObservableProperty] private string _notificationMessage = "";
    [ObservableProperty] private bool _notificationIsError;

    public ObservableCollection<RecoveryVolumeInfo> Volumes { get; } = new();
    public ObservableCollection<RecoveredFileItem> Results { get; } = new();

    public bool IsBusy => IsScanning || IsRecovering;
    public bool HasResults => _allItems.Count > 0;
    public string AdminHint => IsAdmin
        ? "已以系統管理員身分執行：可直接解析 NTFS $MFT 刪除項目。"
        : "未以系統管理員身分執行：無法直接讀取原始磁碟區，將改用免提權的資源回收筒掃描（磁碟映像檔仍可完整解析）。";

    public RecoveryViewModel()
    {
        IsAdmin = AdminHelper.IsRunningAsAdmin();
        AppendOutput($"DiskMaster 檔案復原引擎啟動（管理員：{(IsAdmin ? "是" : "否")}）。");
        if (!IsAdmin) AppendOutput("[HINT] 需要 NTFS MFT 深度掃描時，請以系統管理員身分重新啟動；或使用磁碟映像檔（.img/.dd）進行離線復原。");
        RefreshVolumes();
    }

    partial void OnIsScanningChanged(bool value) => OnBusyChanged();
    partial void OnIsRecoveringChanged(bool value) => OnBusyChanged();
    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedVolumeChanged(RecoveryVolumeInfo? value)
    {
        if (value == null) return;
        if (string.IsNullOrWhiteSpace(TargetPath) || Volumes.Any(v => v.RootPath.Equals(TargetPath.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            TargetPath = value.RootPath;
        }
    }

    private void OnBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        ScanCommand.NotifyCanExecuteChanged();
        RecoverSelectedCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void ShowNotification(string message, bool isError)
    {
        NotificationMessage = message;
        NotificationIsError = isError;
        IsNotificationOpen = true;
    }

    private void AppendOutput(string text)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            if (TerminalOutput.Length > 200_000) TerminalOutput = TerminalOutput[^100_000..];
            TerminalOutput += $"{DateTime.Now:HH:mm:ss} {text}\n";
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Volumes / shell hand-off
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void RefreshVolumes()
    {
        try
        {
            var previous = SelectedVolume?.RootPath;
            Volumes.Clear();
            foreach (var volume in _service.EnumerateVolumes()) Volumes.Add(volume);
            SelectedVolume = Volumes.FirstOrDefault(v => v.RootPath.Equals(previous, StringComparison.OrdinalIgnoreCase))
                             ?? Volumes.FirstOrDefault(v => v.IsNtfs && v.DriveType == DriveType.Fixed)
                             ?? Volumes.FirstOrDefault();
            AppendOutput($"偵測到 {Volumes.Count} 個磁碟區。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"列舉磁碟區失敗：{ex.Message}";
            AppendOutput("[ERROR] " + ex.Message);
        }
    }

    /// <summary>
    /// Called by the page when Explorer (or <c>--recover</c>) handed over a path/volume.
    /// Selects the matching volume, keeps the path as a hint/filter and starts a scan.
    /// Never throws and never blocks on an unparsable path — it reports through StatusMessage instead.
    /// </summary>
    public void LoadTarget(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "右鍵載入的目標路徑是空的，請手動選擇磁碟區。";
                return;
            }

            var clean = path.Trim().Trim('"');
            if (Volumes.Count == 0) RefreshVolumes();

            var volumeRoot = RecoveryService.ResolveVolumeRoot(clean);
            var match = Volumes.FirstOrDefault(v => v.RootPath.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase));

            var isFile = false;
            try { isFile = File.Exists(clean); } catch { }
            var isDirectory = false;
            try { isDirectory = Directory.Exists(clean); } catch { }

            if (match != null) SelectedVolume = match;

            TargetPath = clean;
            FilterText = "";

            if (isFile) FilterText = System.IO.Path.GetFileName(clean);
            else if (isDirectory && clean.TrimEnd('\\').Length > 3) FilterText = System.IO.Path.GetFileName(clean.TrimEnd('\\'));

            if (match == null)
            {
                StatusMessage = $"找不到「{clean}」所屬的磁碟區 — 請手動選擇要掃描的磁碟區。";
                AppendOutput($"[SHELL] 無法對應磁碟區：{clean}（可能已移除或不是本機磁碟）");
                return;
            }

            StatusMessage = $"已由檔案總管右鍵載入目標：{clean}（磁碟區 {match.RootPath}）";
            AppendOutput($"[SHELL] LoadTarget(\"{clean}\") → {match.RootPath}；自動開始掃描。");
            AppendOutput(isFile
                ? "[SHELL] 目標是檔案，候選清單已用檔名預先過濾。"
                : isDirectory && clean.TrimEnd('\\').Length > 3
                    ? "[SHELL] 目標是資料夾，候選清單已用資料夾名稱預先過濾。"
                    : "[SHELL] 目標是磁碟機根目錄，將掃描整個磁碟區。");

            if (!IsBusy && ScanCommand.CanExecute(null)) ScanCommand.Execute(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入右鍵目標失敗：{ex.Message}";
            AppendOutput("[ERROR] LoadTarget: " + ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Scan
    // ══════════════════════════════════════════════════════════════════════

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        var target = string.IsNullOrWhiteSpace(TargetPath) ? SelectedVolume?.RootPath ?? "" : TargetPath.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = "請先選擇要掃描的磁碟區或輸入映像檔路徑。";
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            IsScanning = true;
            ProgressPercent = 0;
            ProgressText = "準備掃描…";
            StatusMessage = $"正在掃描 {target} …";
            _allItems.Clear();
            Results.Clear();
            UpdateSelectionSummary();
            SummaryText = "";

            var options = new RecoveryScanOptions
            {
                Mode = RecoveryScanMode.Auto,
                EnableSignatureCarve = EnableSignatureCarve,
                IncludeRecycleBin = IncludeRecycleBinFallback
            };

            AppendOutput($"[SCAN] 開始：{target}（簽章雕刻：{(EnableSignatureCarve ? "開" : "關")}，資源回收筒：{(IncludeRecycleBinFallback ? "開" : "關")}）");
            var progress = new Progress<RecoveryScanProgress>(p =>
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    ProgressPercent = Math.Clamp(p.Percent, 0, 100);
                    ProgressText = string.IsNullOrWhiteSpace(p.Detail) ? p.Phase : $"{p.Phase} — {p.Detail}";
                    if (!string.IsNullOrWhiteSpace(p.Detail)) StatusMessage = p.Detail;
                });
            });

            var result = await _service.ScanAsync(target, options, progress, AppendOutput, _cts.Token);

            foreach (var warning in result.Warnings) AppendOutput("[WARN] " + warning);
            foreach (var item in result.Items)
            {
                _allItems.Add(item);
                Subscribe(item);
            }

            ApplyFilter();
            CapabilityText = string.IsNullOrWhiteSpace(result.CapabilityReport) ? RecoveryService.CapabilitySummary : result.CapabilityReport;
            ProgressPercent = 100;
            ProgressText = result.Success ? $"完成：{result.Items.Count} 個候選項目" : "掃描結束（有錯誤）";

            if (!result.Success)
            {
                StatusMessage = result.Error;
                ShowNotification(result.Error, true);
                AudioFeedbackService.PlayError();
            }
            else
            {
                StatusMessage = $"掃描完成：找到 {result.Items.Count} 個候選項目（可復原 {_allItems.Count(i => i.IsRecoverable)} 個）。";
                SummaryText = $"{_allItems.Count(i => i.Confidence == RecoveryConfidence.High)} 個高信心、{_allItems.Count(i => i.Confidence == RecoveryConfidence.Medium)} 個中信心、{_allItems.Count(i => i.Confidence == RecoveryConfidence.Low)} 個低信心、{_allItems.Count(i => !i.IsRecoverable)} 個僅剩中繼資料。";
                if (_allItems.Count > 0) AudioFeedbackService.PlaySuccess();
            }
            AppendOutput($"[SCAN] {StatusMessage}");
            OnPropertyChanged(nameof(HasResults));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "掃描已取消。";
            AppendOutput("[SCAN] 使用者取消掃描。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"掃描失敗：{ex.Message}";
            AppendOutput("[ERROR] " + ex.Message);
            ShowNotification(StatusMessage, true);
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Recover
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RecoverSelectedAsync()
    {
        var selection = _allItems.Where(i => i.IsSelected).ToList();
        if (selection.Count == 0)
        {
            ShowNotification("尚未選取任何要復原的項目。", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            ShowNotification("請先選擇復原目的資料夾（不可與來源同一個磁碟區）。", true);
            return;
        }

        // Refuse up front — writing to the source volume is how recoveries destroy data.
        var guard = _service.CheckDestination(DestinationPath, selection);
        if (!guard.Ok)
        {
            StatusMessage = guard.Message;
            AppendOutput("[REFUSED] " + guard.Message);
            ShowNotification(guard.Message, true);
            AudioFeedbackService.PlayError();
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            IsRecovering = true;
            ProgressPercent = 0;
            ProgressText = "準備復原…";
            StatusMessage = $"正在將 {selection.Count} 個項目復原至 {DestinationPath} …";
            AppendOutput($"[RECOVER] 目的地：{DestinationPath}（保留原始路徑：{(PreserveOriginalPaths ? "是" : "否")}）");

            var progress = new Progress<RecoveryItemOutcome>(outcome =>
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    var done = outcome.Success ? "✅" : outcome.Skipped ? "⏭️" : "❌";
                    ProgressText = $"{done} {outcome.Item?.Name} — {outcome.Message}";
                    ProgressPercent = Math.Clamp(ProgressPercent + (100.0 / Math.Max(1, selection.Count)), 0, 100);
                });
            });

            var summary = await _service.RecoverAsync(selection, DestinationPath, PreserveOriginalPaths, progress, AppendOutput, _cts.Token);

            ProgressPercent = 100;
            ProgressText = summary.Headline;
            SummaryText = summary.Headline;
            StatusMessage = summary.Headline;
            AppendOutput("[SUMMARY] " + summary.Headline);

            foreach (var outcome in summary.Outcomes.Where(o => !o.Success))
            {
                AppendOutput($"  [{(outcome.Skipped ? "SKIP" : "FAIL")}] {outcome.Item?.Name} — {outcome.Message}");
            }

            ShowNotification(summary.Headline, summary.Refused || summary.Failed > 0);
            if (summary.Succeeded > 0) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "復原已取消（已寫入的檔案會保留，未處理的項目維持原狀）。";
            AppendOutput("[RECOVER] 使用者取消。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"復原失敗：{ex.Message}";
            AppendOutput("[ERROR] " + ex.Message);
            ShowNotification(StatusMessage, true);
        }
        finally
        {
            IsRecovering = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is { IsCancellationRequested: false })
        {
            _cts.Cancel();
            AppendOutput("[INFO] 已要求取消目前作業。");
            StatusMessage = "正在取消…";
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Selection / filtering
    // ══════════════════════════════════════════════════════════════════════

    private void Subscribe(RecoveredFileItem item)
    {
        if (!_subscribed.Add(item)) return;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecoveredFileItem.IsSelected))
        {
            DispatcherHelper.RunOnUIThread(UpdateSelectionSummary);
        }
    }

    private void UpdateSelectionSummary()
    {
        var selected = _allItems.Where(i => i.IsSelected).ToList();
        SelectedCount = selected.Count;
        var bytes = selected.Where(i => !i.IsDirectory).Sum(i => i.SizeBytes);
        SelectedSizeText = $"{RecoveryVolumeInfo.FormatBytes(bytes)}";
        OnPropertyChanged(nameof(SelectionText));
    }

    public string SelectionText => $"已選 {SelectedCount} 個項目，估計 {SelectedSizeText}";

    private void ApplyFilter()
    {
        var filter = (FilterText ?? "").Trim().ToLowerInvariant();
        Results.Clear();
        foreach (var item in _allItems)
        {
            if (filter.Length == 0 || item.SearchText.Contains(filter)) Results.Add(item);
        }
        UpdateSelectionSummary();
    }

    public IReadOnlyList<RecoveredFileItem> VisibleItems => Results.ToList();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Results) if (item.IsRecoverable) item.IsSelected = true;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in _allItems) item.IsSelected = false;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void SelectRecoverable()
    {
        foreach (var item in Results) item.IsSelected = item.IsRecoverable;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void ClearTerminal() => TerminalOutput = "";

    [RelayCommand]
    private void ClearResults()
    {
        foreach (var item in _allItems.Where(i => _subscribed.Contains(i))) item.PropertyChanged -= OnItemPropertyChanged;
        _subscribed.Clear();
        _allItems.Clear();
        Results.Clear();
        SummaryText = "";
        StatusMessage = "候選清單已清除。";
        UpdateSelectionSummary();
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private void DismissNotification() => IsNotificationOpen = false;
}
