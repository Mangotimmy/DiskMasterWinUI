# Regression: SystemOptimizer page froze then exited silently (exit code 0xC00000FD = stack overflow).
# Root cause: ExplorerWatchdog_Toggled flipped the VM state; the OneWay-bound ToggleSwitch.IsOn re-raised
# Toggled synchronously -> infinite recursion. Stack overflow is uncatchable, so no error dialog appeared.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

Write-Host "`n[START] Optimizer page stack-overflow regression..." -ForegroundColor Cyan

# Test 1 (static): handler must be value-driven / idempotent, not a flip.
$page = [System.IO.File]::ReadAllText((Join-Path $root "Pages\SystemOptimizerPage.xaml.cs"))
$m = [regex]::Match($page, 'private void ExplorerWatchdog_Toggled\(.*?\r?\n    \}', 'Singleline')
if (-not $m.Success) { throw "ExplorerWatchdog_Toggled not found" }
if ($m.Value -notmatch 'ts\.IsOn\s*==\s*ViewModel\.IsExplorerWatchdogEnabled') { throw "Handler must ignore binding-driven Toggled (IsOn == VM state)" }
if ($m.Value -match 'ToggleExplorerWatchdogCommand') { throw "Handler must not use the flip-style command" }
Write-Host "  -> PASS: Toggled handler is idempotent." -ForegroundColor Green

$vm = [System.IO.File]::ReadAllText((Join-Path $root "ViewModels\SystemOptimizerViewModel.cs"))
if ($vm -notmatch '_isApplyingWatchdogChange') { throw "VM must keep re-entrancy guard" }
Write-Host "  -> PASS: VM re-entrancy guard present." -ForegroundColor Green

# Test 2 (runtime): open the page directly and make sure the process survives.
$exe = Join-Path $root "publish\DiskMasterWinUI.exe"
if (-not (Test-Path $exe)) { Write-Host "  -> SKIP runtime check (publish exe missing)" -ForegroundColor Yellow; return }
Get-Process DiskMasterWinUI -ErrorAction SilentlyContinue | Stop-Process -Force
$err = Join-Path $env:TEMP "dm_overflow_err.txt"
Remove-Item $err -ErrorAction SilentlyContinue
$p = Start-Process $exe -ArgumentList "--tab","SystemOptimizer" -RedirectStandardError $err -PassThru
$exited = $p.WaitForExit(25000)
if ($exited) {
    $code = "0x{0:X}" -f $p.ExitCode
    $tail = if (Test-Path $err) { (Get-Content $err -TotalCount 12) -join "`n" } else { "" }
    throw "App exited within 25s on SystemOptimizer (exit $code)`n$tail"
}
Stop-Process -Id $p.Id -Force
Write-Host "  -> PASS: SystemOptimizer page stayed alive for 25s." -ForegroundColor Green

Write-Host "`n[COMPLETED] Optimizer stack-overflow regression PASSED." -ForegroundColor Green
