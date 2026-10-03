using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Windows Memory Management Agent (MMAgent) configuration state.
/// </summary>
public partial class MMAgentConfig : ObservableObject
{
    [ObservableProperty]
    private bool _memoryCompression;

    [ObservableProperty]
    private bool _pageCombining;

    [ObservableProperty]
    private bool _applicationPreLaunch;

    [ObservableProperty]
    private bool _applicationLaunchProfiling;

    [ObservableProperty]
    private bool _operationRecording;

    /// <summary>
    /// Checks if the current configuration matches ultra-low-latency gaming profile (Compression=Off, PageCombining=Off).
    /// </summary>
    public bool IsGamingProfileActive => !MemoryCompression && !PageCombining;
}
