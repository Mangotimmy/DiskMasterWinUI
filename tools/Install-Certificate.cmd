@echo off
setlocal enabledelayedexpansion

set "IS_SILENT=0"
if /i "%~1"=="/S" set "IS_SILENT=1"
if /i "%~1"=="/SILENT" set "IS_SILENT=1"
if /i "%~1"=="/QUIET" set "IS_SILENT=1"
if /i "%~1"=="-Silent" set "IS_SILENT=1"
if /i "%~1"=="-Quiet" set "IS_SILENT=1"

if "%IS_SILENT%"=="0" (
    echo =============================================================
    echo    DiskMaster Pro Publisher Certificate Trust Installer
    echo =============================================================
    echo.
)

:: Locate Certificate File
set "CERT_FILE="
if exist "%~dp0DiskMaster_Certificate.cer" set "CERT_FILE=%~dp0DiskMaster_Certificate.cer"
if not defined CERT_FILE if exist "%~dp0installer_output\DiskMaster_Certificate.cer" set "CERT_FILE=%~dp0installer_output\DiskMaster_Certificate.cer"
if not defined CERT_FILE if exist "%~dp0..\installer_output\DiskMaster_Certificate.cer" set "CERT_FILE=%~dp0..\installer_output\DiskMaster_Certificate.cer"
if not defined CERT_FILE if exist "%~dp0DiskMaster_Certificate_win-x64.cer" set "CERT_FILE=%~dp0DiskMaster_Certificate_win-x64.cer"
if not defined CERT_FILE if exist "%~dp0DiskMaster_Certificate_win-arm64.cer" set "CERT_FILE=%~dp0DiskMaster_Certificate_win-arm64.cer"

if not defined CERT_FILE (
    echo [ERROR] Certificate file DiskMaster_Certificate.cer not found!
    if "%IS_SILENT%"=="0" pause
    exit /b 1
)

if "%IS_SILENT%"=="0" echo Found certificate: %CERT_FILE%

:: Install to CurrentUser (no elevation required) and LocalMachine (if elevated)
if "%IS_SILENT%"=="0" echo Registering in CurrentUser Certificate Stores...
certutil -user -addstore -f "TrustedPublisher" "%CERT_FILE%" >nul 2>&1
certutil -user -addstore -f "Root" "%CERT_FILE%" >nul 2>&1

:: Attempt LocalMachine registration (requires admin, will quietly ignore if standard user)
certutil -addstore -f "TrustedPublisher" "%CERT_FILE%" >nul 2>&1
certutil -addstore -f "Root" "%CERT_FILE%" >nul 2>&1

if "%IS_SILENT%"=="0" (
    echo.
    echo [SUCCESS] DiskMaster Pro Certificate has been trusted!
    echo Windows SmartScreen and Unknown Publisher warnings are now resolved.
    echo.
    pause
)

exit /b 0
