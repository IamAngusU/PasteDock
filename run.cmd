@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" -Bootstrap %*
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
