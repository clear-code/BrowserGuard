@echo off
rem Removes the Inno Setup package this MSI installed.
rem
rem BrowserGuard.iss sets no AppId, so Inno Setup derives the uninstall key from
rem AppName. Both registry views are tried: the install is 64 bit today, but a
rem 32 bit build would put the key under WOW6432Node instead.

setlocal enabledelayedexpansion

set "UNINSTALL_KEY=HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BrowserGuard_is1"

call :uninstall "%UNINSTALL_KEY%" /reg:64
call :uninstall "%UNINSTALL_KEY%" /reg:32
exit /b 0

:uninstall
reg query "%~1" /v UninstallString %2 >nul 2>&1
if not %errorlevel% equ 0 exit /b 0
for /f "tokens=2*" %%A in ('reg query "%~1" /v UninstallString %2') do set "UNINSTALL_PATH=%%B"
if not defined UNINSTALL_PATH exit /b 0
call !UNINSTALL_PATH! /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UninstallMsiPackage=no
set "UNINSTALL_PATH="
exit /b 0
