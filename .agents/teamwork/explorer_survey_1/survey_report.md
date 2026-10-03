# Comprehensive Architecture & Codebase Survey Report: R1 & R2
**DiskMaster Pro WinUI 3 — Tab Tear-Off, Docking & Modular Layout Engine**

**Date:** 2026-10-01  
**Author:** Explorer 1 (Tab and Layout Explorer)  
**Target Project:** `C:\Users\Atszl\Desktop\DiskMasterWinUI`  
**Target Solution / Project File:** `DiskMasterWinUI.csproj` (Target Framework: `net9.0-windows10.0.26100.0`, WinUI 3 / Windows App SDK)

---

## 1. Executive Summary

This survey report provides a detailed architectural audit and implementation blueprint for:
1. **R1: Chrome-Style Mouse Drag Tab Tear-Off & Re-Docking** (WinUI 3 `TabView.TabDroppedOutside`, standalone `FloatingTabWindow`, cross-window drag re-docking, exception-free visual tree reparenting, multi-monitor coordinates).
2. **R2: Free Component & Card Customization (Modular Layout Engine)** (modular card ordering/visibility for `EasyModePage`, `DiskToolsPage` / S.M.A.R.T. & Reliability counters, `WimDeployPage` / Deploy options, workspace preset visibility integration, and persistent state serialization in `AppSettings`).

Build Status Verification:
- Build command: `dotnet build DiskMasterWinUI.csproj -c Release`
- Result: **0 Warnings, 0 Errors, Exit Code 0**.

---

## 2. Requirement 1: Chrome-Style Mouse Drag Tab Tear-Off & Re-Docking

### 2.1 Current Architecture & Code Locations

#### 2.1.1 Main TabView Container (`MainWindow.xaml` & `MainWindow.xaml.cs`)
- **File:** `C:\Users\Atszl\Desktop\DiskMasterWinUI\MainWindow.xaml` (Lines 76–272)
- **Current TabView Declaration:**
  ```xml
  <TabView x:Name="MainTabs" Grid.Row="1"
           CanReorderTabs="True"
           CanDragTabs="True"
           IsAddTabButtonVisible="False"
           SelectionChanged="MainTabs_SelectionChanged">
  ```
- **Tab Items:** Contains 9 declared `TabViewItem` elements:
  1. `TabEasyMode` (Tag: `"EasyMode"`)
  2. `TabWimDeploy` (Tag: `"WimDeploy"`)
  3. `TabBootManager` (Tag: `"BootManager"`)
  4. `TabDiskTools` (Tag: `"DiskTools"`)
  5. `TabSystemHealth` (Tag: `"SystemHealth"`)
  6. `TabNtfsPermissions` (Tag: `"NtfsPermissions"`)
  7. `TabAdvancedMode` (Tag: `"AdvancedMode"`)
  8. `TabOptimizer` (Tag: `"SystemOptimizer"`)
  9. `TabSettings` (Tag: `"Settings"`)
- **Content Area:** Below the `TabView`, a single `Frame` (`ContentFrame` at Line 283) hosts the selected page inside `ContentScrollViewer` and `ContentRoot`.
- **Page Caching:** `MainWindow.xaml.cs` (Lines 17–25) maintains lazy singletons of the 9 pages:
  - `_easyModePage`, `_wimDeployPage`, `_bootManagerPage`, `_diskToolsPage`, `_systemHealthPage`, `_ntfsPermissionsPage`, `_advancedModePage`, `_systemOptimizerPage`, `_settingsPage`.
- **Existing Tab Floating Mechanism:**
  - `MainWindow.FloatTab(TabViewItem tabItem)` (`MainWindow.xaml.cs`, Lines 357–393) is currently invoked only via:
    - Tab right-click context menu: `TabContext_Float_Click` (Line 478)
    - Tab strip footer button: `FloatTabBtn` ("🪟 浮動", Line 243)
    - Keyboard accelerator: `Ctrl+Shift+F` (Line 662)
  - `_floatingWindows` dictionary: `Dictionary<string, FloatingTabWindow>` tracks active detached windows by tab tag.

#### 2.1.2 Floating Tab Window (`Controls/FloatingTabWindow.xaml` & `.xaml.cs`)
- **File:** `C:\Users\Atszl\Desktop\DiskMasterWinUI\Controls\FloatingTabWindow.xaml` and `FloatingTabWindow.xaml.cs`
- **Class:** `public sealed partial class FloatingTabWindow : Window`
- **Current Layout:**
  - TitleBar: `TitleBar x:Name="FloatingTitleBar"` with icon and title.
  - Toolbar: Top border with `WindowNoticeText` and `DockBackButton` ("📥 嵌回主視窗 (Dock Back)").
  - Content Area: `ScrollViewer` hosting `FloatingContentFrame` (`Frame`).
- **Dock Request Pattern:**
  - Exposes `public event Action<FloatingTabWindow>? DockRequested;`
  - In `PerformDock()` (Lines 96–113):
    1. Unregisters event handlers from `LocalizationService` and `SettingsService`.
    2. Detaches page: `FloatingContentFrame.Content = null;`
    3. Fires `DockRequested?.Invoke(this);`
    4. Calls `this.Close();`
  - In `FloatingTabWindow_Closed` (Lines 88–94):
    If `!_isDocking`, automatically triggers `PerformDock()` so closing the window via the Windows title bar (X) button cleanly docks the tab back to `MainWindow`.

---

### 2.2 Critical Findings & Architectural Constraints

1. **`TabDroppedOutside` Event is Not Wired:**
   - In `MainWindow.xaml` (Line 76), `MainTabs` sets `CanDragTabs="True"`, but does NOT subscribe to `TabDroppedOutside`.
   - When a user clicks and drags a tab outside the tab strip, WinUI 3 raises `TabDroppedOutside`, but nothing happens and the tab snaps back.
   - **Fix:** Wire `TabDroppedOutside="MainTabs_TabDroppedOutside"`.

2. **Detached Window Screen Positioning & Multi-Monitor Support:**
   - Currently, `FloatingTabWindow` calculates bounds using `DisplayHelper.GetOptimalWindowBounds(...)` based on the window's own HWND before activation, which defaults to centering on the primary monitor.
   - For true Chrome-style tear-off, the new window must spawn immediately at the user's current mouse cursor position, supporting any secondary or tertiary monitor.
   - Win32 `GetCursorPos` can capture `POINT` in desktop virtual screen coordinates at the moment `TabDroppedOutside` fires.
   - `AppWindow.Move(new PointInt32(pt.X - 120, Math.Max(0, pt.Y - 20)))` places the window's title bar right under the cursor.

3. **Visual Tree Reparenting & `ArgumentException` Prevention:**
   - WinUI 3 strictly forbids a `UIElement` (such as `Page`) having more than one visual parent simultaneously. Setting `FloatingContentFrame.Content = page` or `MainWindow.ContentFrame.Content = page` while `page.Parent != null` throws:
     `System.ArgumentException: Value does not fall within the expected range (HRESULT: 0x80070057)`.
   - In `FloatTab`, if the floated tab was the active tab, `ContentFrame.Content = null` is called. However, if an inactive tab is dragged out, `ReferenceEquals(ContentFrame.Content, page)` is false. While inactive pages are normally not attached to `ContentFrame`, defensive detachment must be enforced:
     ```csharp
     public static void SafelyDetachElement(FrameworkElement? element)
     {
         if (element == null) return;
         if (element.Parent is Frame parentFrame)
         {
             parentFrame.Content = null;
         }
         else if (element.Parent is ContentControl cc)
         {
             cc.Content = null;
         }
         else if (element.Parent is Panel p)
         {
             p.Children.Remove(element);
         }
     }
     ```
   - This defensive detachment must be called prior to `FloatingContentFrame.Content = page` and prior to `ContentFrame.Content = win.HostedPage`.

4. **Mouse Drag Re-Docking Back into `MainTabs`:**
   - Currently, docking is only available via the "📥 嵌回主視窗" button on `FloatingTabWindow`, the "Dock All" button on `MainWindow`, or `Ctrl+Shift+D`.
   - To support mouse drag re-docking into the main `TabView`:
     - **Option A (Tab-to-Tab Drag Drop):** Equip `FloatingTabWindow` with a draggable tab header or `TabView` having `CanDragTabs="True"`. On `MainTabs`, subscribe to `TabStripDragOver` and `TabStripDrop`.
       In `TabDragStarting`, package the tab identifier: `args.Data.SetText("DiskMaster:Tab:" + tag)`.
       In `MainTabs_TabStripDragOver`, verify `DataView.Contains(StandardDataFormats.Text)` and set `AcceptedOperation = DataPackageOperation.Move`.
       In `MainTabs_TabStripDrop`, extract `tag`, determine the drop index from `args.DropPosition`, and dock the tab at the targeted slot.
     - **Option B (Snapping & Dock Zones):** In addition to tab drag-drop, keep the one-click dock-back action and add drag-to-dock zones or one-click dock-all button (`FloatingManagerBtn`).

5. **Application Shutdown Cleanup & Leak Prevention:**
   - `MainWindow.xaml.cs` lacks a `this.Closed` handler. If `MainWindow` is closed while floating windows remain open, those windows linger as orphaned processes or crash on dereferencing `MainWindow.CurrentInstance`.
   - **Fix:** In `MainWindow.xaml.cs`:
     ```csharp
     this.Closed += (s, e) =>
     {
         foreach (var win in _floatingWindows.Values.ToList())
         {
             win.Close();
         }
         _floatingWindows.Clear();
     };
     ```

---

## 3. Requirement 2: Free Component & Card Customization (Modular Layout Engine)

### 3.1 Code Architecture & Existing Components

#### 3.1.1 Configuration Persistence (`Services/SettingsService.cs`)
- **File:** `C:\Users\Atszl\Desktop\DiskMasterWinUI\Services\SettingsService.cs`
- **Configuration Class:** `AppSettings` (Lines 5–65)
- **Storage Location:** `%LocalAppData%\DiskMaster_Portable\settings.json` (Lines 79–81)
- **Serialization:** `System.Text.Json.JsonSerializer` with `WriteIndented = true` (Lines 105–107)
- **Change Notifications:** `public event Action? SettingsChanged;`
- **Current Properties in `AppSettings`:**
  - `WorkspacePreset` (default: `"Master"`, supports `"Master"`, `"Deploy"`, `"Repair"`, `"Gaming"`, `"Lite"`)
  - `Theme`, `ScalePercent`, `Language`, `CustomFontFamily`, `CustomFontScale`, `EnableGlassTransparency`, `CardOpacity`
  - Wallpaper & Avatar settings
  - Downloader & Network parameters
- **Missing in `AppSettings`:**
  - No `TabOrder` property to save custom tab sequence.
  - No `WorkspaceTabOrders` dictionary for per-preset custom tab arrangements.
  - No `ComponentVisibility` dictionary (`Dictionary<string, bool>`) to store user card toggles.
  - No `ComponentOrder` dictionary (`Dictionary<string, int>`) to store card layout indices.
  - No `SplitterSizes` dictionary (`Dictionary<string, double>`) for persistent AdobeSplitter positions.

#### 3.1.2 Tab Ordering Architecture (`MainWindow.xaml.cs`)
- **Hardcoded Presets (Lines 34–41):**
  ```csharp
  private static readonly Dictionary<string, string[]> WorkspaceTabTags = new()
  {
      ["Master"] = ["EasyMode", "WimDeploy", "BootManager", "DiskTools", "SystemHealth", "NtfsPermissions", "AdvancedMode", "SystemOptimizer", "Settings"],
      ["Deploy"] = ["WimDeploy", "BootManager", "EasyMode", "DiskTools", "Settings"],
      ["Repair"] = ["SystemHealth", "BootManager", "DiskTools", "NtfsPermissions", "EasyMode", "Settings"],
      ["Gaming"] = ["SystemOptimizer", "DiskTools", "SystemHealth", "Settings"],
      ["Lite"] = ["EasyMode", "WimDeploy", "SystemHealth", "Settings"]
  };
  ```
- **Tab Reordering Defect:**
  - While `CanReorderTabs="True"` allows users to drag tabs within `MainTabs`, no event (`TabDragCompleted`, `TabStripDrop`, or collection change) persists the new tab order to `AppSettings`.
  - Upon switching workspace presets or relaunching the application, tab ordering resets to the hardcoded array.

#### 3.1.3 Dashboard & Tool Cards by Target Page

##### A. EasyModePage (`Pages/EasyModePage.xaml`)
| Card / Component ID | Visual Element & Location | Description |
|---------------------|---------------------------|-------------|
| `CardHeroStatus` | `Border` (Line 55) | 🛡️ System Health, Temperature, BitLocker status & One-Click Quick Action pills |
| `CardDisksPanel` | `Border` (Line 108) | Physical Disks list (`ListView`), refresh, and disk context menu |
| `CardPartitionMap` | `Border` (Line 187) | Interactive graphical partition blocks, slider, quick-size chips, delta cards |
| `CardSuggestedCommand` | `Border` (Line 343) | Suggested DiskPart command box, copy button, execute button |
| `CardPartitionTools` | `Expander` (Line 373) | Format, filesystem, drive letters, partition create/extend/delete, UEFI layout, boot repair |
| `CardConsoleLog` | `Border` (Line 509) | Output log terminal, copy all, save as, clear log |
| `SplitterDisks` | `AdobeSplitter` (Line 164) | Vertical splitter controlling `ColDisks` width |
| `SplitterPartitionMap` | `AdobeSplitter` (Line 332) | Horizontal splitter controlling `RowPartitionMap` height |
| `SplitterConsole` | `AdobeSplitter` (Line 500) | Horizontal splitter controlling `RowConsole` height |

##### B. DiskToolsPage (`Pages/DiskToolsPage.xaml`)
| Card / Component ID | Visual Element & Location | Description |
|---------------------|---------------------------|-------------|
| `CardSmartOverview` | `Border` (Line 56) | S.M.A.R.T. disk list, health status summary, predictive failure query |
| `CardSmartDeepTelemetry` | `Border` (Line 108) | Full S.M.A.R.T. IOCTL decoding, NVMe 8 KPI cards (Temp, Wear, TBW, TBR, Power-On Hours, Unsafe Shutdowns, Media Errors, Error Logs), raw table |
| `CardBasicDiagnostics` | `StackPanel` (Line 285) | Fallback summary panel for drives without raw ATA registers |
| `CardReliabilityCounters` | `Expander` (Line 313) | Detailed reliability counters (Temperature, Wear, Power-On Hours, Read/Write Errors) |
| `CardChkdsk` | `Border` (Line 359) | Chkdsk filesystem scanning and repair |
| `CardSurfaceScan` | `Border` (Line 424) | Sector surface scan and bad block identification |
| `CardBenchmark` | `Border` (Line 485) | Disk read/write throughput speed benchmark |
| `CardWipe` | `Border` (Line 558) | DoD 5220.22-M / NIST 800-88 disk sanitization |
| `CardRescue` | `Border` (Line 616) | WinRE environment rescue and boot rebuild |
| `CardBitLocker` | `Border` (Line 678) | BitLocker encryption, decryption, and key management |
| `CardConsoleLog` | `Border` (Line 748) | Command terminal output log |

##### C. WimDeployPage (`Pages/WimDeployPage.xaml`)
| Card / Component ID | Visual Element & Location | Description |
|---------------------|---------------------------|-------------|
| `CardDownloader` | `Expander` (Line 50) | Official Windows ISO/WIM downloader (Rufus Fido direct link & MSDN catalog) |
| `CardImagePath` | `Grid` (Line 360) | WIM / ESD / ISO image path input, browse, and parse |
| `CardIsoMounted` | `Border` (Line 378) | Auto-mounted ISO virtual drive badge and dismount button |
| `CardEditions` | `ComboBox` (Line 398) | Image edition and index selector dropdown |
| `CardPartitions` | `Grid` (Line 409) | Target system installation partition and EFI boot partition dropdowns |
| `CardFirmware` | `ComboBox` (Line 427) | UEFI / BIOS firmware boot mode selection |
| `CardGuard` | `InfoBar` (Line 432) | Windows 7/8/10/11 boot compatibility guard notice |
| `CardBypassOptions` | `StackPanel` (Line 437) | Win11 hardware bypass, bypass MS account NRO, Compact OS, auto BCD creation |
| `CardDriverInjection` | `Grid` (Line 454) | Offline driver injection directory input and browse |
| `CardStartDeploy` | `Button` (Line 469) | Deploy image trigger button |
| `CardVhdxDeploy` | `Expander` (Line 479) | Native VHDX virtual disk boot deployment option |
| `CardImageMgmt` | `Expander` (Line 506) | Image management tools (Capture, Append, Export, Split, Mount, Service, Clean) |
| `CardDeployLog` | `Border` (Right Col) | Deployment log console and progress bar |

---

### 3.2 Modular Layout Engine Implementation Design

#### 3.2.1 Core Layout Models & Settings Extension
Add the following persistent properties to `AppSettings` in `Services/SettingsService.cs`:
```csharp
// ── Tab & Modular Component Customization ──
public List<string> CustomTabOrder { get; set; } = new();
public Dictionary<string, List<string>> WorkspaceTabOrders { get; set; } = new();
public Dictionary<string, bool> ComponentVisibility { get; set; } = new();
public Dictionary<string, int> ComponentOrder { get; set; } = new();
public Dictionary<string, double> SplitterSizes { get; set; } = new();
```

#### 3.2.2 Workspace Presets & Component Visibility Mapping
Define clean preset visibility defaults for each workspace scenario:

```csharp
public static class WorkspaceComponentDefaults
{
    public static readonly Dictionary<string, Dictionary<string, bool>> PresetVisibility = new()
    {
        ["Master"] = new()
        {
            // All cards visible
        },
        ["Deploy"] = new()
        {
            ["WimDeploy.CardDownloader"] = true,
            ["WimDeploy.CardImagePath"] = true,
            ["WimDeploy.CardEditions"] = true,
            ["WimDeploy.CardPartitions"] = true,
            ["WimDeploy.CardBypassOptions"] = true,
            ["WimDeploy.CardVhdxDeploy"] = true,
            ["WimDeploy.CardImageMgmt"] = false, // Collapsed in focused deploy mode
            ["EasyMode.CardPartitionMap"] = true,
            ["EasyMode.CardPartitionTools"] = true,
            ["DiskTools.CardSmartOverview"] = false,
            ["DiskTools.CardBenchmark"] = false,
        },
        ["Repair"] = new()
        {
            ["DiskTools.CardSmartOverview"] = true,
            ["DiskTools.CardSmartDeepTelemetry"] = true,
            ["DiskTools.CardReliabilityCounters"] = true,
            ["DiskTools.CardChkdsk"] = true,
            ["DiskTools.CardRescue"] = true,
            ["DiskTools.CardBenchmark"] = false,
            ["DiskTools.CardWipe"] = false,
            ["EasyMode.CardHeroStatus"] = true,
            ["EasyMode.CardSuggestedCommand"] = true,
        },
        ["Gaming"] = new()
        {
            ["DiskTools.CardSmartOverview"] = true,
            ["DiskTools.CardBenchmark"] = true,
            ["DiskTools.CardReliabilityCounters"] = true,
            ["DiskTools.CardWipe"] = false,
            ["DiskTools.CardRescue"] = false,
        },
        ["Lite"] = new()
        {
            ["EasyMode.CardHeroStatus"] = true,
            ["EasyMode.CardDisksPanel"] = true,
            ["EasyMode.CardPartitionMap"] = true,
            ["EasyMode.CardPartitionTools"] = false, // Simplified
            ["DiskTools.CardSmartOverview"] = true,
            ["DiskTools.CardSmartDeepTelemetry"] = false,
            ["DiskTools.CardReliabilityCounters"] = false,
        }
    };
}
```

#### 3.2.3 Modular Card Controller Pattern
Implement a lightweight `ModularLayoutManager`:
1. **Card Registration:** Each page registers its cards by passing `(string cardId, string displayName, FrameworkElement element, Panel parentContainer)`.
2. **Apply Layout:**
   - Reads `AppSettings.Current.ComponentVisibility` (falling back to workspace preset defaults).
   - Sets `element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed`.
   - Reorders items inside `parentContainer.Children` according to `ComponentOrder`.
3. **Customization Dialog / Flyout:**
   - A standard button "📐 自訂版面" (Customize Layout) in the page header triggers a dialog.
   - The dialog lists each card with a toggle switch (Show/Hide) and reorder buttons (▲ Up / ▼ Down).
   - Includes "🔄 重設為工作區預設" (Reset to Preset Default).
   - Upon confirmation, updates the UI immediately and calls `SettingsService.Instance.Save()`.
4. **Splitter Position Persistence:**
   - In `AdobeSplitter.cs`, when `EndDrag` completes or size changes, invoke an event or save `TargetColumn.Width.Value` / `TargetRow.Height.Value` to `AppSettings.Current.SplitterSizes`.
   - When the page loads, restore the column width or row height.

---

## 4. Architectural Constraints & Risks

| Area | Risk / Constraint | Mitigation Strategy |
|------|-------------------|---------------------|
| **WinUI 3 Visual Tree** | `ArgumentException` on visual reparenting between windows | Enforce mandatory visual parent detachment before reparenting in both detach and dock cycles. |
| **Tab Drag & Drop** | WinUI 3 TabView internal drag state collision if items are modified mid-drag | Perform tab removal via `DispatcherQueue.TryEnqueue` upon `TabDroppedOutside` to let the drag pipeline finish cleanly. |
| **Multi-Monitor Tear-Off** | Windows appearing off-screen on DPI changes or disconnected monitors | Use `GetCursorPos` + clamp bounds to the active monitor's `DisplayArea.WorkArea`. |
| **Settings Schema Evolution** | Existing `settings.json` missing new properties causing null values | Ensure all new properties on `AppSettings` have non-null default initializers (`new()`). |
| **XAML Bindings & DataContext** | Reordering elements in a `StackPanel` may disrupt `{x:Bind}` | Reordering `UIElement` items directly within `StackPanel.Children` preserves each element's pre-compiled binding scope. |

---

## 5. Verification Plan

1. **Compilation Verification:**
   - Run `dotnet build DiskMasterWinUI.csproj -c Release`
   - Expect: 0 warnings, 0 errors.

2. **Tab Tear-Off & Re-Docking Verification:**
   - Drag tab outside tab strip -> verify `FloatingTabWindow` appears at mouse position.
   - Drag across multiple monitors -> verify smooth movement and correct DPI scaling.
   - Drag tab back to `MainTabs` or click "📥 嵌回主視窗" -> verify clean reinsertion without exceptions.
   - Click "全部嵌回" (`DockAll_Click`) -> verify all floating windows dock back.
   - Close main window while tabs are floating -> verify all processes terminate without orphans.

3. **Layout Customization & Persistence Verification:**
   - Reorder tabs -> restart application -> verify tab order is restored.
   - Toggle card visibility on `EasyModePage`, `DiskToolsPage`, and `WimDeployPage` -> restart application -> verify card states persist.
   - Switch workspace presets ("Deploy", "Repair", "Gaming", "Lite") -> verify preset card visibility applies immediately.
