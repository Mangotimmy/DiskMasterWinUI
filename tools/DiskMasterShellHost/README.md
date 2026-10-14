# DiskMasterShellHost — Explorer right-click launcher

`DiskMasterShellHost.exe` is the tiny forwarding exe behind the **"用 DiskMaster 復原檔案 / Recover with DiskMaster"**
Explorer context-menu entries. Explorer cannot pass arguments to a WinUI app directly in a way the app can parse
reliably, so the shell verb calls this launcher with the clicked path, and the launcher starts the GUI app on the
Recovery tab:

```
DiskMasterShellHost.exe "D:\Photos"
        ->  DiskMasterWinUI.exe --tab Recovery --recover "D:\Photos"
```

It is a **plain console app**: no XAML, no WinUI, no `PackageReference`, so it builds fully offline.
When Explorer starts it, it hides its own console window; when a terminal starts it, the output stays visible.

## Files

| File | Purpose |
| --- | --- |
| `DiskMasterShellHost.csproj` | net9.0-windows console project (no packages) |
| `Program.cs` | argument parsing, app-exe resolution, launch |
| `verify/` | standalone real-HKCU verification harness (builds the shipped `ShellIntegrationService.cs`) |

Build: `dotnet build tools\DiskMasterShellHost\DiskMasterShellHost.csproj -c Release -v:m`

## Forwarded command line

Always exactly this shape (quoting handled by `ProcessStartInfo.ArgumentList`):

```
--tab Recovery --recover "<clicked path>"
```

The GUI side (`App.xaml.cs`, owned by the main app) parses `--recover` into `App.PendingRecoverPath`, navigates to the
Recovery tab and calls `RecoveryPage.LoadTarget(path)`.

## App exe resolution order

First hit wins; the full attempt trace is printed by `--dry-run`, `--print-target` and written to the log:

1. `--app "<path>"` (explicit override; accepts an exe **or** a folder containing `DiskMasterWinUI.exe`)
2. `<launcher dir>\DiskMasterShellHost.app.txt` — first non-empty line, `#` starts a comment (portable override file)
3. `<launcher dir>\DiskMasterWinUI.exe` (portable / side-by-side layout)
4. `%DISKMASTER_APP_PATH%`
5. `HKCU\Software\DiskMasterPro\InstallLocation` — written by `ShellIntegrationService.Install()`
6. `HKLM\SOFTWARE\DiskMasterPro\InstallLocation` — all-users variant (needs elevation)
7. `%ProgramFiles%\DiskMaster Pro\DiskMasterWinUI.exe`, `%LOCALAPPDATA%\Programs\{DiskMaster Pro,DiskMaster,DiskMasterWinUI}\DiskMasterWinUI.exe`

`--strict` stops after step 3, so an installer/CI run can prove the files were laid out correctly instead of
silently launching a previously installed copy.

## Registry layout written by `Services/ShellIntegrationService.cs`

Per-user (default, **no elevation**), scope `HKCU`:

```
HKCU\Software\Classes\Drive\shell\DiskMasterRecover                 (Default) / MUIVerb / Icon / Position
HKCU\Software\Classes\Drive\shell\DiskMasterRecover\command         (Default) = "<launcher>" "%V"
HKCU\Software\Classes\Directory\shell\DiskMasterRecover\command     (Default) = "<launcher>" "%1"
HKCU\Software\Classes\Directory\Background\shell\DiskMasterRecover\command  (Default) = "<launcher>" "%V"
HKCU\Software\DiskMasterPro\InstallLocation                        = full path of DiskMasterShellHost.exe
```

All-users variant (explicit opt-in, requires Administrator), scope `HKLM`:

```
HKLM\Software\Classes\...\DiskMasterRecover\command                 (same three classes)
HKLM\SOFTWARE\DiskMasterPro\InstallLocation
```

`Uninstall()` removes exactly what `Install()` added: every key that already existed is exported first to
`%LOCALAPPDATA%\DiskMaster_Portable\RegistryBackups\shell-integration-*.reg` and a manifest
(`shell-integration-manifest.txt`) records what was created; uninstall restores the `.reg` backups for pre-existing
keys, deletes only the subtrees it created, and cleans up ancestor keys it created that are left empty.

## Installer note

`tools/**` is excluded from `DiskMasterWinUI.csproj`, so the launcher is **not** part of the app output.
The installer must copy the built `DiskMasterShellHost.exe` into the same folder as `DiskMasterWinUI.exe`
(`DiskMasterSetup.iss` copies `publish\*` to `{app}`). `ShellIntegrationService.Install()` looks for the launcher
next to the app exe, and `Install()` refuses with a clear message when it is missing.

## Manual verification

```powershell
# dry run: resolve the app exe and print the exact forwarded command line (launches nothing)
.\DiskMasterShellHost.exe --app "C:\Program Files\DiskMaster Pro\DiskMasterWinUI.exe" --dry-run "D:\My Photos"

# prove a missing app exe fails loudly instead of exiting silently (exit code 2)
.\DiskMasterShellHost.exe --strict --dry-run "D:\"

# real HKCU install -> verify -> uninstall -> verify HKCU unchanged
#   MUST be run outside the DSH agent sandbox (that sandbox denies every HKCU write)
dotnet build tools\DiskMasterShellHost\verify\ShellVerify.csproj -c Release
tools\DiskMasterShellHost\verify\bin\Release\net9.0-windows\ShellVerify.exe
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | GUI app started (or the requested dry-run/print action succeeded) |
| 2 | `DiskMasterWinUI.exe` could not be found (message on stderr, also logged) |
| 3 | The app exe was found but could not be started |
| 64 | Bad usage (reserved) |

Log: `%TEMP%\DiskMasterShellHost.log` (truncated automatically past 512 KB).
