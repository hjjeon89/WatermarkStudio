@echo off
setlocal DisableDelayedExpansion
set "wm_codepage="
for /f "tokens=2 delims=:" %%C in ('chcp') do set "wm_codepage=%%C"
chcp 65001 >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup.ps1" -Action Install
set "result=%errorlevel%"
if defined wm_codepage chcp %wm_codepage% >nul
if /i not "%~1"=="--no-pause" pause
exit /b %result%
