@echo off
rem Build everything and produce the installer:
rem   1. publish the native messaging host (BrowserGuard)
rem   2. build the browser extension packages
rem   3. pack the signed crx bundled with the installer
rem   4. compile the Inno Setup installer into SetupOutput
rem   5. wrap it in the MSI package, into SetupOutput\msi\<language>
rem
rem NOTE: keep this file ASCII-only. cmd.exe reads .bat in the OEM code page,
rem so non-ASCII comments break parsing on non-English Windows.

setlocal
set "ROOT=%~dp0"
set "SIGNING_KEY=%ROOT%webextensions\pem\edge.pem"
set "MSI_BUILD=%ROOT%BrowserGuardMsiSetup\bin\x64\Release"
set "MSI_OUT=%ROOT%SetupOutput\msi"

rem BrowserGuard.iss hardcodes the extension ID, which only matches a crx signed
rem with the designated key. Check before the long build steps.
if not exist "%SIGNING_KEY%" goto :no_key

echo.
echo === 1/5 Publishing BrowserGuard ===
dotnet publish "%ROOT%BrowserGuard\BrowserGuard.csproj" -p:PublishProfile=FolderProfile --nologo
if errorlevel 1 goto :failed

echo.
echo === 2/5 Building the browser extension ===
call "%ROOT%webextensions\make.bat" all
if errorlevel 1 goto :failed

echo.
echo === 3/5 Packing the crx ===
call "%ROOT%webextensions\make.bat" crx
if errorlevel 1 goto :failed

echo.
echo === 4/5 Compiling the installer ===
call :find_iscc
if not defined ISCC goto :no_iscc
"%ISCC%" "%ROOT%BrowserGuard.iss"
if errorlevel 1 goto :failed

echo.
echo === 5/5 Building the MSI package ===
dotnet build "%ROOT%BrowserGuardMsiSetup\BrowserGuardSetup.sln" -c Release -p:Platform=x64 --nologo
if errorlevel 1 goto :failed
call :collect_msi ja ja-JP
if errorlevel 1 goto :failed

echo.
echo Build completed.
echo   Installer: "%ROOT%SetupOutput"
echo   MSI:       "%MSI_OUT%"
exit /b 0

rem --- helpers ---------------------------------------------------------------

rem Copies one culture's MSI into SetupOutput\msi\<name>. The build leaves the
rem MSIs of earlier versions beside the new one, so the newest is the one taken.
:collect_msi
set "MSI_SRC=%MSI_BUILD%\%~2"
set "MSI_DEST=%MSI_OUT%\%~1"
if not exist "%MSI_SRC%\*.msi" goto :no_msi
if exist "%MSI_DEST%" rd /s /q "%MSI_DEST%"
mkdir "%MSI_DEST%" >nul 2>&1
for /f "delims=" %%F in ('dir /b /o-d "%MSI_SRC%\*.msi"') do (
    copy /y "%MSI_SRC%\%%F" "%MSI_DEST%\" >nul
    if errorlevel 1 goto :failed
    echo     %~1: %%F
    exit /b 0
)
goto :no_msi

:find_iscc
set "ISCC="
where iscc >nul 2>&1 && set "ISCC=iscc"
if defined ISCC exit /b 0
set "PF86=%ProgramFiles(x86)%"
if exist "%PF86%\Inno Setup 6\ISCC.exe" set "ISCC=%PF86%\Inno Setup 6\ISCC.exe"
if defined ISCC exit /b 0
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
exit /b 0

:no_key
echo.
echo Build failed: the extension signing key was not found.
echo   expected: %SIGNING_KEY%
echo.
echo Place the designated release key there and run this script again.
echo Without it the crx would get a random extension ID, which would not
echo match the ID registered by the installer policy.
exit /b 1

:no_iscc
echo.
echo Build failed: could not find the Inno Setup compiler (ISCC.exe).
echo Install Inno Setup 6, or add ISCC.exe to PATH.
exit /b 1

:no_msi
echo.
echo Build failed: the MSI build produced nothing in "%MSI_SRC%".
exit /b 1

:failed
echo.
echo Build failed.
exit /b 1
