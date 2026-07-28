@echo off
rem Build script for the BrowserGuard browser extension (Windows).
rem The actual logic lives in build.ps1. Run "build.bat help" for usage.
rem NOTE: keep this file ASCII-only. cmd.exe reads .bat in the OEM code page,
rem so non-ASCII comments break parsing on non-English Windows.

setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%build.ps1"

rem Prefer PowerShell 7 (pwsh), fall back to Windows PowerShell.
set "PS_EXE=powershell"
where pwsh >nul 2>&1
if not errorlevel 1 set "PS_EXE=pwsh"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
exit /b %errorlevel%
