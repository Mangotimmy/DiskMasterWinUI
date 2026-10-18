using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class RunningProcessPriorityItem : ObservableObject
{
    [ObservableProperty] private int _pid;
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _priorityClass = "Normal";
    [ObservableProperty] private long _memoryMb;
    [ObservableProperty] private string _executablePath = "";

    public string FormattedPriority => PriorityClass switch
    {
        "RealTime" => "⚡ 即時 (Realtime)",
        "High" => "🚀 高 (High)",
        "AboveNormal" => "⬆️ 高於標準 (AboveNormal)",
        "Normal" => "🟢 標準 (Normal)",
        "BelowNormal" => "⬇️ 低於標準 (BelowNormal)",
        "Idle" => "⚪ 閒置 (Idle)",
        _ => PriorityClass
    };
}

public partial class PersistedPriorityRuleItem : ObservableObject
{
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _priority = "High";
    [ObservableProperty] private int _ifeoValue = 3;
    [ObservableProperty] private string _status = "已寫入 IFEO (開機自動生效)";

    public string FormattedPriority => Priority switch
    {
        "RealTime" => "⚡ 即時 (Realtime)",
        "High" => "🚀 高 (High)",
        "AboveNormal" => "⬆️ 高於標準 (AboveNormal)",
        "Normal" => "🟢 標準 (Normal)",
        _ => Priority
    };
}
