@echo off
setlocal
echo ===================================================
echo   DiskMaster Pro Publisher Certificate Installer
echo ===================================================
echo.
set "CERT_FILE=%~dp0DiskMaster_Certificate.cer"
if not exist "%CERT_FILE%" (
    set "CERT_FILE=%~dp0installer_output\DiskMaster_Certificate.cer"
)
if not exist "%CERT_FILE%" (
    echo [ERROR] Certificate file DiskMaster_Certificate.cer not found!
    pause
    exit /b 1
)

echo Importing DiskMaster Team certificate to Trusted Root and Trusted Publisher...
certutil -addstore -f "Root" "%CERT_FILE%"
certutil -addstore -f "TrustedPublisher" "%CERT_FILE%"

echo.
echo [SUCCESS] DiskMaster Pro certificate installed successfully!
echo Windows SmartScreen and Unknown Publisher warnings have been eliminated.
echo.
pause
