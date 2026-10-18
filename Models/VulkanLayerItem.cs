namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing an audited Khronos Vulkan layer manifest registered in Windows Registry.
/// Supports both ExplicitLayers and ImplicitLayers across 64-bit and 32-bit (WOW6432Node) hives.
/// </summary>
public class VulkanLayerItem
{
    public string ManifestPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HiveName { get; set; } = string.Empty; // e.g. "HKLM (64-bit)", "HKLM (32-bit WOW6432Node)", "HKCU"
    public string RegistrySubKeyPath { get; set; } = string.Empty;
    public string LayerCategory { get; set; } = "Implicit"; // "Explicit" or "Implicit"
    public int RegistryValue { get; set; } // 0 = Active, 1 = Disabled
    public bool ExistsOnDisk { get; set; }

    public bool IsEnabled => RegistryValue == 0;
    public bool IsOrphaned => !ExistsOnDisk;

    public string StatusBadge => !ExistsOnDisk
        ? "⚠️ 檔案遺失 (幽靈殘留 - 易閃退)"
        : (IsEnabled ? "🟢 啟用中 (Active)" : "⚪ 已停用 (Disabled)");

    public string StatusBadgeColor => !ExistsOnDisk
        ? "#E81123"
        : (IsEnabled ? "#107C41" : "#797775");

    public string TypeBadge => LayerCategory == "Explicit"
        ? "🎯 Explicit (手動呼叫載入)"
        : "⚡ Implicit (全域自動注入)";
}
