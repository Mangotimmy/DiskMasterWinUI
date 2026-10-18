using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for auditing, managing, and optimizing Khronos Vulkan layer manifests and overlays.
/// Resolves VK_ERROR_LAYER_NOT_PRESENT (-6) startup crashes by purging orphaned manifests,
/// and provides Pure Vulkan Zero-Overlay Mode (VK_LOADER_LAYERS_DISABLE = "~implicit~")
/// for competitive gaming and minimum frame-time latency.
/// </summary>
public class VulkanOptimizerService
{
    private static readonly Lazy<VulkanOptimizerService> _instance = new(() => new VulkanOptimizerService());
    public static VulkanOptimizerService Instance => _instance.Value;

    private const string EnvVarName = "VK_LOADER_LAYERS_DISABLE";
    private const string EnvVarDisableImplicit = "~implicit~";

    private record LayerTarget(RegistryKey RootKey, string SubKeyPath, string HiveDisplayName, string Category);

    private static readonly LayerTarget[] LayerTargets =
    [
        new(Registry.LocalMachine, @"SOFTWARE\Khronos\Vulkan\ExplicitLayers", "HKLM (64-bit)", "Explicit"),
        new(Registry.LocalMachine, @"SOFTWARE\Khronos\Vulkan\ImplicitLayers", "HKLM (64-bit)", "Implicit"),
        new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Khronos\Vulkan\ExplicitLayers", "HKLM (32-bit WOW6432Node)", "Explicit"),
        new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Khronos\Vulkan\ImplicitLayers", "HKLM (32-bit WOW6432Node)", "Implicit"),
        new(Registry.CurrentUser, @"Software\Khronos\Vulkan\ExplicitLayers", "HKCU", "Explicit"),
        new(Registry.CurrentUser, @"Software\Khronos\Vulkan\ImplicitLayers", "HKCU", "Implicit")
    ];

    /// <summary>
    /// Scans all registered 64-bit, 32-bit, and user-level Vulkan layers, testing file existence and parsing JSON metadata.
    /// </summary>
    public async Task<List<VulkanLayerItem>> ScanAllLayersAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<VulkanLayerItem>();

            foreach (var target in LayerTargets)
            {
                try
                {
                    using var key = target.RootKey.OpenSubKey(target.SubKeyPath, writable: false);
                    if (key == null) continue;

                    foreach (var manifestPath in key.GetValueNames())
                    {
                        var rawVal = key.GetValue(manifestPath);
                        int dwordVal = rawVal is int i ? i : 0;
                        bool exists = File.Exists(manifestPath);

                        string name = Path.GetFileNameWithoutExtension(manifestPath);
                        string description = exists ? "Vulkan 圖形層" : "⚠️ 找不到實體 JSON 檔案 (殘留幽靈層)";

                        if (exists)
                        {
                            try
                            {
                                string jsonContent = File.ReadAllText(manifestPath);
                                using var doc = JsonDocument.Parse(jsonContent);
                                if (doc.RootElement.TryGetProperty("layer", out var layerElem))
                                {
                                    if (layerElem.TryGetProperty("name", out var n)) name = n.GetString() ?? name;
                                    if (layerElem.TryGetProperty("description", out var d)) description = d.GetString() ?? description;
                                }
                            }
                            catch
                            {
                                // Keep fallback name/description if JSON parse fails
                            }
                        }

                        results.Add(new VulkanLayerItem
                        {
                            ManifestPath = manifestPath,
                            Name = name,
                            Description = description,
                            HiveName = target.HiveDisplayName,
                            RegistrySubKeyPath = target.SubKeyPath,
                            LayerCategory = target.Category,
                            RegistryValue = dwordVal,
                            ExistsOnDisk = exists
                        });
                    }
                }
                catch
                {
                    // Ignore inaccessible registry hives
                }
            }

            return results;
        });
    }

    /// <summary>
    /// Purges all orphaned registry values where the target manifest JSON file no longer exists.
    /// Prevents VK_ERROR_LAYER_NOT_PRESENT (-6) and application startup crashes.
    /// </summary>
    public async Task<(int PurgedCount, List<string> PurgedPaths)> PurgeOrphanedLayersAsync()
    {
        return await Task.Run(() =>
        {
            int purged = 0;
            var paths = new List<string>();

            foreach (var target in LayerTargets)
            {
                try
                {
                    using var key = target.RootKey.OpenSubKey(target.SubKeyPath, writable: true);
                    if (key == null) continue;

                    foreach (var manifestPath in key.GetValueNames())
                    {
                        if (!File.Exists(manifestPath))
                        {
                            try
                            {
                                key.DeleteValue(manifestPath, throwOnMissingValue: false);
                                purged++;
                                paths.Add($"{target.HiveDisplayName}: {manifestPath}");
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return (purged, paths);
        });
    }

    /// <summary>
    /// Enables or disables a specific registered Vulkan layer manifest (0 = Active, 1 = Disabled).
    /// </summary>
    public async Task<bool> SetLayerStatusAsync(string hiveName, string subKeyPath, string manifestPath, bool enable)
    {
        return await Task.Run(() =>
        {
            try
            {
                var root = hiveName.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
                    ? Registry.CurrentUser
                    : Registry.LocalMachine;

                using var key = root.OpenSubKey(subKeyPath, writable: true);
                if (key != null)
                {
                    key.SetValue(manifestPath, enable ? 0 : 1, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { }
            return false;
        });
    }

    /// <summary>
    /// Checks whether Pure Vulkan Zero-Overlay Mode is currently active (VK_LOADER_LAYERS_DISABLE = "~implicit~").
    /// </summary>
    public bool IsPureVulkanModeEnabled()
    {
        try
        {
            string? userVal = Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.User);
            if (string.Equals(userVal?.Trim(), EnvVarDisableImplicit, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string? procVal = Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.Process);
            return string.Equals(procVal?.Trim(), EnvVarDisableImplicit, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Toggles Pure Vulkan Zero-Overlay Mode.
    /// When enabled, forces the Vulkan Loader (vulkan-1.dll) to bypass all implicit overlays (Discord, Steam, EOS, OBS, RTSS)
    /// to eliminate swapchain presentation hooks and stuttering while preserving hardware drivers.
    /// </summary>
    public async Task<bool> SetPureVulkanModeAsync(bool enable)
    {
        return await Task.Run(() =>
        {
            try
            {
                string? val = enable ? EnvVarDisableImplicit : null;
                Environment.SetEnvironmentVariable(EnvVarName, val, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable(EnvVarName, val, EnvironmentVariableTarget.Process);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}
