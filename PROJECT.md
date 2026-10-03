# Project: DiskMaster Pro WinUI 3 Upgrade

## Architecture
- **Framework & Runtime**: WinUI 3 (Windows App SDK), .NET 9 (`net9.0-windows10.0.26100.0`), Self-Contained Unpackaged Desktop Application (`WindowsPackageType=None`).
- **Presentation Layer**: `MainWindow` host with `TabView` navigation, multi-window `FloatingTabWindow` instances, XAML pages (`EasyModePage`, `DiskToolsPage`, `WimDeployPage`, `BootManagerPage`, `AdvancedModePage`, `SystemHealthPage`, `SystemOptimizerPage`, `SettingsPage`), `ModularLayoutManager` for card customization and preset layouts.
- **Service & Process Layer**: 
  - `ProcessHelper`: Concurrent process runner using `Task.WhenAll` to avoid 4KB anonymous pipe buffer deadlocks.
  - Subprocess services: `DiskToolsService`, `BcdManagerService`, `BootRepairService`, `WimDeployService`, `EnvironmentCheckService`, `RufusDownloadService` routing to `ProcessHelper`.
  - Diagnostics & Telemetry: `SmartReaderService` (NVMe Log Page 0x02 spec parser with offset 128 and unprivileged fallback), `HealthMonitorService`, `DiskReliabilityInfo`.
  - Settings & State: `SettingsService` / `AppSettings` persisting tab ordering, component visibility, layout orders, and splitter positions to JSON.
  - Localization: `LocalizationService` supporting 4-language parity (`zh-TW`, `zh-CN`, `en-US`, `ja-JP`) without fallback corruption.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Process Pipe Deadlock Elimination | Provide `ProcessHelper.RunCommandAsync` / concurrent `Task.WhenAll` stream consumption across all process services (`DiskToolsService`, `BcdManagerService`, `BootRepairService`, `WimDeployService`, `EnvironmentCheckService`, `RufusDownloadService`). | M1 | ORIGINAL_REQUEST §R3 |
| 2 | NVMe Log Page 0x02 Offset Fix | Correct `PowerOnHours` offset from 96 to 128 in `SmartReaderService.cs` per NVMe Base Spec 5.14.1.2. | M1 | ORIGINAL_REQUEST §R4 |
| 3 | Unprivileged S.M.A.R.T. Fallback | Provide reliable fallback for `PowerOnHours` using Windows `InstallDate` and `TickCount64` when hardware IOCTL / CIM queries are unprivileged. | M1 | ORIGINAL_REQUEST §R4 |
| 4 | Non-N/A Reliability Counters | Ensure 0 read/write errors display as `"0"` instead of `null` / `"N/A"` in `HealthMonitorService.cs` and `DiskReliabilityInfo.cs`. | M1 | ORIGINAL_REQUEST §R4 |
| 5 | Dynamic Disk Indexing | Replace hardcoded disk index `0` with `SelectedDisk?.Number ?? 0` in `EasyModeViewModel.cs`. | M1 | ORIGINAL_REQUEST §R4 |
| 6 | Top-Bar Zoom Control Cleanup | Remove `[⚡ Auto] [➖] 100% [➕]` zoom control group from `TabView.TabStripFooter` in `MainWindow.xaml` and guard related code-behind. | M2 | ORIGINAL_REQUEST §R5 |
| 7 | Tab Header Emoji Removal | Strip emoji prefixes from tab headers in `MainWindow.xaml` and `LocalizationService.cs`, enabling clean native `FontIconSource` glyphs. | M2 | ORIGINAL_REQUEST §R5 |
| 8 | Subtitle & Bilingual Parentheses Removal | Eliminate redundant parenthetical subtitles (e.g. `(Floating Independent Window)`, `(Reliability Counters)`) across XAML and code-behind. | M2 | ORIGINAL_REQUEST §R5 |
| 9 | Pure 4-Language Parity | Implement comprehensive localization resolving `zh-TW`, `zh-CN`, `en-US`, `ja-JP` across all pages, fixing the 336 ternary Japanese-to-English fallback bug. | M2 | ORIGINAL_REQUEST §R5 |
| 10 | Chrome Tab Tear-Off via Drag | Wire `TabDroppedOutside` on `MainTabs` to detach tabs into `FloatingTabWindow` positioned at cursor coordinates via Win32 `GetCursorPos`. | M3 | ORIGINAL_REQUEST §R1 |
| 11 | Visual Tree Reparenting & Lifecycle | Implement defensive visual tree detachment preventing `ArgumentException`, and ensure `MainWindow.Closed` disposes floating windows. | M3 | ORIGINAL_REQUEST §R1 |
| 12 | Mouse Drag Re-Docking & Dock-All | Wire `TabStripDragOver` and `TabStripDrop` for drag-to-dock, and provide one-click dock-all capability. | M3 | ORIGINAL_REQUEST §R1 |
| 13 | Modular Component & Card Customization | Implement `ModularLayoutManager` enabling cards within `EasyModePage`, `DiskToolsPage`, and `WimDeployPage` to be shown/hidden/reordered freely. | M3 | ORIGINAL_REQUEST §R2 |
| 14 | Layout & Tab Order Persistence | Extend `AppSettings` to persist `WorkspaceTabOrders`, `ComponentVisibility`, `ComponentOrder`, and `SplitterSizes` across application restarts. | M3 | ORIGINAL_REQUEST §R2 |
| 15 | E2E Requirements Test Suite | Implement comprehensive opaque-box test suite covering Tiers 1-4 with automated test runner, generating `TEST_READY.md`. | E2E-Track | Project Pattern Dual Track |
| 16 | E2E Verification & Adversarial Hardening | Validate 100% pass of E2E suite, perform Tier 5 adversarial coverage hardening, and verify Release compilation & portable packaging. | M-Final | ORIGINAL_REQUEST §Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Core Engine Stability & Telemetry | Features #1, #2, #3, #4, #5: Process pipe deadlock elimination, NVMe struct offset 128 fix, unprivileged fallback, 0-read-errors display fix, dynamic disk indexing. | none | IN_PROGRESS |
| M2 | UI Declutter, Tab Polish & 4-Language Parity | Features #6, #7, #8, #9: Remove zoom buttons, strip emojis from tabs, eliminate bilingual parentheses, establish 4-language parity (zh-TW, zh-CN, en-US, ja-JP). | none | PLANNED |
| M3 | Chrome Tab Tear-Off & Modular Layout Engine | Features #10, #11, #12, #13, #14: TabDroppedOutside tear-off, cursor positioning, reparenting safety, drag re-docking, modular card customization, AppSettings persistence. | M1, M2 | PLANNED |
| E2E-Track | Requirements-Driven E2E Test Suite | Feature #15: Opaque-box test harness, runner script, Tiers 1-4 test cases covering all R1-R5 features, publication of `TEST_READY.md`. | none (parallel) | IN_PROGRESS |
| M-Final | E2E Test Pass, Adversarial Hardening & Packaging | Feature #16: 100% E2E test pass, Tier 5 white-box adversarial testing, Release build (0 errors, 0 warnings), portable singlefile package execution. | M3, E2E-Track | PLANNED |

## Interface Contracts

### Process Execution Contract: `ProcessHelper` ↔ Subprocess Services
```csharp
namespace DiskMasterWinUI.Helpers
{
    public static class ProcessHelper
    {
        // High-level execution method returning stdout/stderr concurrently without deadlock
        public static Task<ProcessResult> RunCommandAsync(
            string fileName, 
            string arguments, 
            CancellationToken cancellationToken = default);

        public static Task<ProcessResult> RunProcessAsync(
            ProcessStartInfo startInfo, 
            CancellationToken cancellationToken = default);
    }

    public record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool Success);
}
```
- Invariant: `StandardOutput` and `StandardError` MUST be read concurrently using `Task.WhenAll`. No sequential `ReadToEndAsync` calls are permitted.

### S.M.A.R.T. Telemetry Contract: `SmartReaderService` ↔ `HealthMonitorService` ↔ `DiskReliabilityInfo`
```csharp
public class NvmeHealthDetails
{
    public ulong PowerOnHours { get; set; } // Must be read from offset 128
    public ulong PowerCycles { get; set; }  // Must be read from offset 112
    public ulong MediaErrors { get; set; }
}

public class DiskReliabilityInfo
{
    public long? PowerOnHours { get; set; }
    public long? ReadErrorsTotal { get; set; }   // 0L when zero errors; never null for healthy SSD
    public long? WriteErrorsTotal { get; set; }  // 0L when zero errors
    public string PowerOnDisplay { get; }
    public string ReadErrorsDisplay { get; }    // Renders "0" when 0L, "N/A" only if truly null
}
```

### Layout Persistence Contract: `SettingsService` / `AppSettings` ↔ UI
```csharp
public class AppSettings
{
    // Existing settings...
    public Dictionary<string, List<string>> WorkspaceTabOrders { get; set; } = new();
    public Dictionary<string, bool> ComponentVisibility { get; set; } = new();
    public Dictionary<string, List<string>> ComponentOrder { get; set; } = new();
    public Dictionary<string, double> SplitterSizes { get; set; } = new();
}
```

### Window Reparenting & Detach Contract: `MainWindow` ↔ `FloatingTabWindow`
```csharp
// Tab tear-off invocation on TabDroppedOutside
public void FloatTabAt(TabViewItem tabItem, Windows.Graphics.PointInt32 screenPoint);

// Visual tree detachment safety helper
public static void DetachFromVisualTree(UIElement element);

// Re-docking into MainTabs
public void RedockTab(string tabTag, UIElement content, string header, IconSource icon);
public void DockAllFloatingTabs();
```

## Code Layout
- `Helpers/ProcessHelper.cs`: Concurrency-safe process execution runner.
- `Services/`:
  - `DiskToolsService.cs`, `BcdManagerService.cs`, `BootRepairService.cs`, `WimDeployService.cs`: Subprocess service consumers.
  - `SmartReaderService.cs`, `HealthMonitorService.cs`: S.M.A.R.T. and NVMe telemetry services.
  - `SettingsService.cs`: JSON configuration and `AppSettings` storage.
  - `LocalizationService.cs`: 4-language localized dictionary and helper resolution.
  - `ModularLayoutManager.cs`: Dynamic component visibility, ordering, and preset binding.
- `Models/DiskReliabilityInfo.cs`: Reliability telemetry data model.
- `ViewModels/EasyModeViewModel.cs`: Easy Mode page viewmodel with dynamic disk index binding.
- `MainWindow.xaml`, `MainWindow.xaml.cs`: TabView host, tear-off handlers, dock-all, zoom cleanup.
- `Controls/FloatingTabWindow.xaml`, `Controls/FloatingTabWindow.xaml.cs`: Detached floating window host.
- `Pages/`:
  - `EasyModePage.xaml`, `.cs`: Easy mode partitioned cards.
  - `DiskToolsPage.xaml`, `.cs`: S.M.A.R.T. cards, diagnostic tools.
  - `WimDeployPage.xaml`, `.cs`: Modular deployment options.
- `tests/`: E2E test suites (Tiers 1-4, test runner).
