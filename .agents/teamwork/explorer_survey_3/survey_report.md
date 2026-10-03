# DiskMaster Pro WinUI 3 — Codebase Survey Report: UI, Localization & Build Architecture

**Surveyor**: Explorer 3 (UI Localization Build Explorer)  
**Date**: 2026-10-02  
**Target Milestone**: Codebase Survey for Upgrade (R5 UI Cleanup, Pure 4-Language Parity, Build & Single-File Packaging)  
**Project Root**: `C:\Users\Atszl\Desktop\DiskMasterWinUI`  

---

## Executive Summary

This survey provides a comprehensive architectural and code-level investigation of DiskMaster Pro WinUI 3 with specific focus on **Requirement R5 (UI Clutter, Emoji & Scale Button Cleanup, Bilingual Subtitle Elimination, and 4-Language Parity)** and the **Build & Packaging Architecture**.

### Key Survey Findings
1. **Top-Bar Zoom Clutter**: The `[⚡ Auto] [➖] 100% [➕]` zoom control group is located in `MainWindow.xaml` (lines 256–264) inside `<TabView.TabStripFooter>`. In `MainWindow.xaml.cs`, lines 887–931 reference `ZoomLevelText`, `AutoZoomBtn`, `AutoZoom_Click`, `ZoomIn_Click`, and `ZoomOut_Click`. Keyboard accelerators (`Ctrl +`, `Ctrl -`, `Ctrl 0`) and the Settings page UI scale slider operate independently, allowing the footer buttons to be cleanly removed without losing scaling functionality.
2. **Tab Header Emojis vs Native Fluent Icons**: All 9 tabs in `MainWindow.xaml` already declare native WinUI 3 `FontIconSource` elements with Segoe Fluent Icon glyphs (`&#xEDA2;`, `&#xE896;`, etc.). However, redundant emoji prefixes (e.g. `📊`, `🚀`, `🪟`, `🛠️`, `🩺`, `🔒`, `⚙️`, `⚡`) are hardcoded in both `MainWindow.xaml` and in all 4 language dictionaries inside `LocalizationService.cs`. Tab headers can be cleanly stripped of emojis to achieve native Fluent design.
3. **Verbose Subtitles & Bilingual Parentheses**: Over 50 bilingual parenthetical titles (such as `(Floating Independent Window) — 支援多螢幕拖曳`, `(Reliability Counters)`, `(Boot Repair)`, `(Environment Doctor)`) are scattered across `Controls\FloatingTabWindow.xaml`, `Pages\DiskToolsPage.xaml`, `Pages\BootManagerPage.xaml`, `Pages\SystemHealthPage.xaml`, and their respective code-behind files.
4. **4-Language Parity Architecture Gap (The "Japanese Fallback" Root Cause)**:
   - While `LocalizationService.cs` contains 73 dictionary keys for each of `zh-TW`, `zh-CN`, `en-US`, and `ja-JP`, almost none of the page content uses these dictionary keys.
   - Instead, **336 hardcoded inline ternary expressions** using `bool isZh = LocalizationService.Instance.IsChinese;` are spread across 14 C# files (`*Page.xaml.cs`, `Helpers`, `MainWindow.xaml.cs`).
   - Because `IsChinese` is defined as `CurrentLanguage == "zh-TW" || CurrentLanguage == "zh-CN"`, switching to Japanese (`ja-JP`) causes `isZh` to be `false`, causing **all 336 UI strings across all pages to fall back directly to English**! Furthermore, Simplified Chinese (`zh-CN`) receives untranslated Traditional Chinese.
5. **Build & Single-File Packaging**:
   - `DiskMasterWinUI.csproj` targets `net9.0-windows10.0.26100.0` as an unpackaged (`WindowsPackageType=None`), self-contained WinUI 3 application.
   - `dotnet build DiskMasterWinUI.csproj -c Release` compiles cleanly with **0 errors and 0 warnings**.
   - `build_portable_singlefile.ps1` produces a true standalone portable executable (`installer_output\DiskMaster_Portable.exe`, ~131MB) via an embedded `payload.zip` extracted at runtime into `%LocalAppData%\DiskMaster_Portable\app_{ticks}` by a dedicated launcher project (`tools\DiskMasterPortableLauncher`).
   - **Test Suite**: No unit test projects or automated test suites currently exist in the solution.

---

## Part 1: R5 Survey — UI Clutter, Emoji & Scale Button Cleanup

### 1.1 Top-Bar Zoom Control Group in `TabView.TabStripFooter`

#### Exact Location
- **File**: `MainWindow.xaml`
- **Lines**: 256–264
- **Current Implementation**:
```xml
<!-- UI Zoom Controller with Auto Detect Button -->
<StackPanel Orientation="Horizontal" Spacing="3" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            CornerRadius="4" Padding="4,2" ToolTipService.ToolTip="介面縮放 / 快捷鍵: Ctrl + +/-">
    <Button x:Name="AutoZoomBtn" Content="⚡ Auto" Click="AutoZoom_Click" Padding="6,2" FontSize="11"
            ToolTipService.ToolTip="自動偵測螢幕最佳比例" />
    <Button Content="➖" Click="ZoomOut_Click" Padding="6,2" FontSize="11" />
    <TextBlock x:Name="ZoomLevelText" Text="100%" VerticalAlignment="Center" Padding="4,0" FontSize="11" FontWeight="SemiBold" />
    <Button Content="➕" Click="ZoomIn_Click" Padding="6,2" FontSize="11" />
</StackPanel>
```

#### Code-Behind Dependencies in `MainWindow.xaml.cs`
- **Line 27**: `private double _currentZoom = 1.0;`
- **Lines 718–734**: `SetupKeyboardAccelerators()` registers `Ctrl + Add`, `Ctrl + Subtract`, and `Ctrl + Number0` to call `ZoomIn()`, `ZoomOut()`, and `SetZoom(1.0)`.
- **Lines 835–849**: `ZoomIn()` and `ZoomOut()` helper methods.
- **Lines 851–875**: `UpdateViewportLayout()` applies `RootScaleTransform.ScaleX = factor;` to scale the page content frame.
- **Lines 887–899**:
```csharp
private void SetZoom(double factor)
{
    _currentZoom = factor;
    ZoomLevelText.Text = $"{(int)(factor * 100)}%"; // Direct field access to TextBlock!
    UpdateViewportLayout();

    var s = SettingsService.Instance.Current;
    if (s.ScalePercent != (int)(factor * 100))
    {
        s.ScalePercent = (int)(factor * 100);
        SettingsService.Instance.Save();
    }
}
```
- **Lines 901–902**: Event handlers `ZoomIn_Click` and `ZoomOut_Click`.
- **Lines 922–931**: `AutoDetectZoom()` and `AutoZoom_Click`.

#### Cleanup Impact Analysis
1. Removing the `StackPanel` zoom control from `MainWindow.xaml` directly fulfills R5 decluttering.
2. In `MainWindow.xaml.cs`, `ZoomLevelText.Text = ...` will throw a `NullReferenceException` if the `TextBlock` is removed from XAML without changing line 890 to a null-conditional check: `if (ZoomLevelText != null) ZoomLevelText.Text = ...;` (or removing the line entirely).
3. `AutoZoom_Click`, `ZoomIn_Click`, and `ZoomOut_Click` in `MainWindow.xaml.cs` can be removed or made private helpers.
4. Scale adjustment remains available through `SettingsPage.xaml` (`ScaleBox` and `AutoDetectScaleBtn`) and keyboard shortcuts (`Ctrl +`, `Ctrl -`, `Ctrl 0`), ensuring users still retain zoom capabilities if needed.

---

### 1.2 Redundant Emojis in Tab Headers vs Fluent `FontIconSource`

#### Current Implementation in `MainWindow.xaml`
In `MainWindow.xaml` (lines 81–234), each `TabViewItem` contains both an emoji in `Header` AND a native `FontIconSource`:

| Tab Element | Current Header in XAML | Native FontIconSource Glyph | Intended Clean Header |
|---|---|---|---|
| `TabEasyMode` (line 82) | `Header="📊 簡易模式"` | `&#xEDA2;` (Diagnostic) | `Header="簡易模式"` |
| `TabWimDeploy` (line 99) | `Header="🚀 系統部署"` | `&#xE896;` (Download/Cloud) | `Header="系統部署"` |
| `TabBootManager` (line 116) | `Header="🪟 引導管理"` | `&#xE7C3;` (Page/Document) | `Header="引導管理"` |
| `TabDiskTools` (line 133) | `Header="🛠️ 磁碟工具"` | `&#xE90F;` (Repair/Tools) | `Header="磁碟工具"` |
| `TabSystemHealth` (line 150) | `Header="🩺 系統修復"` | `&#xEC06;` (Heart/Pulse) | `Header="系統修復"` |
| `TabNtfsPermissions` (line 167) | `Header="🔒 權限管理"` | `&#xE72E;` (Lock) | `Header="權限管理"` |
| `TabAdvancedMode` (line 184) | `Header="⚙️ 進階腳本"` | `&#xEC7A;` (CommandPrompt) | `Header="進階腳本"` |
| `TabOptimizer` (line 201) | `Header="⚡ 最佳化"` | `&#xE945;` (SpeedHigh) | `Header="最佳化"` |
| `TabSettings` (line 218) | `Header="⚙️ 設定"` | `&#xE713;` (Setting) | `Header="設定"` |

#### Dynamic Language Override in `MainWindow.xaml.cs`
In `MainWindow.xaml.cs` (lines 752–760), `ApplyLanguage()` overwrites every tab header dynamically from `LocalizationService`:
```csharp
TabEasyMode.Header = loc["EasyMode"];
TabWimDeploy.Header = loc["WimDeploy"];
TabBootManager.Header = loc["BootManager"];
TabDiskTools.Header = loc["DiskTools"];
TabSystemHealth.Header = loc["SystemRepair"];
TabNtfsPermissions.Header = loc["NtfsPermissions"];
TabAdvancedMode.Header = loc["AdvancedMode"];
TabOptimizer.Header = loc["Optimizer"];
TabSettings.Header = loc["Settings"];
```

#### Dictionary Values in `Services\LocalizationService.cs`
The dictionary values in `LocalizationService.cs` currently store the emoji in the string for **all four languages**:
```csharp
// zh-TW (lines 24-32)
["EasyMode"] = "📊 簡易模式", ["WimDeploy"] = "🚀 系統部署", ["BootManager"] = "🪟 引導管理", ...
// zh-CN (lines 100-108)
["EasyMode"] = "📊 简易模式", ["WimDeploy"] = "🚀 系统部署", ["BootManager"] = "🪟 引导管理", ...
// en-US (lines 176-184)
["EasyMode"] = "📊 Easy Mode", ["WimDeploy"] = "🚀 WIM Deploy", ["BootManager"] = "🪟 Boot Manager", ...
// ja-JP (lines 252-260)
["EasyMode"] = "📊 かんたんモード", ["WimDeploy"] = "🚀 システム展開", ["BootManager"] = "🪟 ブート管理", ...
```

#### Verification of Native Fluent Icon Rendering
When the emojis are removed from the string (e.g. `"簡易模式"`, `"Easy Mode"`, `"かんたんモード"`), WinUI 3's `TabViewItem` automatically displays the `<FontIconSource>` glyph on the left of the header text. This completely eliminates the double-icon artifact where an emoji was rendered directly adjacent to a Segoe Fluent font icon.

---

### 1.3 Verbose Subtitles & Bilingual Parentheses Inventory

A codebase-wide regex search identified verbose subtitles and bilingual parentheses in both XAML markup and code-behind assignments:

#### 1. Controls & Windows
- **`Controls\FloatingTabWindow.xaml` (line 37)**:
  - Current: `Text="獨立浮動分頁視窗 (Floating Independent Window) — 支援多螢幕拖曳"`
  - Proposed clean: `Text="獨立浮動分頁視窗"` (or localized: `loc["FloatingWindowNotice"]`)
  - Line 42: `Text="📥 嵌回主視窗 (Dock Back)"` -> `Text="📥 嵌回主視窗"`
- **`Controls\AvatarCompanionControl.xaml` (lines 27–41)**:
  - `Text="📌 停靠位置 (Dock Position)"` -> `Text="停靠位置"`
  - `Text="↘️ 右下角 (Bottom-Right)"` -> `Text="右下角"`
  - `Text="🔍 助手尺寸 (Size)"` -> `Text="助手尺寸"`
  - `Text="🎭 表情模式 (Emotion)"` -> `Text="表情模式"`
- **`Controls\BitLockerUnlockDialog.xaml` (lines 34–35)**:
  - `Content="🔑 使用 48 位元修復金鑰 (Recovery Key)"` -> `Content="使用 48 位元修復金鑰"`
  - `Content="🔒 使用使用者密碼 (Password)"` -> `Content="使用密碼"`

#### 2. `Pages\DiskToolsPage.xaml` & `DiskToolsPage.xaml.cs`
- **Line 313 & CS Line 32**:
  - Current: `Header="📊 詳細可靠性指標 (Reliability Counters)"`
  - Proposed clean:
    - `zh-TW`: `可靠性指標`
    - `zh-CN`: `可靠性指标`
    - `en-US`: `Reliability Counters`
    - `ja-JP`: `信頼性カウンター`
- **CS Lines 23–25**:
  - `TabKeys.Header`: `"金鑰與硬體序號 (Keys & Serials)"` -> `"金鑰與硬體序號"`
  - `TabEnv.Header`: `"環境健檢與套件補齊 (Environment Doctor)"` -> `"環境健檢與套件補齊"`
  - `TabOptimize.Header`: `"進階維護與救援 (Optimization & Recovery)"` -> `"進階維護與救援"`
  - `TabHealth.Header`: `"🩺 磁碟健康 (Disk Health)"` -> `"磁碟健康"`
- **XAML Lines 163, 181, 190, 286, 419, 469, 482, 484, 486, 501, 665, 699, 770**:
  - `🌡️ 核心溫度 (Composite Temp)` -> `核心溫度`
  - `📝 累計總寫入量 (TBW)` -> `累計總寫入量`
  - `📖 累計總讀取量 (TBR)` -> `累計總讀取量`
  - `ℹ️ 磁碟健康與基礎遙測摘要 (Basic Hardware Diagnostics)` -> `磁碟健康與基礎遙測摘要`
  - `UEFI / BIOS OEM 原廠嵌入金鑰 (MSDM Table)` -> `UEFI / BIOS OEM 原廠嵌入金鑰`
  - `主機板與 BIOS 實體硬體序號 (Hardware Identification)` -> `主機板與硬體序號`
  - `BIOS 序號 (Serial Number)` -> `BIOS 序號`
  - `主機板型號 (Motherboard Product)` -> `主機板型號`
  - `原廠製造商 (Manufacturer)` -> `製造商`
  - `離線 Windows 金鑰提取器 (Offline System Registry Hive)` -> `離線 Windows 金鑰提取器`
  - `vssadmin: 磁碟區陰影複本 (Volume Shadow Copies)` -> `磁碟區陰影複本`
  - `System Restore: 系統還原點 (Restore Points)` -> `系統還原點`
  - `🔓 永久解密 (Decrypt)` -> `永久解密`

#### 3. `Pages\BootManagerPage.xaml` & `BootManagerPage.xaml.cs`
- **CS Lines 23, 29, 30, 31, 34, 35**:
  - `DeepScanBtnText.Text`: `"🔍 全盤探測並掛載開機 (Deep Scan)"` -> `"全盤探測並掛載開機"`
  - `TabBootEntries.Header`: `"開機項目管理 (Boot Entries)"` -> `"開機項目管理"`
  - `TabBuildBoot.Header`: `"全新分割區建置開機引導 (Build New Boot Partition)"` -> `"全新分割區建置開機引導"`
  - `TabGlobalSettings.Header`: `"全域開機設定 (Global Settings)"` -> `"全域開機設定"`
  - `EditEntryTitle.Text`: `"編輯開機項目 (Edit Boot Entry)"` -> `"編輯開機項目"`
  - `DescBox.Header`: `"開機選單顯示名稱 (Description)"` -> `"開機選單顯示名稱"`

#### 4. `Pages\AdvancedModePage.xaml` & `AdvancedModePage.xaml.cs`
- **CS Lines 48, 51, 56**:
  - `TabBootRepair.Header`: `"開機修復 (Boot Repair)"` -> `"開機修復"`
  - `TabBcdEdit.Header`: `"BCD 項目管理 (BCDEdit)"` -> `"BCD 項目管理"`
  - `TabFullLog.Header`: `"完整執行記錄 (Full Log)"` -> `"完整執行記錄"`

#### 5. `Pages\EasyModePage.xaml.cs`
- **CS Lines 47, 65, 71, 77**:
  - `ToolsExpander.Header`: `"磁碟分割區與磁碟區管理工具 (Partition & Volume Tools)"` -> `"磁碟分割區與磁碟區管理工具"`
  - `CleanDiskBtn.Content`: `"🧹 清除磁碟 (Clean)"` -> `"清除磁碟"`
  - `ExtendFsBtn.Content`: `"↔️ 延伸檔案系統 (Extend FS)"` -> `"延伸檔案系統"`
  - `OutputLogTitle.Text`: `"執行終端記錄 (Output Log)"` -> `"執行終端記錄"`

#### 6. `Pages\SystemHealthPage.xaml.cs`
- **CS Lines 31, 51, 58, 59**:
  - `AdvCleanupExpander.Header`: `"🧹 進階清理 (Advanced Cleanup)"` -> `"進階清理"`
  - `OfflineRepairExpander.Header`: `"🧰 WinPE 離線系統修復 (Offline Image & SFC Repair)"` -> `"WinPE 離線系統修復"`
  - `WindowsUpdateExpander.Header`: `"🔄 Windows Update 修復與更新管理 (Update Repair & Reset)"` -> `"Windows Update 修復與更新管理"`
  - `TempCleanExpander.Header`: `"🧹 系統暫存與垃圾深度清理 (System Temp & Deep Cache Cleanup)"` -> `"系統暫存與垃圾深度清理"`

#### 7. `Pages\SystemOptimizerPage.xaml.cs`
- **CS Lines 27, 28, 29, 30, 32**:
  - `TabGaming.Header`: `"🎮 電競遊戲模式 (Gaming Mode)"` -> `"電競遊戲模式"`
  - `TabCpu.Header`: `"⚡ CPU與排程優先權 (CPU & Task)"` -> `"CPU與排程優先權"`
  - `TabPower.Header`: `"🔋 電源計畫與核心喚醒 (Power Plans)"` -> `"電源計畫與核心喚醒"`
  - `TabBackup.Header`: `"💾 登錄檔備份與還原 (Backup & Restore)"` -> `"登錄檔備份與還原"`
  - `HeroTitle.Text`: `"電競遊戲極致模式 (Ultra Gaming Mode)"` -> `"電競遊戲極致模式"`

---

### 1.4 Localization Architecture & 4-Language Parity Analysis

#### Root Cause of the "Japanese Fallback" Bug
The project's localization is centralized in `Services\LocalizationService.cs`. In this class:
```csharp
[ObservableProperty]
private string _currentLanguage = "zh-TW";

public bool IsChinese => CurrentLanguage == "zh-TW" || CurrentLanguage == "zh-CN";
```
However, instead of querying `LocalizationService.Instance[key]`, each page's code-behind defines its own `ApplyLanguage()` method with:
```csharp
bool isZh = LocalizationService.Instance.IsChinese;
SomeControl.Text = isZh ? "傳統中文文字 (English Parentheses)" : "English Text";
```
Because of this pattern:
1. When `CurrentLanguage == "ja-JP"`, `IsChinese` returns `false`.
2. Every ternary evaluates to the `false` branch: the **English** string!
3. Across the entire application, **336 ternary expressions** exhibit this exact behavior:
   - `Pages\SettingsPage.xaml.cs`: 91 instances
   - `Pages\WimDeployPage.xaml.cs`: 57 instances
   - `Pages\EasyModePage.xaml.cs`: 45 instances
   - `Pages\SystemHealthPage.xaml.cs`: 33 instances
   - `Pages\AdvancedModePage.xaml.cs`: 27 instances
   - `Pages\NtfsPermissionsPage.xaml.cs`: 25 instances
   - `Pages\BootManagerPage.xaml.cs`: 18 instances
   - `Pages\DiskToolsPage.xaml.cs`: 18 instances
   - `Pages\SystemOptimizerPage.xaml.cs`: 9 instances
   - `MainWindow.xaml.cs`: 5 instances
   - `Helpers\DialogHelper.cs`: 4 instances
   - `ViewModels\SettingsViewModel.cs`: 2 instances
   - `ViewModels\WimDeployViewModel.cs`: 1 instance
   - `Controls\StatusBarControl.xaml.cs`: 1 instance

#### Key Distribution in `LocalizationService.cs`
- Existing keys in `LocalizationService.cs`: **73 keys** per language dictionary.
- Key parity check (`check_keys.ps1`): All 73 keys are present in all four dictionaries (`zh-TW`, `zh-CN`, `en-US`, `ja-JP`).
- **The Gap**: The 73 keys cover only top-level navigation tabs, workspace names, and basic actions. The other **336 page-level UI strings** were never added to `LocalizationService.cs` dictionaries and rely entirely on the broken `isZh ? ... : ...` ternary logic!

---

## Part 2: Build & Packaging Architecture

### 2.1 Project Configuration & Dependencies (`DiskMasterWinUI.csproj`)

```xml
<TargetFramework>net9.0-windows10.0.26100.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<Platforms>x86;x64;ARM64</Platforms>
<OutputType>WinExe</OutputType>
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

#### Package References
- `CommunityToolkit.Mvvm` (Version 8.4.2)
- `Microsoft.Windows.SDK.BuildTools` (Version *)
- `Microsoft.WindowsAppSDK` (Version *)
- `Microsoft.Windows.SDK.BuildTools.WinApp` (Version *)
- `System.Management` (Version 10.0.12)

#### Special Build Targets in `DiskMasterWinUI.csproj`
- `EnsureResourcesPriAndXbf`: Ensures `resources.pri` exists in `$(TargetDir)` after build.
- `EnsurePublishResources`: Ensures `resources.pri` and all `*.xbf` files are copied to the publish directory so unpackaged WinUI 3 controls resolve their XAML binary format and string tables at runtime without MSIX packaging.

### 2.2 Compilation Verification
Executing:
```powershell
dotnet build DiskMasterWinUI.csproj -c Release
```
- **SDK**: .NET SDK 9.0.318
- **Platform**: `win-x64`
- **Output**: `bin\Release\net9.0-windows10.0.26100.0\win-x64\DiskMasterWinUI.dll`
- **Result**: **0 Warnings, 0 Errors** (Clean build verified).

### 2.3 Single-File Standalone Portable Packaging (`build_portable_singlefile.ps1`)

The packaging pipeline produces `installer_output\DiskMaster_Portable.exe`:
```
[1/4] dotnet publish DiskMasterWinUI.csproj -c Release -r win-x64 --self-contained true -o publish
       + Copy-Item Scripts -> publish\
[2/4] Zip publish/ -> tools/DiskMasterPortableLauncher/payload.zip
[3/4] dotnet publish tools/DiskMasterPortableLauncher/DiskMasterPortableLauncher.csproj
       -c Release -r win-x64 --self-contained true
       -p:PublishSingleFile=true
       -p:IncludeNativeLibrariesForSelfExtract=true
       -p:EnableCompressionInSingleFile=true
       -o temp_out
[4/4] Copy temp_out/DiskMasterPortableLauncher.exe -> installer_output/DiskMaster_Portable.exe
```

#### Launcher Runtime Behavior (`tools\DiskMasterPortableLauncher\Program.cs`)
1. Embedded Resource: The launcher embeds `payload.zip`.
2. Extraction Directory:
   - Normal Windows: `%LocalAppData%\DiskMaster_Portable\app_{ticks}`
   - WinPE Environment: `X:\...\Temp\DiskMaster_PE\app_{ticks}`
3. Cache Invalidation: Uses the launcher executable's `LastWriteTimeUtc.Ticks` to detect when a new version is executed, extracting only once per build. Older versions in `%LocalAppData%\DiskMaster_Portable` are cleaned up asynchronously.
4. Elevation: Starts extracted `DiskMasterWinUI.exe` with `Verb = "runas"`, ensuring administrator privileges.
5. Current Artifact: `installer_output\DiskMaster_Portable.exe` (131,159,994 bytes / ~125.08 MB).

### 2.4 Existing Tests Assessment
- **Unit Test Projects**: None found in the repository.
- **Test Suites / Frameworks**: Neither MSTest, NUnit, nor xUnit is referenced or installed.
- **Solution File**: No `.sln` file exists; the workspace consists of `DiskMasterWinUI.csproj` and `tools\DiskMasterPortableLauncher\DiskMasterPortableLauncher.csproj`.

---

## Part 3: Actionable Architecture Roadmap for Implementers

### Phase 1: Top-Bar Zoom Cleanup (MainWindow.xaml & MainWindow.xaml.cs)
1. Delete the `StackPanel` containing `AutoZoomBtn`, `ZoomOut_Click`, `ZoomLevelText`, and `ZoomIn_Click` from `MainWindow.xaml` (lines 256–264).
2. In `MainWindow.xaml.cs`, guard line 890:
   ```csharp
   if (ZoomLevelText != null) ZoomLevelText.Text = $"{(int)(factor * 100)}%";
   ```
3. Remove unused event handlers `ZoomIn_Click`, `ZoomOut_Click`, and `AutoZoom_Click` from `MainWindow.xaml.cs`.
4. Keep `_currentZoom`, `SetZoom()`, `AutoDetectZoom()`, and keyboard accelerators (`Ctrl +`, `Ctrl -`, `Ctrl 0`) so user scaling via keyboard or Settings page continues to function cleanly.

### Phase 2: Tab Header Emojis & Bilingual Parentheses Cleanup
1. In `MainWindow.xaml`, strip emojis from all 9 `TabViewItem.Header` attributes (e.g. `Header="簡易模式"`).
2. In `Services\LocalizationService.cs`, update dictionary entries for all four languages to remove emojis from tab keys:
   - `EasyMode`: `"簡易模式"`, `"简易模式"`, `"Easy Mode"`, `"かんたんモード"`
   - `WimDeploy`: `"系統部署"`, `"系统部署"`, `"WIM Deploy"`, `"システム展開"`
   - `BootManager`: `"引導管理"`, `"引导管理"`, `"Boot Manager"`, `"ブート管理"`
   - `DiskTools`: `"磁碟工具"`, `"磁盘工具"`, `"Disk Tools"`, `"ディスクツール"`
   - `SystemRepair`: `"系統修復"`, `"系统修复"`, `"System Repair"`, `"システム修復"`
   - `NtfsPermissions`: `"權限管理"`, `"权限管理"`, `"Permissions"`, `"権限管理"`
   - `AdvancedMode`: `"進階腳本"`, `"高级脚本"`, `"Advanced Scripts"`, `"高度スクリプト"`
   - `Optimizer`: `"最佳化"`, `"优化加速"`, `"Optimizer"`, `"最適化加速"`
   - `Settings`: `"設定"`, `"设置"`, `"Settings"`, `"設定"`
3. Remove parenthetical English subtitles across XAML headers and buttons:
   - `Controls\FloatingTabWindow.xaml`: Strip `(Floating Independent Window) — 支援多螢幕拖曳` and `(Dock Back)`.
   - `Pages\DiskToolsPage.xaml`: Change `📊 詳細可靠性指標 (Reliability Counters)` to clean header.
   - Clean up subtitles in `AdvancedModePage`, `BootManagerPage`, `SystemHealthPage`, `SystemOptimizerPage`, and `NtfsPermissionsPage`.

### Phase 3: Pure 4-Language Parity Implementation
To completely eradicate the "Japanese falls back to English" bug:
1. Provide a 4-language helper method in `LocalizationService.cs`:
   ```csharp
   public string S(string zhTw, string zhCn, string enUs, string jaJp) => CurrentLanguage switch
   {
       "zh-CN" => zhCn,
       "en-US" => enUs,
       "ja-JP" => jaJp,
       _ => zhTw
   };
   ```
2. Refactor all 336 ternary instances across the 14 files or centralize them into `LocalizationService` dictionary keys so that when `CurrentLanguage == "ja-JP"`, authentic Japanese text is displayed rather than the English fallback branch.
3. Ensure Simplified Chinese (`zh-CN`) receives proper simplified terms rather than identical Traditional Chinese strings.

---

## Conclusion
The codebase is clean and builds without warnings in Release mode. The UI clutter, emoji duplication, and Japanese fallback issues stem from identifiable, localized patterns: hardcoded emojis in tab strings alongside `FontIconSource`, and binary `isZh` ternary statements across page code-behind files. Remediation is fully scoped, low-risk, and requires no external dependency changes.
