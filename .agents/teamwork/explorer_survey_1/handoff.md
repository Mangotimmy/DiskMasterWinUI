# Handoff Report: R1 & R2 Architectural Survey & Blueprint

**Role:** Explorer 1 (Tab and Layout Explorer)  
**Working Directory:** `C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\explorer_survey_1`  
**Handoff Type:** Hard (Survey Task Complete)  
**Report Document:** `C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\explorer_survey_1\survey_report.md`  

---

## 1. Observation

1. **TabView Configuration in `MainWindow.xaml` (Lines 76–80):**
   ```xml
   <TabView x:Name="MainTabs" Grid.Row="1"
            CanReorderTabs="True"
            CanDragTabs="True"
            IsAddTabButtonVisible="False"
            SelectionChanged="MainTabs_SelectionChanged">
   ```
   `CanDragTabs="True"` is present, but `TabDroppedOutside`, `TabStripDragOver`, and `TabStripDrop` are completely absent.

2. **Existing Tab Detach Implementation in `MainWindow.xaml.cs` (Lines 357–393):**
   `FloatTab(TabViewItem tabItem)` creates a `new FloatingTabWindow(tag, header, page)` and removes `tabItem` from `MainTabs.TabItems`. It sets `ContentFrame.Content = null` only if `ReferenceEquals(ContentFrame.Content, page)` is true. If an inactive tab is detached, or if reparenting occurs when docking back, no defensive check against `page.Parent` exists.

3. **FloatingTabWindow Implementation in `Controls/FloatingTabWindow.xaml.cs` (Lines 40, 105):**
   Hosts the page via `FloatingContentFrame.Content = page;` and on docking executes `FloatingContentFrame.Content = null;` before invoking `DockRequested?.Invoke(this);`. Window positioning in lines 50–55 computes bounds via `DisplayHelper.GetOptimalWindowBounds`, centering the window instead of tracking cursor coordinates on multi-monitor setups.

4. **`MainWindow` Lacks Window Closing Cleanup (`MainWindow.xaml.cs`):**
   Search for `Closed` event on `MainWindow` yielded zero results (`grep_search` found no matches). Floating windows are not disposed or docked if `MainWindow` is closed.

5. **Settings Architecture in `Services/SettingsService.cs` (Lines 5–65, 79–83, 106–107):**
   Configuration is defined in `AppSettings` and saved to `%LocalAppData%\DiskMaster_Portable\settings.json` using `System.Text.Json.JsonSerializer`.
   Current properties include `WorkspacePreset`, `Theme`, `ScalePercent`, `Language`, etc.
   Crucially, `AppSettings` contains no properties for `CustomTabOrder`, `WorkspaceTabOrders`, `ComponentVisibility`, `ComponentOrder`, or `SplitterSizes`.

6. **Tab Ordering Reset on Relaunch (`MainWindow.xaml.cs` Lines 34–41, 334–347):**
   `WorkspaceTabTags` stores hardcoded arrays of tab tags for each preset ("Master", "Deploy", "Repair", "Gaming", "Lite"). Whenever `ApplyWorkspacePreset` is called or the app launches, tab order resets to the hardcoded array, discarding any drag reordering.

7. **Page Card Hierarchies:**
   - `EasyModePage.xaml`:
     - Line 55: `Border` (Hero System Drive Status & Quick Actions)
     - Line 108: `Border` (Disks Panel)
     - Line 187: `Border` (Interactive Graphical Partition Map)
     - Line 343: `Border` (Suggested DiskPart Command)
     - Line 373: `Expander` (Partition & Volume Management Tools)
     - Line 509: `Border` (Output Log Console)
     - Lines 164, 332, 500: `AdobeSplitter` controls for vertical and horizontal panes
   - `DiskToolsPage.xaml`:
     - Line 56: `Border` (S.M.A.R.T. Health Overview)
     - Line 108: `Border` (Full S.M.A.R.T. Deep Telemetry with 8 NVMe KPI cards)
     - Line 285: `StackPanel` (Basic Diagnostics Fallback Summary)
     - Line 313: `Expander` (Reliability Counters Detail)
     - Lines 359, 424, 485, 558, 616, 678, 748: Chkdsk, Surface Scan, Benchmark, Wipe, Rescue, BitLocker, Console Log cards
   - `WimDeployPage.xaml`:
     - Line 50: `Expander` (Windows ISO/WIM Downloader)
     - Line 360: `Grid` (Image Path & Browse)
     - Line 398: `ComboBox` (Edition Selector)
     - Line 409: `Grid` (Destination & Boot Partitions)
     - Line 437: `StackPanel` (Windows Addons & Bypass Options)
     - Line 454: `Grid` (Driver Injection)
     - Line 469: `Button` (Start Deployment)
     - Line 479: `Expander` (VHDX Boot Deployment)
     - Line 506: `Expander` (Image Management Tools)

8. **Build Verification (`dotnet build DiskMasterWinUI.csproj -c Release`):**
   Output: `建置成功。0 個警告, 0 個錯誤。經過時間 00:00:40.21` (Task task-84 exit code 0).

---

## 2. Logic Chain

1. **For R1 (Tab Tear-Off & Re-Docking):**
   - Observation 1 shows `CanDragTabs="True"` is already enabled on `MainTabs`, but `TabDroppedOutside` is not hooked.
   - Therefore, hooking `TabDroppedOutside="MainTabs_TabDroppedOutside"` will directly capture tab tear-off gestures when dragged outside the tab bar.
   - Observation 3 shows `FloatingTabWindow` centers itself rather than following the mouse. By querying Win32 `GetCursorPos` inside `TabDroppedOutside`, the exact drop coordinates across any monitor can be retrieved and passed to `FloatingTabWindow.AppWindow.Move(...)`.
   - Observation 2 & 3 show potential visual tree conflicts if an element still has a parent. Enforcing a defensive visual tree detachment helper (`if (page.Parent is Frame f) f.Content = null; else if (page.Parent is Panel p) p.Children.Remove(page);`) guarantees that `System.ArgumentException` cannot occur during float or dock operations.
   - By subscribing `MainTabs` to `TabStripDragOver` and `TabStripDrop`, and equipping `FloatingTabWindow` with draggable tab/header data transfer (`DataPackageOperation.Move`), users can drag tabs back into the main tab strip.
   - Observation 4 shows window lifecycle leakage on shutdown. Adding a `this.Closed` handler to `MainWindow` ensures all floating child windows are cleanly closed without leaving orphaned processes.

2. **For R2 (Modular Layout Engine & Persistence):**
   - Observation 5 confirms `AppSettings` is JSON-serialized to local app data, but lacks tab order and component layout collections.
   - Observation 6 demonstrates why user tab reordering is lost: `ApplyWorkspacePreset` re-populates `MainTabs` using hardcoded arrays without consulting user state.
   - By adding `CustomTabOrder`, `WorkspaceTabOrders`, `ComponentVisibility`, `ComponentOrder`, and `SplitterSizes` to `AppSettings`, both tab ordering and card configurations can be persisted across restarts.
   - Observation 7 establishes that cards within `EasyModePage`, `DiskToolsPage`, and `WimDeployPage` are discrete visual elements (`Border`, `Expander`, `Grid`) housed inside `StackPanel` or `Grid` containers.
   - Because they are direct children of containers, a `ModularLayoutManager` can cleanly control their `Visibility` (`Visible` vs `Collapsed`) and reorder them within `container.Children` without breaking compiled `{x:Bind}` scopes.
   - Mapping workspace presets ("Master", "Deploy", "Repair", "Gaming", "Lite") to preset visibility dictionaries allows workspace switching to seamlessly toggle specialized components (e.g. S.M.A.R.T. telemetry in Repair, Deploy options in Deploy, Benchmark in Gaming).

---

## 3. Caveats

- **Network / External Dependencies:** Rufus Fido script and external ISO links are not invoked during survey.
- **Assumptions Made:** In WinUI 3, moving `UIElement` references between collections within the same thread does not require re-instantiation of data contexts or compiled bindings.
- **Unexplored Areas:** Subprocess concurrency (R3), NVMe offset 128 (R4), and Top-bar scale clutter/4-language localization (R5) are assigned to other explorers/implementers and are outside this report's primary focus.

---

## 4. Conclusion

1. **R1:** The existing `FloatTab` and `FloatingTabWindow` infrastructure provides an excellent foundation. Implementing Chrome-style drag-to-float requires:
   - Wiring `TabDroppedOutside` on `MainTabs` with Win32 `GetCursorPos` positioning for multi-monitor accuracy.
   - Enforcing defensive visual detachment to prevent `ArgumentException`.
   - Wiring `TabStripDragOver` and `TabStripDrop` for drag-to-re-dock, complemented by the existing one-click dock back and dock all actions.
   - Adding `MainWindow.Closed` cleanup.

2. **R2:** Implementing the modular layout engine requires:
   - Extending `AppSettings` with `WorkspaceTabOrders`, `ComponentVisibility`, `ComponentOrder`, and `SplitterSizes`.
   - Updating `ApplyWorkspacePreset` to respect saved tab order.
   - Implementing `ModularLayoutManager` to manage card visibility and ordering per page, with preset visibility mapping and a user-facing customization dialog/flyout.
   - Hooking `AdobeSplitter.EndDrag` to persist pane dimensions.

---

## 5. Verification Method

1. **Compilation Check:**
   ```powershell
   dotnet build C:\Users\Atszl\Desktop\DiskMasterWinUI\DiskMasterWinUI.csproj -c Release
   ```
   *Expected output: Build succeeded with 0 warnings and 0 errors.*

2. **Files to Inspect:**
   - Report: `C:\Users\Atszl\Desktop\DiskMasterWinUI\.agents\teamwork\explorer_survey_1\survey_report.md`
   - Target files for implementation:
     - `MainWindow.xaml` and `MainWindow.xaml.cs`
     - `Controls/FloatingTabWindow.xaml` and `Controls/FloatingTabWindow.xaml.cs`
     - `Services/SettingsService.cs`
     - `Pages/EasyModePage.xaml` and `Pages/EasyModePage.xaml.cs`
     - `Pages/DiskToolsPage.xaml` and `Pages/DiskToolsPage.xaml.cs`
     - `Pages/WimDeployPage.xaml` and `Pages/WimDeployPage.xaml.cs`

3. **Invalidation Conditions:**
   - Any modification that leads to `ArgumentException` during tab detach/dock cycles.
   - Tab reordering or card layout failing to survive an application restart.
   - Workspace preset selection failing to alter component visibility.
