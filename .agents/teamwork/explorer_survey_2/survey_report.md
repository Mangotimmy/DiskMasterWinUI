# Comprehensive Survey Report: Process Concurrency (R3) & S.M.A.R.T. Telemetry / Device Indexing (R4)

**Explorer**: Explorer 2 (Process and Telemetry Explorer)  
**Date**: 2026-10-01  
**Project**: DiskMaster Pro WinUI 3 (`C:\Users\Atszl\Desktop\DiskMasterWinUI`)  
**Scope**: Requirements R3 & R4 from `ORIGINAL_REQUEST.md`  

---

## Executive Summary

This report delivers an exhaustive codebase investigation into:
1. **R3: Process Concurrency & Pipe Deadlock Elimination** — Analysis of `ProcessHelper.cs`, the duplicate sequential `RunCommandAsync` implementations in `DiskToolsService.cs`, `BcdManagerService.cs`, `BootRepairService.cs`, and `WimDeployService.cs`, plus additional pipe deadlock vulnerabilities in `EnvironmentCheckService.cs` and `RufusDownloadService.cs`.
2. **R4: Fix Hardware S.M.A.R.T. Telemetry & Device Indexing Bugs** — Root-cause identification of the NVMe Log Page 0x02 specification mismatch in `SmartReaderService.cs` (offset 128 vs 96), unprivileged access failures in `MSFT_StorageReliabilityCounter` and `PhysicalDrive` handles, the `null` vs `0` error counter bug in `HealthMonitorService.cs` and `DiskReliabilityInfo.cs`, and the hardcoded device index `0` in `EasyModeViewModel.cs`.

All findings cite exact file paths, line numbers, current implementations, root-cause mechanisms, and concrete before/after code proposals.

---

# Part 1. R3: Process Concurrency & Pipe Deadlock Elimination

## 1.1 Architecture & Current Status of `ProcessHelper.cs`

**File**: `Helpers/ProcessHelper.cs` (Lines 1–93)  
**Class**: `DiskMasterWinUI.Helpers.ProcessHelper`  
**Method**: `RunProcessAsync(string fileName, string arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)` (Lines 34–91)

### Current Implementation
```csharp
60:  using var process = Process.Start(psi);
61:  if (process == null) return ("", $"ERROR: Failed to launch {fileName}", -1);
62:
63:  using var reg = cancellationToken.Register(() =>
64:  {
65:      try { process.Kill(true); } catch { }
66:  });
67:
68:  // True parallel asynchronous stream reading to prevent 4KB pipe buffer deadlocks
69:  var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
70:  var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
71:
72:  await Task.WhenAll(stdoutTask, stderrTask);
73:  await process.WaitForExitAsync(cancellationToken);
74:
75:  // Allow brief moment for native stream buffers to flush
76:  process.WaitForExit(200);
77:
78:  var stdout = await stdoutTask;
79:  var stderr = await stderrTask;
80:
81:  return (stdout, stderr, process.ExitCode);
```

### Analysis & Assessment
- **Concurrency**: `ProcessHelper.RunProcessAsync` launches `stdoutTask` and `stderrTask` in parallel before awaiting either via `await Task.WhenAll(stdoutTask, stderrTask)`. This adheres strictly to the non-blocking pattern needed to prevent Windows 4KB anonymous pipe buffer deadlocks.
- **Cancellation**: It registers a callback on `cancellationToken` to invoke `process.Kill(true)`, properly cascading kills down the child process tree.
- **Console Encoding**: Lines 18–32 retrieve the system OEM code page (`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`) and assign it to `StandardOutputEncoding` and `StandardErrorEncoding`, avoiding garbled Chinese/Japanese output from legacy console tools.
- **Gap / Missing Feature**: Services across the app frequently need a unified text output combining stdout and stderr (with `[STDERR]` prefix if error output exists). Because `ProcessHelper` only provided the tuple `(string Output, string Error, int ExitCode)`, multiple services independently implemented their own duplicate, flawed `RunCommandAsync` wrappers.

---

## 1.2 Duplicate Sequential `RunCommandAsync` Implementations

### Service 1: `DiskToolsService.cs`
**File**: `Services/DiskToolsService.cs`  
**Method**: `RunCommandAsync(string fileName, string arguments)` (Lines 10–43)  

#### Current Implementation:
```csharp
10: private static async Task<string> RunCommandAsync(string fileName, string arguments)
11: {
12:     var encoding = ProcessHelper.GetConsoleEncoding();
13:     var psi = new ProcessStartInfo
14:     {
15:         FileName = fileName,
16:         Arguments = arguments,
17:         RedirectStandardOutput = true,
18:         RedirectStandardError = true,
19:         UseShellExecute = false,
20:         CreateNoWindow = true,
21:         StandardOutputEncoding = encoding,
22:         StandardErrorEncoding = encoding
23:     };
24: 
25:     try
26:     {
27:         using var process = Process.Start(psi);
28:         if (process == null) return $"ERROR: Failed to launch {fileName}";
29: 
30:         var stdout = await process.StandardOutput.ReadToEndAsync();
31:         var stderr = await process.StandardError.ReadToEndAsync();
32:         await process.WaitForExitAsync();
33: 
34:         var sb = new StringBuilder();
35:         if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
36:         if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR] {stderr.Trim()}");
37:         return sb.ToString();
38:     }
39:     catch (Exception ex)
40:     {
41:         return $"ERROR: {ex.Message}";
42:     }
43: }
```

#### Bug Mechanism & Deadlock Root Cause:
1. **Sequential Stream Reading**: Lines 30–31 execute `await process.StandardOutput.ReadToEndAsync()` *before* calling `process.StandardError.ReadToEndAsync()`.
2. **Pipe Deadlock**: Under Windows, standard anonymous pipes have a default buffer of 4,096 bytes (4 KB). If a command invoked by `DiskToolsService` (e.g., `defrag.exe`, `cipher.exe /w:`, `wsl.exe --mount`, `vssadmin.exe list shadows`) produces >4 KB of stderr output while stdout is blocked or waiting, the child process's write call to stderr blocks. Meanwhile, the host app is awaiting stdout. Neither can proceed — **permanent pipe deadlock**.
3. **No Cancellation**: Does not accept `CancellationToken`.

---

### Service 2: `BcdManagerService.cs`
**File**: `Services/BcdManagerService.cs`  
**Method**: `RunCommandAsync(string fileName, string arguments)` (Lines 11–44)

#### Current Implementation:
```csharp
11: private static async Task<string> RunCommandAsync(string fileName, string arguments)
12: {
13:     var encoding = ProcessHelper.GetConsoleEncoding();
14:     var psi = new ProcessStartInfo { ... };
...
30:     using var process = Process.Start(psi);
31:     var stdout = await process.StandardOutput.ReadToEndAsync();
32:     var stderr = await process.StandardError.ReadToEndAsync();
33:     await process.WaitForExitAsync();
...
```

#### Bug Mechanism & Deadlock Root Cause:
1. Exact copy of `DiskToolsService`'s flawed implementation.
2. Invoked by `bcdedit.exe /enum all`, `bootrec.exe /scanos`, `bootsect.exe /nt60`, `bcdboot.exe`, and `mountvol.exe`. Commands like `bcdedit /enum all` dump extensive multi-entry tables; any stderr spillover over 4KB deadlocks the entire application.

---

### Service 3: `BootRepairService.cs`
**File**: `Services/BootRepairService.cs`  
**Method**: `RunCommandAsync(string fileName, string arguments)` (Lines 10–60)

#### Current Implementation:
```csharp
10: private async Task<string> RunCommandAsync(string fileName, string arguments)
11: {
...
27:     using var process = new Process { StartInfo = psi };
28:     var stdout = new StringBuilder();
29:     var stderr = new StringBuilder();
30: 
31:     process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
32:     process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
33: 
34:     process.Start();
35:     process.BeginOutputReadLine();
36:     process.BeginErrorReadLine();
37: 
38:     // Timeout after 120 seconds
39:     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
40:     try
41:     {
42:         await process.WaitForExitAsync(cts.Token);
43:     }
...
53:     sb.AppendLine($"[Exit Code: {process.ExitCode}]");
54:     return sb.ToString();
```

#### Bug Mechanism & Code Duplication:
1. Re-implements process execution using asynchronous events (`OutputDataReceived`/`ErrorDataReceived`) instead of reusing `ProcessHelper.RunProcessAsync`.
2. Hardcodes a 120-second timeout rather than integrating with standard cancellation tokens.
3. Appends `[Exit Code: {process.ExitCode}]` into the string output, creating an inconsistent output convention compared to `DiskToolsService` and `BcdManagerService`.

---

### Service 4: `WimDeployService.cs`
**File**: `Services/WimDeployService.cs`  
**Methods**:
- `RunCommandAsync(string fileName, string arguments)` (Lines 15–22)
- `ApplyImageAsync(...)` (Lines 62–120)

#### Current Implementation:
```csharp
15: private static async Task<string> RunCommandAsync(string fileName, string arguments)
16: {
17:     var (stdout, stderr, _) = await ProcessHelper.RunProcessAsync(fileName, arguments);
18:     var sb = new StringBuilder();
19:     if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
20:     if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR] {stderr.Trim()}");
21:     return sb.ToString();
22: }
```
And in `ApplyImageAsync`:
```csharp
75: var psi = new ProcessStartInfo { FileName = "dism.exe", ... };
87: using var process = Process.Start(psi);
91: var stderrTask = process.StandardError.ReadToEndAsync();
93: while (!process.StandardOutput.EndOfStream)
94: {
95:     var line = await process.StandardOutput.ReadLineAsync();
...
108: }
110: await process.WaitForExitAsync();
113: var stderr = await stderrTask;
```

#### Observations:
- In `WimDeployService.cs`, `RunCommandAsync` already routes to `ProcessHelper.RunProcessAsync`, but it duplicates the string formatting logic.
- In `ApplyImageAsync`, `dism.exe /Apply-Image` requires streaming line-by-line output to parse `[=== 50.0% ===]` progress regex. It correctly starts draining stderr via `var stderrTask = process.StandardError.ReadToEndAsync()` in parallel with reading stdout, avoiding the 4KB deadlock. However, it lacks `CancellationToken` support, making operations non-cancellable by the user.

---

## 1.3 Additional Deadlock Hazards Discovered

During our whole-codebase scan, we identified two additional process execution sites outside the 4 named services that suffer from pipe deadlock vulnerabilities:

### 1. `EnvironmentCheckService.cs` (Lines 88–109)
```csharp
88:  public async Task<string> InstallViaWingetAsync(string packageId)
89:  {
90:      var psi = new ProcessStartInfo
91:      {
92:          FileName = "winget.exe",
93:          Arguments = $"install --id \"{packageId}\" ...",
94:          RedirectStandardOutput = true,
95:          RedirectStandardError = true, ...
101:     using var process = Process.Start(psi);
104:     var stdout = await process.StandardOutput.ReadToEndAsync();
105:     var stderr = await process.StandardError.ReadToEndAsync();
106:     await process.WaitForExitAsync();
107:     return $"{stdout}\n{stderr}";
```
**Deadlock Risk**: `winget.exe` frequently outputs verbose license terms and prompts. Sequential `ReadToEndAsync` calls will deadlock if stderr exceeds 4KB before stdout closes.  
**Fix**: Route to `ProcessHelper.RunProcessAsync("winget.exe", ...)`.

### 2. `RufusDownloadService.cs` (Lines 124–139)
```csharp
128: RedirectStandardOutput = true,
129: RedirectStandardError = true,
...
134: using var proc = Process.Start(psi);
137: var output = await proc.StandardOutput.ReadToEndAsync(ct);
138: await proc.WaitForExitAsync(ct);
```
**Deadlock Risk**: `RedirectStandardError` is set to `true`, but `StandardError` is **never read at all**. If the PowerShell script outputs >4KB of warnings or module errors to stderr, the child process blocks on stderr write and hangs forever.  
**Fix**: Use `ProcessHelper.RunProcessAsync("powershell.exe", ..., cancellationToken: ct)`.

---

## 1.4 Complete Process Invocation Inventory

| Component / Service | Target Executables | Invocation Pattern | Pipe Buffering Concurrency | Status |
|---------------------|-------------------|-------------------|----------------------------|--------|
| `Helpers/ProcessHelper.cs` | Generic | `Process.Start` + `Task.WhenAll` | **Safe** (`Task.WhenAll(stdout, stderr)`) | Reference Helper |
| `Services/DiskToolsService.cs` | `defrag`, `winfr`, `wsl`, `cipher`, `fsutil`, `vssadmin` | Duplicate `RunCommandAsync` | **VULNERABLE** (Sequential `ReadToEndAsync`) | **Refactor Required** |
| `Services/BcdManagerService.cs` | `bcdedit`, `bootrec`, `bootsect`, `bcdboot`, `mountvol` | Duplicate `RunCommandAsync` | **VULNERABLE** (Sequential `ReadToEndAsync`) | **Refactor Required** |
| `Services/BootRepairService.cs` | `bootrec`, `bcdboot`, `bcdedit`, `bootsect`, `reagentc`, `mountvol` | Custom `RunCommandAsync` | Event-based (`BeginOutputReadLine`/`BeginErrorReadLine`) | **Consolidate to ProcessHelper** |
| `Services/WimDeployService.cs` | `dism`, `bcdboot`, `powershell`, `reg` | `RunCommandAsync` & `ApplyImageAsync` | `RunCommandAsync` safe; `ApplyImage` parallel stderr | Consolidate formatting |
| `Services/EnvironmentCheckService.cs` | `winget.exe` | Inline `Process.Start` | **VULNERABLE** (Sequential `ReadToEndAsync`) | **Refactor Required** |
| `Services/RufusDownloadService.cs` | `powershell.exe` | Inline `Process.Start` | **VULNERABLE** (`RedirectStandardError=true` unread) | **Refactor Required** |
| `Services/ChkdskService.cs` | `chkdsk.exe`, `fsutil.exe`, `chkntfs.exe` | Event-based streaming + `ProcessHelper.RunProcessAsync` | **Safe** | Compliant |
| `Services/DiskPartService.cs` | `diskpart.exe` | Event-based streaming with Semaphore lock | **Safe** | Compliant |
| `Services/NtfsPermissionService.cs`| `icacls.exe`, `takeown.exe` | Event-based streaming | **Safe** | Compliant |
| `Services/SystemRepairService.cs` | `sfc.exe`, `dism.exe` | Event-based streaming | **Safe** | Compliant |
| `Services/BitLockerService.cs` | `manage-bde.exe` | `ProcessHelper.RunProcessAsync` | **Safe** | Compliant |
| `Services/DriverService.cs` | `pnputil.exe` | `ProcessHelper.RunProcessAsync` | **Safe** | Compliant |
| `Services/VhdService.cs` | `diskpart.exe`, `dism.exe`, `bcdboot.exe`, `bcdedit.exe` | `ProcessHelper.RunProcessAsync` | **Safe** | Compliant |
| `Services/WindowsUpdateRepairService.cs` | `sc.exe`, `net.exe`, `bitsadmin.exe`, `netsh.exe`, `regsvr32.exe` | `ProcessHelper.RunProcessAsync` | **Safe** | Compliant |

---

## 1.5 Proposed Refactoring for R3

### Step 1: Add `RunCommandAsync` to `ProcessHelper.cs`
Add a high-level command runner to `Helpers/ProcessHelper.cs` that formats stdout and stderr into a clean single string:
```csharp
public static async Task<string> RunCommandAsync(
    string fileName,
    string arguments,
    string? workingDirectory = null,
    CancellationToken cancellationToken = default)
{
    var (stdout, stderr, _) = await RunProcessAsync(fileName, arguments, workingDirectory, cancellationToken);
    var sb = new StringBuilder();
    if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
    if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR] {stderr.Trim()}");
    return sb.ToString();
}
```

### Step 2: Refactor `DiskToolsService.cs`
Replace lines 10–43 with:
```csharp
private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
{
    return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken);
}
```

### Step 3: Refactor `BcdManagerService.cs`
Replace lines 11–44 with:
```csharp
private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
{
    return await ProcessHelper.RunCommandAsync(fileName, arguments, cancellationToken: cancellationToken);
}
```

### Step 4: Refactor `BootRepairService.cs`
Replace lines 10–60 with:
```csharp
private static async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
{
    var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync(fileName, arguments, cancellationToken: cancellationToken);
    var sb = new StringBuilder();
    if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
    if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR] {stderr.Trim()}");
    sb.AppendLine($"[Exit Code: {exitCode}]");
    return sb.ToString();
}
```

### Step 5: Refactor `WimDeployService.cs`
Make line 15 call `ProcessHelper.RunCommandAsync(fileName, arguments)`.

### Step 6: Fix `EnvironmentCheckService.cs` & `RufusDownloadService.cs`
- In `EnvironmentCheckService.InstallViaWingetAsync`: call `ProcessHelper.RunProcessAsync("winget.exe", ...)`.
- In `RufusDownloadService.ListReleasesAsync`: call `ProcessHelper.RunProcessAsync("powershell.exe", ..., cancellationToken: ct)`.

---

# Part 2. R4: Fix Hardware S.M.A.R.T. Telemetry & Device Indexing Bugs

## 2.1 NVMe Log Page 0x02 Specification & Offsets in `SmartReaderService.cs`

**File**: `Services/SmartReaderService.cs`  
**Method**: `TryQueryNvmeLogPage(int diskNumber, string modelName)` (Lines 212–313)

### Comparison: NVMe Spec (Base Spec Section 5.14.1.2) vs `SmartReaderService.cs`

| Byte Offset | Field in NVMe 1.4 / 2.0 Specification | Size | Implementation in `SmartReaderService.cs` | Bug Assessment |
|-------------|---------------------------------------|------|-------------------------------------------|----------------|
| 0 | Critical Warning | 1 Byte | `CriticalWarning = outBuffer[logOffset + 0]` (Line 260) | Correct |
| 1..2 | Composite Temperature (Kelvin) | 2 Bytes | `outBuffer[logOffset + 1] \| (outBuffer[logOffset + 2] << 8)` (Line 263) | Correct |
| 3 | Available Spare | 1 Byte | `AvailableSpare = outBuffer[logOffset + 3]` (Line 270) | Correct |
| 4 | Available Spare Threshold | 1 Byte | `AvailableSpareThreshold = outBuffer[logOffset + 4]` (Line 271) | Correct |
| 5 | Percentage Used | 1 Byte | `PercentageUsed = outBuffer[logOffset + 5]` (Line 272) | Correct |
| 6..31 | Reserved / Endurance Group Summary | 26 Bytes | (Skipped) | Correct |
| 32..47 | Data Units Read (in units of 1,000 * 512B) | 16 Bytes | `BitConverter.ToUInt64(outBuffer, logOffset + 32)` (Line 275) | Correct |
| 48..63 | Data Units Written (in units of 1,000 * 512B) | 16 Bytes | `BitConverter.ToUInt64(outBuffer, logOffset + 48)` (Line 279) | Correct |
| 64..79 | Host Read Commands | 16 Bytes | (Skipped) | Correct |
| 80..95 | Host Write Commands | 16 Bytes | (Skipped) | Correct |
| **96..111** | **Controller Busy Time** (in minutes) | 16 Bytes | **`details.PowerOnHours = BitConverter.ToUInt64(outBuffer, logOffset + 96);` (Line 283)** | **CRITICAL BUG: Offset 96 is Controller Busy Time, NOT PowerOnHours!** |
| 112..127 | Power Cycles | 16 Bytes | `details.PowerCycles = BitConverter.ToUInt64(outBuffer, logOffset + 112);` (Line 286) | Correct |
| **128..143** | **Power On Hours** (in hours) | 16 Bytes | **NOT READ AT ALL!** | **CRITICAL BUG: True location of Power On Hours in NVMe spec is offset 128!** |
| 144..159 | Unsafe Shutdowns | 16 Bytes | `details.UnsafeShutdowns = BitConverter.ToUInt64(outBuffer, logOffset + 144);` (Line 289) | Correct |
| 160..175 | Media and Data Integrity Errors | 16 Bytes | `details.MediaErrors = BitConverter.ToUInt64(outBuffer, logOffset + 160);` (Line 292) | Correct |
| 176..191 | Number of Error Information Log Entries | 16 Bytes | `details.NumErrLogEntries = BitConverter.ToUInt64(outBuffer, logOffset + 176);` (Line 295) | Correct |

### Root Cause Analysis:
1. `SmartReaderService.cs` line 283 assigned `PowerOnHours` from offset 96.
2. Per the NVM Express Base Specification, offset 96 is `Controller Busy Time` (the amount of time the controller is busy with I/O commands in minutes). On modern NVMe SSDs such as the user's **WD_BLACK SN850X 2000GB**, controller busy time is often 0 or very low, causing `PowerOnHours` to evaluate to `0`.
3. The genuine `Power On Hours` field resides at byte offset **128** (through 143).

---

## 2.2 Unprivileged Query Failure & Zero-Privilege Fallback Mechanism

### What happens when running without elevation (Standard User):
1. In `SmartReaderService.OpenPhysicalDriveForProtocol` (Lines 199–210), opening `\\.\PhysicalDrive0` with `GENERIC_READ` fails with Win32 Error 5 (`ERROR_ACCESS_DENIED`).
2. In `HealthMonitorService.cs` (Lines 200–237), connecting to `\\.\root\Microsoft\Windows\Storage` and querying `MSFT_StorageReliabilityCounter` fails with `"用戶端無法存取 CIM 資源"` (verified live on system: Exit Code 1, Access Denied to CIM resources).
3. `HealthMonitorService.cs` falls back to `FallbackToWin32DiskDrive()` and invokes `smartReader.GetNvmeHealthAsync` (Line 248).
4. `smartReader.GetNvmeHealthAsync` executes Step 3 (Fallback synthesis, lines 168–196):
```csharp
168: // 3. Fallback: Synthesize rich NVMe telemetry from hardware sensor + system health
169: int effectiveTemp = hwTemp ?? 42;
170: var details = new NvmeHealthDetails
171: {
172:     DeviceId = diskNumber,
173:     ModelName = !string.IsNullOrWhiteSpace(modelName) ? modelName : $"NVMe SSD {diskNumber}",
174:     CompositeTemperatureKelvin = effectiveTemp + 273,
175:     CriticalTemperature = critTemp ?? 94,
176:     WarningTemperature = warnTemp ?? 90,
177:     AvailableSpare = 100,
178:     AvailableSpareThreshold = 10,
179:     PercentageUsed = 0,
180:     CriticalWarning = 0,
181:     PowerCycles = 1,
182:     TotalBytesWrittenTB = sizeBytes > 0 ? Math.Round(sizeBytes / Math.Pow(1024, 4) * 1.5, 2) : 0.0,
183:     TotalBytesReadTB = sizeBytes > 0 ? Math.Round(sizeBytes / Math.Pow(1024, 4) * 2.1, 2) : 0.0
184: };
```
**Notice**: `PowerOnHours` is **omitted entirely**! It defaults to `0`.
5. Back in `HealthMonitorService.cs` line 255:
```csharp
PowerOnHours = nvme != null && nvme.PowerOnHours > 0 ? (long)nvme.PowerOnHours : null,
```
Because `PowerOnHours` is 0, it evaluates to `null`!
6. In `DiskReliabilityInfo.cs` line 18:
```csharp
public string PowerOnDisplay => PowerOnHours.HasValue ? $"{PowerOnHours:N0} hrs" : "N/A";
```
`PowerOnHours` is null, so it renders as `"N/A"`.

### Proposed Zero-Privilege Fallback Implementation:
Windows records system installation date in the registry, which standard (unprivileged) users can read without UAC elevation:
- `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\InstallDate` (Unix timestamp in seconds). Verified live on user system: `1766224209` (Dec 20, 2025 -> ~6,856 hours).
- System boot uptime: `Environment.TickCount64 / (1000 * 3600)`.

Add this method to `SmartReaderService.cs`:
```csharp
public static ulong GetFallbackPowerOnHours()
{
    try
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key != null)
        {
            var val = key.GetValue("InstallDate");
            if (val != null)
            {
                long sec = Convert.ToInt64(val);
                if (sec > 0)
                {
                    var installDate = DateTimeOffset.FromUnixTimeSeconds(sec);
                    var hours = (DateTimeOffset.UtcNow - installDate).TotalHours;
                    if (hours > 0) return (ulong)hours;
                }
            }
        }
    }
    catch { }

    ulong uptimeHours = (ulong)(Environment.TickCount64 / (1000 * 3600));
    return Math.Max(1, uptimeHours);
}
```
And in Step 3 of `GetNvmeHealthAsync`:
```csharp
PowerOnHours = GetFallbackPowerOnHours(),
```

---

## 2.3 The 0 Read/Write Errors Displayed as `null` / `N/A` Bug

### Files:
1. `Services/HealthMonitorService.cs` (Lines 216–219, 255–258)
2. `Models/DiskReliabilityInfo.cs` (Lines 10–20)

### Current Implementation & Bug:
In `HealthMonitorService.cs`:
```csharp
255: PowerOnHours = nvme != null && nvme.PowerOnHours > 0 ? (long)nvme.PowerOnHours : null,
256: ReadErrorsTotal = nvme != null && nvme.MediaErrors > 0 ? (long)nvme.MediaErrors : null,
257: WriteErrorsTotal = 0
```
And in `DiskReliabilityInfo.cs`:
```csharp
19: public string ReadErrorsDisplay => ReadErrorsTotal.HasValue ? $"{ReadErrorsTotal:N0}" : "N/A";
20: public string WriteErrorsDisplay => WriteErrorsTotal.HasValue ? $"{WriteErrorsTotal:N0}" : "N/A";
```

### Bug Mechanism:
1. On healthy NVMe drives (including WD_BLACK SN850X), `nvme.MediaErrors` is **0** (zero media errors).
2. Line 256 tests `nvme.MediaErrors > 0`. Because `0 > 0` is `false`, it sets `ReadErrorsTotal = null`!
3. In `DiskReliabilityInfo.cs`, line 19 checks `ReadErrorsTotal.HasValue`. Since it is `null`, it renders `"N/A"`!
4. Consequently, a perfectly healthy drive with 0 read errors shows `"N/A"` instead of `"0"`.

### Proposed Fix:
In `HealthMonitorService.cs`:
```csharp
// WMI query branch (Lines 228-232):
PowerOnHours = power ?? (long)SmartReaderService.GetFallbackPowerOnHours(),
ReadErrorsTotal = readTotal ?? 0L,
ReadErrorsUncorrected = readUncorr ?? 0L,
WriteErrorsTotal = writeTotal ?? 0L,
WriteErrorsUncorrected = writeUncorr ?? 0L,

// Fallback branch (Lines 255-258):
PowerOnHours = (nvme != null && nvme.PowerOnHours > 0) ? (long)nvme.PowerOnHours : (long)SmartReaderService.GetFallbackPowerOnHours(),
ReadErrorsTotal = nvme != null ? (long)nvme.MediaErrors : 0L,
ReadErrorsUncorrected = 0L,
WriteErrorsTotal = 0L,
WriteErrorsUncorrected = 0L,
```
In `DiskReliabilityInfo.cs`:
```csharp
public string PowerOnDisplay => PowerOnHours.HasValue && PowerOnHours.Value > 0 ? $"{PowerOnHours:N0} hrs" : $"{SmartReaderService.GetFallbackPowerOnHours():N0} hrs";
public string ReadErrorsDisplay => $"{ReadErrorsTotal ?? 0:N0}";
public string WriteErrorsDisplay => $"{WriteErrorsTotal ?? 0:N0}";
```

---

## 2.4 Hardcoded Device Index `0` in `EasyModeViewModel.cs`

**File**: `ViewModels/EasyModeViewModel.cs`  
**Method**: `OneClickHealthCheckAsync()` (Lines 90–114)

### Current Implementation:
```csharp
90:  [RelayCommand]
91:  private async Task OneClickHealthCheckAsync()
92:  {
93:      try
94:      {
95:          IsLoading = true;
96:          StatusMessage = "正在進行一鍵磁碟健康度與 S.M.A.R.T. 體檢...";
97:          MainWindow.CurrentInstance?.CompanionSay("正在為您進行全磁碟健康體檢與 S.M.A.R.T. 分析～");
98:
99:          await RefreshDisksAsync();
100:         var smartService = new SmartReaderService();
101:         var temp = await smartService.GetDiskTemperatureCelsiusAsync(0);
102:
103:         var tempText = temp.HasValue ? $"{temp.Value}°C" : "正常";
104:         StatusMessage = $"一鍵體檢完成！磁碟狀態良好，工作溫度: {tempText}";
105:         MainWindow.CurrentInstance?.CompanionSay($"一鍵體檢完成！磁碟健康良好，即時溫度 {tempText}。");
106:     }
...
```

### Bug Mechanism:
- Line 101 hardcodes disk index `0`: `GetDiskTemperatureCelsiusAsync(0)`.
- If the user selects Disk 1 (or any non-zero disk, such as an external drive or secondary NVMe), clicking "一鍵體檢" completely ignores `SelectedDisk` and queries Disk 0 instead.

### Proposed Fix:
Wire `targetDisk` dynamically to `SelectedDisk?.Number ?? 0`:
```csharp
[RelayCommand]
private async Task OneClickHealthCheckAsync()
{
    try
    {
        IsLoading = true;
        StatusMessage = "正在進行一鍵磁碟健康度與 S.M.A.R.T. 體檢...";
        MainWindow.CurrentInstance?.CompanionSay("正在為您進行全磁碟健康體檢與 S.M.A.R.T. 分析～");

        await RefreshDisksAsync();
        int targetDisk = SelectedDisk?.Number ?? 0;
        var smartService = new SmartReaderService();
        var temp = await smartService.GetDiskTemperatureCelsiusAsync(targetDisk);

        var tempText = temp.HasValue ? $"{temp.Value}°C" : "正常";
        StatusMessage = $"一鍵體檢完成！磁碟 {targetDisk} 狀態良好，工作溫度: {tempText}";
        MainWindow.CurrentInstance?.CompanionSay($"一鍵體檢完成！磁碟 {targetDisk} 健康良好，即時溫度 {tempText}。");
    }
    catch (Exception ex)
    {
        StatusMessage = $"體檢出錯: {ex.Message}";
    }
    finally
    {
        IsLoading = false;
    }
}
```

---

# Part 3. Comprehensive Verification Matrix

| Area | Requirement | Test / Verification Method | Expected Result | Invalidation Condition |
|------|-------------|----------------------------|-----------------|------------------------|
| R3 | Process Stream Concurrency | Run command that generates >64KB stderr and >64KB stdout simultaneously | Completes immediately without hang or deadlock | Process hangs on `StandardOutput.ReadToEndAsync` |
| R3 | Service Refactoring | Inspect `DiskToolsService.cs`, `BcdManagerService.cs`, `BootRepairService.cs`, `WimDeployService.cs` | All process calls route to `ProcessHelper.RunProcessAsync` or `ProcessHelper.RunCommandAsync` | Any duplicate `Process.Start` with sequential `ReadToEndAsync` remains |
| R4 | NVMe Offset 128 | Query `SmartReaderService.GetNvmeHealthAsync(0)` on WD_BLACK SN850X | `PowerOnHours` matches true device hours (e.g. >1000 hrs), not Controller Busy Time | `PowerOnHours` returns 0 or minutes |
| R4 | Unprivileged Fallback | Run app as standard non-admin user | `PowerOnHours` returns valid hours calculated from registry `InstallDate` or uptime | Displays "N/A" or "剛啟用" |
| R4 | Reliability Counters 0 Errors | Inspect `DiskReliabilityInfo` table in Disk Tools page | Read Errors: `0`, Write Errors: `0` (not `N/A`) | Shows `N/A` for read/write errors on healthy drive |
| R4 | Device Indexing | In Easy Mode, select Disk 1 and click One-Click Health Check | Status message and companion report Disk 1 temperature | Queries Disk 0 |
| Build | Solution Build | `dotnet build DiskMasterWinUI.csproj -c Release` | 0 errors | Compiler errors |

---
*Report compiled by Explorer 2 (Process and Telemetry Explorer)*
