@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -Bootstrap %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" echo Build stopped. Read the error above; no successful build is implied.
pause
exit /b %result%
