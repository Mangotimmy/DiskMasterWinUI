# Windows Registry In-Depth Research: Explorer\Advanced & Khronos Vulkan ExplicitLayers (/boost)

> **Document Type:** Systems Engineering & Graphics Optimization Reference  
> **Target Subsystems:** Windows Shell (`explorer.exe`, `dwm.exe`, `shell32.dll`) & Khronos Vulkan Loader (`vulkan-1.dll`)  
> **Registry Paths Analyzed:**  
> 1. `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`  
> 2. `電腦\HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\Vulkan\ExplicitLayers` (along with `ImplicitLayers`, `WOW6432Node`, and Driver Class PnP keys)  

---

## 1. Executive Summary

This research document provides an exhaustive, empirical breakdown of two critical Windows registry configuration hubs:
1. **Windows Explorer Advanced Options (`HKCU:\...\Explorer\Advanced`)**: The per-user nerve center for Windows Shell behavior, DWM composition latencies, taskbar rendering, memory-heavy background telemetry (Widgets, Copilot, Iris recommendations), and disk I/O caching.
2. **Khronos Vulkan Explicit & Implicit Layers (`HKLM:\...\Khronos\Vulkan\ExplicitLayers`)**: The system-wide graphics intercept pipeline where overlays, recording hooks, game launchers (Rockstar Social Club, Steam, EOS, OBS), and validation layers attach to the Vulkan graphics driver stack.

Both keys have a direct, measurable impact on **system responsiveness, gaming latency, frame-time consistency (1% lows), RAM usage, and stability**.

---

## 2. In-Depth Analysis: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`

### 2.1 OS Subsystem & Architectural Context
- **Subsystems Involved:**
  - **Windows Shell:** `explorer.exe`, `shell32.dll`, `explorerframe.dll`
  - **Compositor:** Desktop Window Manager (`dwm.exe`)
  - **Modern Windows 11 XAML Shell:** `StartDocked.dll`, `Taskbar.dll`, `Windows.UI.Shell.dll`
- **Application Scope:** Per-user (`HKEY_CURRENT_USER`).
- **Update Mechanism:** Many values update in real-time when broadcasting `WM_SETTINGCHANGE` or calling Win32 `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, NULL, NULL)`. Certain process isolation and taskbar shell settings require restarting `explorer.exe`.

---

### 2.2 Complete Value Dictionary (Including All Screenshot Values)

The table below catalogs the exact registry values present in Windows 11 (including every entry visible in the Registry Editor screenshot), their data types, defaults, runtime behaviors, and `/boost` performance recommendations.

#### Group A: UI Latency, DWM Compositor & Animation Speed

| Value Name | Type | Typical Value | Default | What It Does & Subsystem Interaction | `/boost` Latency & Performance Objective |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`TaskbarAnimations`** | `REG_DWORD` | `0` or `1` | `1` | Controls sliding and fading animations for taskbar buttons, flyouts, and window minimize/restore. Managed by DWM. | **Boost Critical:** Set to `0` eliminates 150–250ms animation delay, making window switching and taskbar interaction feel instantaneous. |
| **`ExtendedUIHoverTime`** | `REG_DWORD` | `0`–`400` | `400` (ms) | Milliseconds mouse must hover over a taskbar button before thumbnail live preview appears. | **Boost:** Set to `0` for instantaneous previews, or `10000` to prevent accidental GPU compositor stalls during fast mouse swipes. |
| **`DesktopLivePreviewHoverTime`** | `REG_DWORD` | `0`–`500` | `500` (ms) | Milliseconds mouse must hover over the "Show Desktop" corner before Aero Peek renders all background windows transparent. | **Boost:** Set to `0` (or `50`) eliminates hover delay and prevents DWM compositing hitching. |
| **`DisablePreviewDesktop`** | `REG_DWORD` | `0` or `1` | `0` | Completely disables Aero Peek preview of the desktop on taskbar edge hover. | **Boost:** Set to `1` stops DWM from initiating transparent compositing passes when the mouse accidentally hits the screen edge. |
| **`DisallowShaking`** | `REG_DWORD` | `0` or `1` | `0` | Disables Aero Shake gesture (shaking active window minimizes all background windows). | **Gaming Boost:** Set to `1` prevents accidental window collapse during rapid mouse movements/flicks in windowed/borderless mode. |
| **`ListviewAlphaSelect`** | `REG_DWORD` | `0` or `1` | `1` | Toggles translucent alpha-blended drag selection rectangle on Desktop and File Explorer lists. | **Boost:** Set to `0` uses classic GDI dotted marquee; reduces alpha compositing overhead on low-end or integrated GPUs. |
| **`ListviewShadow`** | `REG_DWORD` | `0` or `1` | `1` | Renders drop shadows beneath desktop icon labels. | **Boost:** Set to `0` disables text shadow rendering passes on the desktop layer. |
| **`TaskbarFlashing`** | `REG_DWORD` | `0` or `1` | `1` | Controls whether taskbar buttons flash orange/red when an application demands focus. | **Boost:** Set to `0` eliminates background flashing distractions during fullscreen gaming and streaming. |

---

#### Group B: Disk I/O, File System Indexing & Explorer Freezes

| Value Name | Type | Typical Value | Default | What It Does & Subsystem Interaction | `/boost` Latency & Performance Objective |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`IconsOnly`** | `REG_DWORD` | `0` or `1` | `0` | Forces File Explorer to display generic file icons instead of extracting thumbnail images (`thumbcache_*.db`). | **Boost Critical:** Set to `1` eliminates massive disk I/O churn, thumbnail cache locking, and COM extractor hangs in large media folders. |
| **`nonetcrawling`** | `REG_DWORD` | `0` or `1` | `0` | Disables Explorer's automatic background polling and crawling of network shares and remote printers. | **Boost Critical:** Set to `1` eliminates the notorious 5–30 second File Explorer freeze when mapped network drives or offline NAS shares are unreachable. |
| **`SeparateProcess`** | `REG_DWORD` | `0` or `1` | `0` | Runs new File Explorer folder windows in an isolated `explorer.exe` process instance. | **Stability Boost:** Set to `1` ensures desktop and taskbar never crash if an individual folder hangs or encounters a bad shell extension. |
| **`PersistBrowsers`** | `REG_DWORD` | `0` or `1` | `0` | Restores previously open folder windows after logging into Windows. | **Boot Boost:** Set to `0` accelerates logon speed and prevents multiple explorer windows from delaying startup. |
| **`HideFileExt`** | `REG_DWORD` | `0` or `1` | `1` | Controls whether file extensions (`.exe`, `.ps1`, `.bat`) are hidden (`1`) or shown (`0`). | **Security & Power-User:** Set to `0` protects against disguised executables (e.g., `document.pdf.exe`). |
| **`Hidden`** | `REG_DWORD` | `1` or `2` | `2` | Controls display of items with `FILE_ATTRIBUTE_HIDDEN` (`1` = Show, `2` = Hide). | **Power-User:** Allows inspecting hidden configuration files and local appdata directories. |
| **`ShowSuperHidden`** | `REG_DWORD` | `0` or `1` | `0` | Controls display of operating system protected files (`SYSTEM + HIDDEN`). | **Maintenance:** Set to `1` during system maintenance; default `0` prevents accidental deletion of critical system files. |
| **`TypeAhead`** | `REG_DWORD` | `0` or `1` | `1` | Allows typing letters in an Explorer list to jump directly to matching filenames. | **Responsiveness:** Keep `1` for immediate keyboard navigation in dense directory trees. |
| **`HideMergeConflicts`**| `REG_DWORD` | `0` or `1` | `0` | Suppresses prompt when merging folders with identical names (`1` = Suppress). | **Workflow:** Accelerates batch file move/copy operations. |
| **`DontPrettyPath`** | `REG_DWORD` | `0` or `1` | `0` | Prevents Windows from converting uppercase 8.3 short paths into title case. | **Technical:** Set to `1` shows true raw filesystem casing. |
| **`ShowInfoTip`** | `REG_DWORD` | `0` or `1` | `1` | Displays pop-up tooltips containing file metadata when hovering over files. | **Boost:** Set to `0` prevents background file metadata lookups on mouse hover. |
| **`ShowCompColor`** | `REG_DWORD` | `0` or `1` | `1` | Displays NTFS compressed files in alternate color (blue text). | **Clarity:** Visual indicator for disk-compressed files. |
| **`ShowEncryptCompressedColor`** | `REG_DWORD` | `0` or `1` | `1` | Displays EFS-encrypted files in green text and compressed in blue. | **Security / Visual:** Instant visual recognition of encrypted items. |
| **`ShowTypeOverlay`** | `REG_DWORD` | `0` or `1` | `1` | Displays small application icon overlay on bottom-right of file thumbnails. | **Visual:** Distinguishes default opening app for thumbnail files. |
| **`ShowStatusBar`** | `REG_DWORD` | `0` or `1` | `1` | Shows bottom status bar with item count, selected count, and file sizes. | **Productivity:** Quick inspection of folder metrics without opening properties. |
| **`NavPaneShowAllFolders`** | `REG_DWORD` | `0` or `1` | `0` | Shows the entire folder hierarchy (including Recycle Bin and Control Panel) in navigation pane. | **Power-User:** Provides full tree-view access to all filesystem roots. |
| **`Filter`** | `REG_DWORD` | `0` or `1` | `0` | Toggles folder column header filtering dropdowns. | **Default:** `0`. |
| **`WebView`** | `REG_DWORD` | `0` or `1` | `1` | Enables web view templates in classic Explorer folders. | **Legacy:** Kept `1` for compatibility with shell folder extensions. |

---

#### Group C: Memory (RAM), Telemetry & Background Cloud Process Reduction

| Value Name | Type | Typical Value | Default | What It Does & Subsystem Interaction | `/boost` Latency & Performance Objective |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`TaskbarDa`** | `REG_DWORD` | `0` or `1` | `1` | Controls the Windows 11 Widgets board ("Desktop Accessories") on the taskbar. | **Boost Critical:** Set to `0` terminates `Widgets.exe` and Edge WebView2 background processes, freeing **150MB–400MB RAM**. |
| **`ShowCopilotButton`** | `REG_DWORD` | `0` or `1` | `1` | Controls the Windows Copilot AI taskbar button. | **Boost:** Set to `0` disables Copilot taskbar hooks and avoids preloading WebView2 runtime. |
| **`TaskbarMn`** | `REG_DWORD` | `0` or `1` | `1` | Controls the Microsoft Teams / Chat button on the Windows 11 taskbar. | **Boost:** Set to `0` prevents Teams autostart triggers and unneeded tray hooks. |
| **`Start_IrisRecommendations`**| `REG_DWORD` | `0` or `1` | `1` | Toggles Windows 11 Start Menu suggested apps and promotional recommendations via the Iris service. | **Privacy & Boost:** Set to `0` disables Microsoft advertising telemetry and background cloud queries in the Start Menu. |
| **`Start_TrackDocs`** | `REG_DWORD` | `0` or `1` | `1` | Tracks recently opened files and surfaces them in Start Menu, Quick Access, and Jump Lists. | **Privacy & Boost:** Set to `0` stops continuous disk writes to `RecentDocs` registry keys and reduces SSD write amplification. |
| **`Start_TrackProgs`** | `REG_DWORD` | `0` or `1` | `1` | Tracks application launch frequency for the Start Menu "Most used" list. | **Privacy & Boost:** Set to `0` halts continuous `UserAssist` telemetry logging in HKCU. |
| **`Start_SearchFiles`** | `REG_DWORD` | `0`, `1`, `2` | `2` | Search indexing scope in Start Menu (`0` = Off, `1` = Favorites, `2` = Files and folders). | **Tuning:** Controls search depth when querying through Start. |
| **`ServerAdminUI`** | `REG_DWORD` | `0` or `1` | `0` | Controls whether Explorer displays Server Administration UI shortcuts. | **System:** Default `0` on client Windows. |
| **`AutoCheckSelect`** | `REG_DWORD` | `0` or `1` | `0` | Displays checkboxes next to files and folders to select multiple items without holding Ctrl. | **Ergonomics:** Tablet/touch optimization; set to `0` for clean desktop mouse usage. |

---

#### Group D: CPU Timer Interruptions & Deep C-State Sleep Preservation

| Value Name | Type | Typical Value | Default | What It Does & Subsystem Interaction | `/boost` / Efficiency Objective |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`ShowSecondsInSystemClock`** | `REG_DWORD` | `0` or `1` | `0` | Displays seconds (`HH:mm:ss`) in the taskbar clock. | **Deep C-State Preservation:** Setting to `1` forces a periodic 1000ms CPU timer interrupt, preventing CPU cores from entering deep idle sleep states (C6/C7). Keep `0` for maximum laptop battery life and lowest thermal output. |

---

#### Group E: Taskbar Ergonomics, Layouts & Clutter Control

| Value Name | Type | Typical Value | Default | What It Does & Subsystem Interaction | Power-User Objective |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`TaskbarAl`** | `REG_DWORD` | `0` or `1` | `1` | Windows 11 taskbar alignment: `0` = Left, `1` = Center. | Set to `0` to restore classic muscle memory on widescreen monitors. |
| **`TaskbarGlomLevel`** | `REG_DWORD` | `0`, `1`, `2` | `0` | Taskbar button grouping: `0` = Always combine, `1` = Combine when full, `2` = Never combine. | Provides window titles and uncombined buttons for multi-tasking. |
| **`MMTaskbarGlomLevel`** | `REG_DWORD` | `0`, `1`, `2` | `0` | Secondary monitor taskbar button grouping (`0`, `1`, `2`). | Synchronizes button grouping across all connected displays. |
| **`MMTaskbarEnabled`** | `REG_DWORD` | `0` or `1` | `1` | Multi-monitor taskbar toggle: `0` = Primary display only, `1` = All displays. | Controls taskbar presence on secondary monitors. |
| **`TaskbarSd`** | `REG_DWORD` | `0`, `1`, `2` | `0` | Multi-monitor button display mode: `0` = All taskbars, `1` = Main taskbar and taskbar where window is open, `2` = Taskbar where window is open only. | Reduces taskbar clutter on secondary displays. |
| **`UseCompactMode`** | `REG_DWORD` | `0` or `1` | `0` | Windows 11 File Explorer compact spacing mode (`1` = Compact). | Increases screen data density by ~30% in folder lists. |
| **`Start_Layout`** | `REG_DWORD` | `0`, `1`, `2` | `0` | Windows 11 Start Menu layout: `0` = Default, `1` = More recommendations, `2` = More pins. | Set to `2` to maximize pinned apps and minimize empty recommendation space. |
| **`ShowTaskViewButton`**| `REG_DWORD` | `0` or `1` | `1` | Toggles the Task View (virtual desktops) button on the taskbar. | Clean taskbar layout when set to `0`. |
| **`TaskbarBadges`** | `REG_DWORD` | `0` or `1` | `1` | Shows notification count badges on taskbar icons. | Clean taskbar layout when set to `0`. |
| **`IsBatteryPercentageEnabled`** | `REG_DWORD` | `0` or `1` | `1` | Displays battery percentage directly on system tray icon. | Hardware monitoring at a glance on mobile devices. |
| **`FontSmoothing`** | `REG_DWORD` | `0`, `1`, `2` | `2` | Font smoothing engine (`0` = Off, `1` = Standard, `2` = ClearType). | Smooth typography across all shell elements. |

---

## 3. In-Depth Analysis: `電腦\HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\Vulkan\ExplicitLayers`

### 3.1 Khronos Vulkan Loader Architecture (`vulkan-1.dll`)
Unlike DirectX or OpenGL (which rely on third-party DLL hooks into `Direct3DCreate9`, `DXGIGetDebugInterface`, or `Present`), Vulkan provides a **formal, standardized layer architecture** managed by the Khronos Vulkan Loader (`vulkan-1.dll`).

The loader sits between the Vulkan application and the hardware driver (ICD). When an application calls `vkCreateInstance()`, the loader parses registry manifests, builds an internal dispatch table, and chains layers together in memory.

```mermaid
graph TD
    App[Vulkan Game / Application] --> Loader[Khronos Loader: vulkan-1.dll]
    
    subgraph Layers [Vulkan Layer Interception Chain]
        Implicit[Implicit Layers: Steam, OBS, Discord, EOS, RTSS]
        Explicit[Explicit Layers: Rockstar Social Club, RenderDoc, Validation]
    end
    
    subgraph Driver [Hardware Graphics Driver]
        ICD[GPU Driver ICD: igvk64.dll / nvoglv64.dll / amdvlk64.dll]
    end
    
    Loader --> Implicit
    Implicit --> Explicit
    Explicit --> ICD
    ICD --> GPU[Physical GPU Hardware]
```

---

### 3.2 Registry Specification: Value Name, Value Type & Value Data
Across all Vulkan layer registry keys:
- **Registry Key Paths:**
  - 64-bit: `HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\Vulkan\ExplicitLayers` (and `HKCU`)
  - 32-bit: `HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Khronos\Vulkan\ExplicitLayers` (and `HKCU`)
- **Value Name:** The **full absolute path** to a layer manifest JSON file (e.g. `C:\Program Files\Rockstar Games\Social Club\SocialClubVulkanLayer.json`).
- **Value Type:** `REG_DWORD`.
- **Value Data Semantics:**
  - **`0` (`0x00000000`):** The layer manifest is **active/registered**. The loader parses the JSON and exposes the layer.
  - **`1` (or any non-zero value):** The loader **ignores and bypasses** this manifest during layer discovery.

---

### 3.3 Explicit Layers vs. Implicit Layers: The Core Architectural Distinction

| Feature | Explicit Layers (`SOFTWARE\Khronos\Vulkan\ExplicitLayers`) | Implicit Layers (`SOFTWARE\Khronos\Vulkan\ImplicitLayers`) |
| :--- | :--- | :--- |
| **Activation Policy** | **Opt-In Only (Dormant by default)**. Never injected unless specifically requested. | **Mandatory Auto-Injection**. Injected into EVERY Vulkan application on the PC. |
| **Trigger Mechanism** | 1. Application requests it by name in `vkCreateInstance()` (`VkInstanceCreateInfo.ppEnabledLayerNames`).<br>2. Explicit user override via `VK_INSTANCE_LAYERS=<layer_name>`.<br>3. Configured via LunarG `VkConfig` override. | The loader automatically parses the key at startup and activates the layer DLL for every Vulkan instance. |
| **Typical Software** | Developer validation (`VK_LAYER_KHRONOS_validation`), game-engine specific hooks (Rockstar Games Social Club), frame capture/debugging (RenderDoc). | Game overlays and capture hooks: Steam Overlay, Epic Online Services, Discord Overlay, OBS Studio Game Capture, RTSS / MSI Afterburner, FPS Monitor. |
| **Failure Mode** | If the JSON or DLL is missing, it only causes an error (`VK_ERROR_LAYER_NOT_PRESENT`) if an application specifically requests that layer. | **Catastrophic Failure Risk:** If the manifest path is broken or corrupted, EVERY Vulkan game or app on the system may crash or fail to initialize on startup. |

---

### 3.4 Live Host System Evidence & Runtime Verification

#### 1. Live Registry Inspection (64-bit vs 32-bit Discrepancy)
Empirical registry queries on the host system revealed:

```text
=== 64-bit Hive: HKLM:\SOFTWARE\Khronos\Vulkan\ExplicitLayers ===
  [0] C:\Program Files\Rockstar Games\Social Club\SocialClubVulkanLayer.json (Exists: True)

=== 64-bit Hive: HKLM:\SOFTWARE\Khronos\Vulkan\ImplicitLayers ===
  [1] C:\ProgramData\obs-studio-hook\obs-vulkan64.json (Disabled in 64-bit)
  [1] C:\Program Files (x86)\Epic Games\...\EOSOverlayVkLayer-Win64.json (Disabled in 64-bit)
  [1] C:\Program Files (x86)\FPS Monitor\tools\fpsmonvk64.json (Disabled in 64-bit)
  [0] C:\Program Files (x86)\Steam\SteamOverlayVulkanLayer64.json (Enabled)
  [0] C:\Program Files (x86)\Steam\SteamFossilizeVulkanLayer64.json (Enabled)

=== 32-bit Hive: HKLM:\SOFTWARE\WOW6432Node\Khronos\Vulkan\ImplicitLayers ===
  [0] C:\ProgramData\obs-studio-hook\obs-vulkan32.json (ENABLED IN 32-BIT!)
  [0] C:\Program Files (x86)\Epic Games\...\EOSOverlayVkLayer-Win32.json (ENABLED IN 32-BIT!)
  [0] C:\Program Files (x86)\FPS Monitor\tools\fpsmonvk32.json (ENABLED IN 32-BIT!)
  [0] C:\Program Files (x86)\Steam\SteamOverlayVulkanLayer.json (Enabled)
  [0] C:\Program Files (x86)\Steam\SteamFossilizeVulkanLayer.json (Enabled)
```

#### 2. Runtime Driver Discovery Verification (PnP vs Khronos)
Querying `HKLM:\SOFTWARE\Khronos\Vulkan\Drivers` returned **zero entries**.  
Querying the Windows PnP Graphics Driver Class registry confirmed modern DCH registration:
```text
HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000
  VulkanDriverName    = C:\WINDOWS\System32\DriverStore\FileRepository\iigd_dch.inf_amd64_9741ef1f4093481f\igvk64.json
  VulkanDriverNameWoW = C:\WINDOWS\System32\DriverStore\FileRepository\iigd_dch.inf_amd64_9741ef1f4093481f\igvk32.json
```

#### 3. Real-Time Loader Diagnostics (`vulkaninfo.exe --summary`)
Executing `vulkaninfo.exe --summary` directly validated layer enumeration:
```text
Instance Layers: count = 3
--------------------------
VK_LAYER_ROCKSTAR_GAMES_social_club Rockstar Games Social Club Layer 1.0.70   version 1
VK_LAYER_VALVE_steam_fossilize      Steam Pipeline Caching Layer     1.4.303  version 1
VK_LAYER_VALVE_steam_overlay        Steam Overlay Layer              1.3.207  version 1
```
- OBS, EOS, and FPS Monitor were omitted from the 64-bit instance layer list because their registry DWORD was `1`.
- Testing environment variable override `$env:VK_LOADER_LAYERS_DISABLE = "~implicit~"` produced live loader confirmation:
  ```text
  WARNING: [Loader Message] Code 0 : Layer "VK_LAYER_VALVE_steam_overlay" forced disabled because name matches filter of env var 'VK_LOADER_LAYERS_DISABLE'.
  WARNING: [Loader Message] Code 0 : Layer "VK_LAYER_VALVE_steam_fossilize" forced disabled because name matches filter of env var 'VK_LOADER_LAYERS_DISABLE'.
  Instance Layers: count = 1 (Only VK_LAYER_ROCKSTAR_GAMES_social_club remained)
  ```

---

### 3.5 Gaming Optimization, Micro-Stutter & Crash Prevention (`/boost`)

1. **Elimination of Orphaned Registry Entries ("Ghost Layers"):**
   When third-party software (e.g. Bandicam, Action!, D3DGear, old graphics SDKs) is uninstalled, uninstallers frequently fail to remove registry keys under `ExplicitLayers` and `ImplicitLayers`. During every game launch, `vulkan-1.dll` executes synchronous file system queries to locate each manifest. If an implicit layer or an enabled explicit layer path does not exist, the game throws `VK_ERROR_LAYER_NOT_PRESENT` (`-6`) or crashes during `vkCreateInstance()`.
2. **Elimination of Swapchain Hook Latency (Micro-Stutters):**
   Implicit layers intercept `vkQueuePresentKHR` and `vkAcquireNextImageKHR`. When multiple overlays run simultaneously (Steam + Epic + Discord + OBS + RTSS), each rendered frame passes through nested interception hooks. This causes frame-time jitter, inconsistent 1% low FPS, and input latency.
3. **Anti-Cheat Integrity:**
   Kernel anti-cheats (BattlEye, Easy Anti-Cheat, Ricochet, Vanguard) audit DLLs loaded into game memory. Stale or rogue Vulkan layer DLLs registered in `HKLM` frequently trigger integrity violations or launch denials.

---

## 4. Production PowerShell Scripts for System Boost

### 4.1 Windows Explorer Latency & Bloat Cleaner Script
```powershell
<#
.SYNOPSIS
    Applies high-performance, low-latency /boost optimizations to Windows Explorer.
.DESCRIPTION
    Disables UI animation lag, terminates background telemetry (Widgets, Copilot, Iris),
    prevents network freezing on offline shares, and isolates explorer processes.
#>

$advPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
$expPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer"

# Ensure key paths exist
if (-not (Test-Path $advPath)) { New-Item -Path $advPath -Force | Out-Null }
if (-not (Test-Path $expPath)) { New-Item -Path $expPath -Force | Out-Null }

Write-Host "Applying Windows Explorer Responsiveness Presets..." -ForegroundColor Cyan

# 1. UI Latency & Responsiveness
Set-ItemProperty -Path $advPath -Name "TaskbarAnimations" -Value 0 -Type DWord
Set-ItemProperty -Path $advPath -Name "DesktopLivePreviewHoverTime" -Value 0 -Type DWord
Set-ItemProperty -Path $advPath -Name "DisallowShaking" -Value 1 -Type DWord
Set-ItemProperty -Path $advPath -Name "SeparateProcess" -Value 1 -Type DWord

# 2. Free Memory & Disable Telemetry Processes
Set-ItemProperty -Path $advPath -Name "TaskbarDa" -Value 0 -Type DWord
Set-ItemProperty -Path $advPath -Name "ShowCopilotButton" -Value 0 -Type DWord
Set-ItemProperty -Path $advPath -Name "Start_IrisRecommendations" -Value 0 -Type DWord

# 3. Disk I/O & Network Optimization
Set-ItemProperty -Path $advPath -Name "nonetcrawling" -Value 1 -Type DWord
Set-ItemProperty -Path $advPath -Name "Start_TrackDocs" -Value 0 -Type DWord
Set-ItemProperty -Path $advPath -Name "IconsOnly" -Value 0 -Type DWord # 1 if maximum speed in media folders desired

# 4. Disable Recent History in Quick Access (native Explorer root)
Set-ItemProperty -Path $expPath -Name "ShowRecent" -Value 0 -Type DWord
Set-ItemProperty -Path $expPath -Name "ShowFrequent" -Value 0 -Type DWord

Write-Host "Restarting explorer.exe to apply changes immediately..." -ForegroundColor Yellow
Stop-Process -Name explorer -Force
```

---

### 4.2 Vulkan Layer Sanitizer & Orphan Cleaner Script
```powershell
<#
.SYNOPSIS
    Cleans orphaned Vulkan layers and resolves VK_ERROR_LAYER_NOT_PRESENT crashes.
.DESCRIPTION
    Audits 64-bit and 32-bit (WOW6432Node) ExplicitLayers and ImplicitLayers hives.
    Purges registry values pointing to nonexistent JSON manifests.
#>

$layerHives = @(
    "HKLM:\SOFTWARE\Khronos\Vulkan\ExplicitLayers",
    "HKLM:\SOFTWARE\Khronos\Vulkan\ImplicitLayers",
    "HKLM:\SOFTWARE\WOW6432Node\Khronos\Vulkan\ExplicitLayers",
    "HKLM:\SOFTWARE\WOW6432Node\Khronos\Vulkan\ImplicitLayers",
    "HKCU:\SOFTWARE\Khronos\Vulkan\ExplicitLayers",
    "HKCU:\SOFTWARE\Khronos\Vulkan\ImplicitLayers"
)

Write-Host "Auditing Vulkan layer manifests across all hives..." -ForegroundColor Cyan

foreach ($hive in $layerHives) {
    if (Test-Path $hive) {
        $key = Get-Item -LiteralPath $hive
        foreach ($manifestPath in $key.GetValueNames()) {
            if (-not (Test-Path -LiteralPath $manifestPath)) {
                Write-Warning "Purging orphaned Vulkan layer manifest: $manifestPath from $hive"
                Remove-ItemProperty -Path $hive -Name $manifestPath -Force
            } else {
                $status = if ($key.GetValue($manifestPath) -eq 0) { "Active (0)" } else { "Disabled (1)" }
                Write-Host "  [OK] $manifestPath ($status) in $hive" -ForegroundColor Green
            }
        }
    }
}

Write-Host "`nTo enable Zero-Overlay Pure Vulkan Gaming Mode in competitive titles, execute:" -ForegroundColor Cyan
Write-Host "setx VK_LOADER_LAYERS_DISABLE `"`~implicit~`"" -ForegroundColor Yellow
```

---

## 5. Summary Matrix for Quick Reference

| Subsystem | Registry Key | Recommended Value | Impact |
| :--- | :--- | :--- | :--- |
| **Explorer** | `TaskbarAnimations` | `0` | Eliminates 150–250ms taskbar animation lag. |
| **Explorer** | `TaskbarDa` | `0` | Terminates Windows 11 Widgets (`Widgets.exe`), freeing 150–400MB RAM. |
| **Explorer** | `nonetcrawling` | `1` | Prevents 5–30s Explorer hangs on offline network shares. |
| **Explorer** | `SeparateProcess` | `1` | Prevents whole desktop/taskbar crash if an Explorer folder hangs. |
| **Vulkan** | `ExplicitLayers` | `0` (for valid path) | Registered layer manifest; loaded only when game specifically requests it. |
| **Vulkan** | `ImplicitLayers` | `1` (or clean orphans) | Prevents uninstalled software from crashing Vulkan games (`VK_ERROR_LAYER_NOT_PRESENT`). |
| **Vulkan Environment** | `VK_LOADER_LAYERS_DISABLE` | `"~implicit~"` | Bypasses all third-party overlay hooks for pure driver latency and maximum 1% low FPS. |
