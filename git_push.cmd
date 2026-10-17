@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

echo [DiskMaster Pro] Starting automated version bump and Git push...

where pwsh >nul 2>&1
if %ERRORLEVEL% equ 0 (
    set "PS_EXE=pwsh"
) else (
    set "PS_EXE=powershell"
)

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%git_push.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

if "%~1"=="" (
    echo.
    echo Press any key to exit...
    pause >nul
)

exit /b %EXITCODE%
