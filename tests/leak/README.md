# UI Soak Gate (tests/leak)

Executable Red/Green gate for the defects the reflection-based E2E suite cannot see:
UI-thread liveness, steady-state handle/memory growth, and overlapping background loads.

## Why

The 212 contract tests in `tests/tier*_*` assert types, members and config values by
reflection. They cannot detect a frozen UI thread, a handle leak, or two concurrent page
loads. This gate closes that gap.

## Run

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/leak/Test-UiSoak.ps1
# or a specific scenario
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/leak/Test-UiSoak.ps1 -Scenario dwell -DwellSeconds 30
```

Requires an unlocked, interactive desktop: the gate launches the Release exe
(`-Exe` to override), focuses it and sends Ctrl+Tab. Debug logging must already be enabled
in `%LOCALAPPDATA%\DiskMaster_Portable\settings.json` for the stall assertions to see
the app's own watchdog warnings.

## Design

1. Adaptive warm-up: cycles tabs until the handle count stops moving (max `-MaxWarmupCycles`).
   Page construction is a one-time allocation, so it must not be counted as a leak.
2. Measurement window: dwell on SystemPerformance, rapid Ctrl+Tab burst, settle.
3. Every sample probes the UI thread with `SendMessageTimeout(WM_NULL)`; an unresponsive
   window is polled for recovery (`-RecoveryTimeoutSeconds`) and recorded as a stall, or as a
   hang when it never recovers (a full dump is captured with `dotnet-dump collect`).
4. Pass/fail is steady-state slope based: handles <= 2/s, private bytes <= 5 MB/s, no stall
   above 1.5 s, no hang. Warm-up stalls are reported but not failed.

## Exit codes

- `0` GREEN, `1` RED (hang, stall or growth threshold), `2` harness/setup failure.

Artifacts (`soak_*.json`, optional `hang_*.dmp`) are written to `tests/leak/artifacts/`
and are git-ignored. Dumps are large - analyse then delete them.
