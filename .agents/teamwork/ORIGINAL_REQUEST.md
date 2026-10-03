# Original User Request

## 2026-10-01T17:54:59Z

Execute an end-to-end upgrade of DiskMaster Pro WinUI 3: implement Chrome-style mouse drag-to-float tab tear-off, free modular component customization, eliminate process pipe deadlock vulnerabilities, fix hardware S.M.A.R.T. N/A issues, correct hardcoded device indexing, remove top-bar scale clutter, and achieve pure 4-language parity (zh-TW, zh-CN, en-US, ja-JP).

Working directory: C:\Users\Atszl\Desktop\DiskMasterWinUI
Integrity mode: development

## Requirements

### R1. Chrome-Style Mouse Drag Tab Tear-Off & Re-Docking
- Wire `TabDroppedOutside` on `MainTabs` (`TabView`) so dragging any tab out of the tab strip immediately detaches it into a standalone `FloatingTabWindow`.
- Ensure clean visual tree reparenting without exceptions or memory leaks.
- Support mouse drag re-docking back into the main `TabView` or one-click dock-all action.

### R2. Free Component & Card Customization (Modular Layout Engine)
- Enable customizable component positioning for dashboard and tool cards within pages (such as Disk Health, S.M.A.R.T. counters, and Deploy options).
- Provide modular reordering / split layout capabilities so users can freely configure which cards or sections appear and in what arrangement.
- Persist custom tab ordering and card layout states to `AppSettings` across application restarts.

### R3. Process Concurrency & Pipe Deadlock Elimination
- Refactor duplicate sequential `RunCommandAsync` implementations in `DiskToolsService.cs`, `BcdManagerService.cs`, `BootRepairService.cs`, and `WimDeployService.cs` to use `ProcessHelper.RunProcessAsync`.
- Ensure all subprocess executions read stdout and stderr concurrently via `Task.WhenAll` to prevent 4KB pipe buffer deadlocks on Windows.

### R4. Fix Hardware S.M.A.R.T. Telemetry & Device Indexing Bugs
- Correct NVMe Log Page 0x02 struct offsets in `SmartReaderService.cs` (`PowerOnHours` at byte offset 128, not 96).
- In `HealthMonitorService.cs` and `DiskReliabilityInfo.cs`, ensure 0 read/write errors display as `0` instead of `null` / `N/A`.
- In `EasyModeViewModel.cs`, fix hardcoded device index `0` to dynamically target `SelectedDisk?.Number ?? 0`.
- Provide effective system installation / uptime fallback for `PowerOnHours` when direct controller queries are unprivileged.

### R5. UI Clutter, Emoji & Scale Button Cleanup
- Remove the top-bar `[⚡ Auto] [➖] 100% [➕]` zoom control group from `TabView.TabStripFooter` to declutter the tab bar.
- Remove redundant emojis from tab headers (e.g. change `📊 簡易模式` to `簡易模式`), letting the native Fluent `FontIconSource` provide clean iconography.
- Remove verbose subtitles and bilingual parentheses (such as `(Floating Independent Window) — 支援多螢幕拖曳` and `(Reliability Counters)`).
- Ensure complete 4-language parity (`zh-TW`, `zh-CN`, `en-US`, `ja-JP`) without falling back to English on Japanese.

## Acceptance Criteria

### Chrome Tab Tear-Off & Windowing
- [ ] Mouse dragging a tab outside the `TabView` tab strip cleanly detaches it into a standalone window.
- [ ] The detached window responds to mouse movements, supports multi-monitor placement, and docks back cleanly when requested.
- [ ] No `System.ArgumentException` or visual tree hierarchy errors occur during detach or dock cycles.

### Free Layout & Customization
- [ ] Reordered tab positions and modular card arrangements are saved in user configuration and accurately restored upon relaunch.
- [ ] Workspace presets seamlessly apply custom component visibility.

### Process Stability & Logic Correctness
- [ ] All external process execution calls route through `ProcessHelper.RunProcessAsync` with concurrent stream reading, preventing pipe deadlocks.
- [ ] `EasyModeViewModel` targets the user's selected disk rather than hardcoded disk 0.

### Diagnostics & UI Cleanliness
- [ ] WD_BLACK SN850X and other NVMe drives display valid non-N/A Power-On Hours and `0` Read Errors in Reliability Counters.
- [ ] The top tab bar contains no redundant scale buttons or double-icon emojis.
- [ ] Language switching between `zh-TW`, `zh-CN`, `en-US`, and `ja-JP` renders 100% native localized text across all pages.

### Build & Package Verification
- [ ] `dotnet build DiskMasterWinUI.csproj -c Release` compiles with 0 warnings and 0 errors.
- [ ] `build_portable_singlefile.ps1` successfully packages the application into `installer_output\DiskMaster_Portable.exe`.
