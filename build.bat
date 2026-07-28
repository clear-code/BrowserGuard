@echo off
rem Build everything and produce the installer:
rem   1. publish the native messaging host (BrowserGuard)
rem   2. build the browser extension packages
rem   3. compile the Inno Setup installer into SetupOutput
rem
rem NOTE: keep this file ASCII-only. cmd.exe reads .bat in the OEM code page,
rem so non-ASCII comments break parsing on non-English Windows.

setlocal
set "ROOT=%~dp0"

echo.
echo === 1/3 Publishing BrowserGuard ===
dotnet publish "%ROOT%BrowserGuard\BrowserGuard.csproj" -p:PublishProfile=FolderProfile --nologo
if errorlevel 1 goto :failed

echo.
echo === 2/3 Building the browser extension ===
call "%ROOT%webextensions\build.bat" all
if errorlevel 1 goto :failed

echo.
echo === 3/3 Compiling the installer ===
call :find_iscc
if not defined ISCC goto :no_iscc
"%ISCC%" "%ROOT%BrowserGuard.iss"
if errorlevel 1 goto :failed

echo.
echo Build completed. Installer is in "%ROOT%SetupOutput".
exit /b 0

rem --- helpers ---------------------------------------------------------------

:find_iscc
set "ISCC="
where iscc >nul 2>&1 && set "ISCC=iscc"
if defined ISCC exit /b 0
set "PF86=%ProgramFiles(x86)%"
if exist "%PF86%\Inno Setup 6\ISCC.exe" set "ISCC=%PF86%\Inno Setup 6\ISCC.exe"
if defined ISCC exit /b 0
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
exit /b 0

:no_iscc
echo.
echo Build failed: could not find the Inno Setup compiler (ISCC.exe).
echo Install Inno Setup 6, or add ISCC.exe to PATH.
exit /b 1

:failed
echo.
echo Build failed.
exit /b 1
