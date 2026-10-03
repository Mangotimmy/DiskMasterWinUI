# DiskMaster Pro — E2E Test Infrastructure Specification (`TEST_INFRA.md`)

## 1. Overview & Architecture

The DiskMaster Pro E2E Test Suite is an opaque-box, requirements-driven testing framework designed to validate Windows system utility functionality without requiring access to internal private state. It directly verifies user requirements (**R1 to R8**) and feature inventory (**F1 to F16**) through public domain service contracts, compiled assemblies, native Windows subsystem CLIs (`pnputil`, `bcdedit`, `powercfg`), file system assets, and registry structures.

```
tests/
├── harness/
│   ├── TestFramework.ps1       # Core assertion engine, test registration, runner & reporting
│   └── TestOracles.ps1         # Authoritative domain oracles, spec generators, reference constants
├── tier1_features/             # Tier 1: Feature Coverage (>=5 tests per feature, 80 tests total)
│   ├── Test_Tier1_F01_To_F04_OemDrivers.ps1
│   ├── Test_Tier1_F05_To_F07_StorageEncyclopedia.ps1
│   ├── Test_Tier1_F08_To_F10_SafeBootAndBcd.ps1
│   ├── Test_Tier1_F11_To_F13_PowerAndLatency.ps1
│   └── Test_Tier1_F14_To_F16_AssetsLocGuide.ps1
├── tier2_boundaries/           # Tier 2: Boundary & Corner Cases (>=5 tests per feature, 80 tests total)
│   ├── Test_Tier2_F01_To_F04_DriverBoundaries.ps1
│   ├── Test_Tier2_F05_To_F07_StorageBoundaries.ps1
│   ├── Test_Tier2_F08_To_F10_BootBoundaries.ps1
│   ├── Test_Tier2_F11_To_F13_PowerBoundaries.ps1
│   └── Test_Tier2_F14_To_F16_AssetLocBoundaries.ps1
├── tier3_combinations/         # Tier 3: Cross-Feature Interactions (15 pairwise tests)
│   └── Test_Tier3_CrossFeatureInteractions.ps1
├── tier4_scenarios/            # Tier 4: Real-World Application Scenarios (10 end-to-end workflows)
│   └── Test_Tier4_RealWorldScenarios.ps1
└── Run-E2ETests.ps1            # Top-level executable test runner CLI
```

---

## 2. 4-Tier Test Methodology

The test suite enforces a rigorous 4-Tier verification hierarchy:

### Tier 1: Feature Coverage (80 Test Cases)
- **Scope**: Every feature from F1 to F16 has a minimum of 5 dedicated test cases verifying primary behavior (happy path) and public interface contracts.
- **F1 (OEM Driver Enumeration)**: 5 tests covering English, Traditional Chinese, Simplified Chinese, Japanese PnPUtil blocks, and multi-driver stream parsing.
- **F2 (OEM Driver Details Inspection)**: 5 tests covering `SignerName` property, publisher extraction, Chinese/Japanese signers, and metadata contracts.
- **F3 (OEM Driver Multi-Selection & Batch Removal)**: 5 tests covering `IsSelected` boolean toggle, independent multi-selection, batch deletion contract, and ViewModel commands.
- **F4 (OEM Driver Force Uninstall)**: 5 tests covering `/delete-driver`, mandatory `/uninstall` switch, `/force` switch, and service return contract.
- **F5 (Storage Directory Scanning)**: 5 tests covering 11 directory definitions, environment path resolution, size calculation, item counts, and non-existent path resilience.
- **F6 (Storage Directory Functional Encyclopedia)**: 5 tests covering safety classification mappings, WinSxS protection guardrails, Temp purge rating, and localized description bindings.
- **F7 (Storage Directory One-Click Actions)**: 5 tests covering Explorer launching, Temp safe clean, WinSxS clean blocking, DISM component cleanup command line.
- **F8 (MSConfig Safe Boot Modes)**: 5 tests covering Minimal, AlternateShell, Network, DsRepair, and Normal mode BCD commands.
- **F9 (Advanced Boot Flags Configuration)**: 5 tests covering NoGuiBoot, BootLog, BaseVideo, SOS, TestSigning, NoIntegrityChecks, HypervisorLaunchType syntax.
- **F10 (BCD Query & Reboot Prompt)**: 5 tests covering active BCD configuration queries, SafeBoot parsing, set mode contracts, and reboot prompts.
- **F11 (Unhide Hidden CPU Power Attributes)**: 5 tests covering `SUB_PROCESSOR` GUID (`54533251-82be-4824-96c1-47b60b740d00`), all 7 PPM attribute GUIDs, unhide/hide CLI syntax.
- **F12 (Hidden Latency & Kernel Tweaks)**: 5 tests covering Dynamic Ticking (`disabledynamictick yes`), LargeSystemCache (1), DisablePagingExecutive (1).
- **F13 (Complete Hiberfil.sys Cleanup)**: 5 tests covering `powercfg /hibernate off`, deletion verification, reclaimed GB conversion, and service contracts.
- **F14 (Modern Fluent Icon Redesign)**: 5 tests covering `Assets/AppIcon.ico` (6 layers: 256, 128, 64, 48, 32, 16 at 32 bpp), required PNG assets, and vibrant Fluent palette.
- **F15 (Full 4-Language Localization Parity)**: 5 tests covering 100% dictionary parity across `zh-TW`, `zh-CN`, `en-US`, `ja-JP`, convenience helper `T(...)`, and dynamic switching.
- **F16 (Comprehensive GitHub Guide)**: 5 tests covering root `README.md` existence, feature modules, architecture, benchmarks, and build guide.

### Tier 2: Boundary & Corner Cases (80 Test Cases)
- **Scope**: Every feature from F1 to F16 has a minimum of 5 edge-case tests validating boundary conditions, malformed input streams, buffer limits, and exception resilience.
- **F1–F4**: Empty streams, missing metadata fields, whitespace variations, Unicode provider names, 500-driver stream stress, unsigned drivers, 500-char publisher DNs, non-semver versions, reboot-required exit code 3010, path traversal sanitization, and quoted INF names.
- **F5–F7**: `System Volume Information` ACL permission access denial handling, directory junction recursion guards, paths exceeding 260 characters, cancellation token honoring, locked file inspection, 0-byte formatting ("0 B"), byte scale boundaries (1023B, 1KB, 1MB, 1GB), and non-elevated DISM exit code 740 handling.
- **F8–F10**: Idempotent safe boot mode setting, AlternateShell to Minimal transition, absent safeboot value handling, case-insensitive mode input, invalid mode rejection, simultaneous flag toggling, "Element not found" deletion resilience, and localized BCD headers.
- **F11–F13**: 128-bit GUID format validation, malformed GUID rejection, AC value index syntax, core parking percentage clamping [0, 100], missing registry value fallback, Dynamic Tick safety, and hiberfil polling timeout safety.
- **F14–F16**: ICO binary header verification (Reserved=0, Type=1, Count>=6), scale-200 PNG pixel dimension validation, missing key fallback to key name, Japanese Kanji/Kana encoding integrity (mojibake prevention), and Markdown syntax validation.

### Tier 3: Cross-Feature Interactions (15 Test Cases)
- Validates pairwise integration between modules:
  - Driver Removal + Localization (`T3.01`)
  - Storage Encyclopedia + Localization (`T3.02`)
  - Safe Boot + Localization (`T3.03`)
  - CPU Power Tuning + Kernel Latency Tweaks (`T3.04`)
  - Storage Analyzer + Hiberfil Purge Recalculation (`T3.05`)
  - Storage Analyzer + DISM Component Store Cleanup (`T3.06`)
  - Driver Multi-Selection + Force Uninstall (`T3.07`)
  - Safe Boot Minimal + NoGuiBoot Flags (`T3.08`)
  - CPU Power Attributes + Active Power Scheme (`T3.09`)
  - Storage Temp Cleanup + Size Recalculation (`T3.10`)
  - BCD Query + Localized Reboot Prompt (`T3.11`)
  - Localization + Asset Icon Window Branding (`T3.12`)
  - Driver Enumeration + DriverStore File Repository (`T3.13`)
  - Kernel Latency Tweaks + Rollback Snapshot (`T3.14`)
  - Safe Boot AlternateShell + BootLog Flag (`T3.15`)

### Tier 4: Real-World Application Scenarios (10 Test Cases)
- Validates realistic multi-step user workflows:
  - SSD Space Reclamation Workflow (`T4.01`)
  - Legacy Driver Audit and Force Batch Removal (`T4.02`)
  - Emergency Safe Boot Diagnostic & Normal Recovery (`T4.03`)
  - Competitive Gaming Latency & Power Optimization (`T4.04`)
  - Multilingual Enterprise Deployment Dynamic Switching (`T4.05`)
  - Windows Component Store Deep Hygiene (`T4.06`)
  - Complete System Optimizer Diagnostic Audit (`T4.07`)
  - Clean Boot Diagnostic Mode (`T4.08`)
  - Restricted ACL Permissions Traversal Resilience (`T4.09`)
  - Release Readiness, Icon Assets, and Packaging Acceptance (`T4.10`)

---

## 3. Authoritative Test Oracles

All expected values are derived from explicit, authoritative sources defined in `tests/harness/TestOracles.ps1`:
- **`PnpUtilOracle`**: Reference parser and command builder for Windows `pnputil.exe` across English, Traditional Chinese, Simplified Chinese, and Japanese.
- **`StorageDirectoryOracle`**: Master list of all 11 required system directories, expected safety levels (`CleanViaSystemTool`, `SafeToPurge`, `EssentialCore`), and authorized actions.
- **`BcdOracle`**: Authoritative commands for BCD Safe Boot modes (`minimal`, `alternateshell`, `network`, `dsrepair`, `normal`) and boot flags (`noguiboot`, `bootlog`, `basevideo`, `sos`, `testsigning`, `nointegritychecks`, `hypervisorlaunchtype`).
- **`PowerCfgOracle`**: Master GUID registry for Windows Processor Power Management:
  - Subgroup GUID: `54533251-82be-4824-96c1-47b60b740d00` (`SUB_PROCESSOR`)
  - Boost Mode: `be337238-0d82-4146-a960-4f3749d470c7`
  - EPP: `36687f9e-e376-49e8-b783-be5e3e3563ab`
  - Autonomous Mode: `8baa4a8a-14fc-482b-bd23-a0f0f71e11e8`
  - Core Parking Min Cores: `0cc5b647-c1df-4637-891a-dec35c318583`
  - Core Parking Max Cores: `ea062031-0e34-4ff1-9b6d-eb1059324028`
  - Heterogeneous Scheduling: `7f24e370-7664-4642-99e3-e605185a0899`
  - System Cooling Policy: `94d3a615-a899-4ac5-ae2b-e4d8f6343d57`
- **`SystemOptimizerOracle`**: Memory management registry paths (`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`) and Dynamic Ticking CLI.
- **`AssetOracle`**: Icon layer sizes (256, 128, 64, 48, 32, 16 at 32 bpp) and PNG asset dimensions.
- **`LocalizationOracle`**: Culture catalog (`zh-TW`, `zh-CN`, `en-US`, `ja-JP`).
- **`ReadmeOracle`**: Required architectural, benchmark, safety, and build sections.

---

## 4. Progressive Testability Mechanics

To support continuous milestone execution across workers without test suites failing prematurely:
1. **Dynamic Capability Discovery**: Tests query the compiled assembly (`DiskMasterWinUI.dll`) via reflection for types and members.
2. **Pending Milestone Handling**: If a test targets a feature scheduled for an upcoming milestone (e.g., M2 Driver details, M3 Storage Analyzer, M4 Safe Boot, M5 README), the test raises a descriptive message:
   `throw "Pending M#: <Feature> not yet implemented"`
3. **Execution Modes**:
   - **Standard Mode (Default)**: Pending milestone tests are reported as `[PEND]`, and do not trigger a non-zero exit code. This allows currently implemented milestones to be verified cleanly.
   - **Strict Mode (`-StrictMode`)**: All `[PEND]` tests are treated as `[FAIL]`. This mode is enforced in **Milestone 6 (Final E2E Acceptance)** when all implementation milestones are complete.

---

## 5. Execution Guide

### Command Line Interface (`tests/Run-E2ETests.ps1`)

```powershell
# Run all 185 tests across all 4 tiers
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1

# Filter by Tier (1 = Feature Coverage, 2 = Boundaries, 3 = Pairwise, 4 = Scenarios)
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -Tier 1
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -Tier 2

# Filter by Feature (e.g. F14 Fluent Icons, F15 Localization Parity)
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -Feature F14
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -Feature F15

# Export JSON execution results
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -OutputPath "test_results.json"

# Strict Mode (all pending milestone tests must pass)
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/Run-E2ETests.ps1 -StrictMode
```

### Exit Codes
- `0`: All executed tests passed (or were pending in non-strict mode).
- `1`: One or more tests failed.
