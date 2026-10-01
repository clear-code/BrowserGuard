@echo off
rem This Source Code Form is subject to the terms of the Mozilla Public
rem License, v. 2.0. If a copy of the MPL was not distributed with this
rem file, You can obtain one at https://mozilla.org/MPL/2.0/.
rem
rem Copyright (c) 2026 ClearCode Inc.

rem Build script for the BrowserGuard browser extension (Windows).
rem The actual logic lives in make.ps1. Run "make.bat help" for usage.
rem NOTE: keep this file ASCII-only. cmd.exe reads .bat in the OEM code page,
rem so non-ASCII comments break parsing on non-English Windows.

setlocal
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%make.ps1"

rem Prefer PowerShell 7 (pwsh), fall back to Windows PowerShell.
set "PS_EXE=powershell"
where pwsh >nul 2>&1
if not errorlevel 1 set "PS_EXE=pwsh"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
exit /b %errorlevel%
