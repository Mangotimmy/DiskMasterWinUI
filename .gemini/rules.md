# Windows Desktop Architecture & Code Quality Guidelines

## 1. Process Execution & Pipe Concurrency
- Never read stdout and stderr sequentially when redirecting streams. Always read concurrently using `Task.WhenAll` to prevent 4KB pipe buffer deadlocks on Windows.
- Route all command invocations through `ProcessHelper.RunProcessAsync`.

## 2. Hardware Telemetry & S.M.A.R.T.
- NVMe Log Page 0x02 offsets:
  - 96..111: `ControllerBusyTime`
  - 112..127: `PowerCycles`
  - 128..143: `PowerOnHours`
  - 144..159: `UnsafeShutdowns`
  - 160..175: `MediaErrors`
- Never treat 0 media errors as null; 0 indicates optimal health.
- Dynamic device target: use `SelectedDisk?.DeviceId ?? 0`, never hardcode index 0.

## 3. WinUI 3 TabView & Windowing
- Wire `TabDroppedOutside` to enable mouse drag-to-float tab detaching.
- Persist user tab ordering and component layouts across sessions.

## 4. Internationalization & Localization Parity
- Support `zh-TW`, `zh-CN`, `en-US`, `ja-JP` uniformly across all UI elements, labels, buttons, and ViewModel status messages.
- Eliminate bilingual parenthetical noise and duplicate emojis in iconography.
- Never hardcode Chinese or English messages in ViewModels; always query `LocalizationService.Instance.CurrentLanguage`.

## 5. Navigation & Page Caching
- Always specify `NavigationCacheMode = NavigationCacheMode.Required;` in Page constructors.
- This prevents page destruction and recreation during tab switches, guaranteeing 0ms tab switching and eliminating redundant WMI/process queries.

## 6. WinUI ToggleSwitch Data-Binding Protection
- WinUI 3 `ToggleSwitch.Toggled` events automatically fire during visual tree materialization when `IsOn` is bound.
- Guard all `Toggled` event handlers with:
  `if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;`
  `if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.TargetProperty) { ... }`
- This completely prevents unauthorized or destructive system operations (such as `manage-bde`, `fsutil`, registry tweaks) from running on page load.

## 7. Parallel Telemetry & Subprocess Concurrency
- When refreshing system configuration or telemetry requiring multiple external processes (e.g. `powercfg`, `Get-MMAgent`, `WMI`, `fsutil`), never run them sequentially.
- Execute independent queries in parallel via `Task.WhenAll(task1, task2, task3)` to minimize load times.
