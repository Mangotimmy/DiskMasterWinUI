using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class BitLockerVolumeItem : ObservableObject
{
    [ObservableProperty] private string _mountPoint = ""; // e.g. "C:", "D:"
    [ObservableProperty] private string _volumeLabel = "";
    [ObservableProperty] private string _volumeType = "Fixed"; // "Operating System", "Fixed Data", "Removable"
    [ObservableProperty] private string _conversionStatus = "Fully Encrypted";
    [ObservableProperty] private double _percentageEncrypted = 100.0;
    [ObservableProperty] private string _encryptionMethod = "XTS-AES 128";
    [ObservableProperty] private string _protectionStatus = "Protection On"; // "Protection On", "Protection Off"
    [ObservableProperty] private string _lockStatus = "Unlocked"; // "Locked", "Unlocked"
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isProtected;
    [ObservableProperty] private string _keyProtectorSummary = "";
    [ObservableProperty] private string _recoveryPassword = "";

    public string DisplayTitle => string.IsNullOrEmpty(VolumeLabel)
        ? $"{MountPoint} (BitLocker)"
        : $"{MountPoint} [{VolumeLabel}]";

    public string StatusBadge => IsLocked ? "🔒 已鎖定 (Locked)" : (IsProtected ? "🛡️ 已保護 (Protected)" : "🔓 未受保護 (Not Protected)");
    public string StatusColor => IsLocked ? "#E81123" : (IsProtected ? "#107C41" : "#F7630C");
}
