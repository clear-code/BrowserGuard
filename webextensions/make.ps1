#Requires -Version 5.1
<#
.SYNOPSIS
    Build script for the BrowserGuard browser extension (Windows).
.DESCRIPTION
    A Windows-native replacement for the Makefile targets.
    Normally invoked through make.bat.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'packages', 'deps', 'lint', 'test', 'format', 'clean', 'package', 'crx', 'help')]
    [string]$Target = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$EdgeDir    = Join-Path $Root 'edge'
$NpmBinDir  = Join-Path $Root 'node_modules\.bin'
$DevDir     = Join-Path $EdgeDir 'dev'
$StageDir   = Join-Path $Root '.build'

$ProdZip = Join-Path $Root 'BrowserGuardEdge.zip'
$DevZip  = Join-Path $Root 'BrowserGuardEdgeDev.zip'
$ProdCrx = Join-Path $Root 'BrowserGuardEdge.crx'

# Drop the designated signing key here for release builds only; never commit it.
$ReleaseKey = Join-Path $Root 'pem\edge.pem'

$DevNameSuffix = 'BrowserGuard Enterprise Developer Edition'

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Tool([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path $Exe -Leaf) failed with exit code $LASTEXITCODE."
    }
}

# --- deps -------------------------------------------------------------------

function Invoke-Deps {
    $eslint = Join-Path $NpmBinDir 'eslint.cmd'
    if (Test-Path $eslint) {
        return
    }
    Write-Step 'npm install (fetching dev dependencies)'
    Push-Location $Root
    try {
        Invoke-Tool 'npm.cmd' @('install', '--no-fund', '--no-audit')
    }
    finally {
        Pop-Location
    }
}

# --- lint -------------------------------------------------------------------

function Get-LintableJsonFile {
    Get-ChildItem -Path $Root -Recurse -Filter '*.json' -File |
        Where-Object {
            $_.FullName -notlike "*\node_modules\*" -and
            $_.FullName -notlike "$DevDir\*" -and
            $_.FullName -notlike "$StageDir\*"
        }
}

function Invoke-JsonLint {
    Write-Step 'Checking JSON syntax'
    # JSON with an empty property name (package-lock.json has one) is rejected
    # unless -AsHashtable is used, which requires PowerShell 6 or later.
    $asHashtable = (Get-Command ConvertFrom-Json).Parameters.ContainsKey('AsHashtable')
    $failed = @()
    foreach ($file in Get-LintableJsonFile) {
        try {
            $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            if ($asHashtable) {
                $null = $text | ConvertFrom-Json -AsHashtable
            }
            else {
                $null = $text | ConvertFrom-Json
            }
        }
        catch {
            $relative = $file.FullName.Substring($Root.Length + 1)
            Write-Host "  NG $relative : $($_.Exception.Message)" -ForegroundColor Red
            $failed += $relative
        }
    }
    if ($failed.Count -gt 0) {
        throw "Invalid JSON: $($failed -join ', ')"
    }
}

function Invoke-Lint([switch]$Fix) {
    Invoke-Deps
    Write-Step ('ESLint' + $(if ($Fix) { ' (--fix)' } else { '' }))
    $eslintArgs = @('.', '--report-unused-disable-directives')
    if ($Fix) {
        $eslintArgs += '--fix'
    }
    Push-Location $Root
    try {
        Invoke-Tool (Join-Path $NpmBinDir 'eslint.cmd') $eslintArgs
    }
    finally {
        Pop-Location
    }
    if (-not $Fix) {
        Invoke-JsonLint
    }
}

# --- test -------------------------------------------------------------------

# Node's own runner, so the unit tests need no extra dependency.
# The files are listed explicitly because passing the directory does not
# discover them reliably.
function Invoke-Test {
    Write-Step 'Running the unit tests'
    $testDir = Join-Path $Root 'test'
    $files = @(Get-ChildItem -Path $testDir -Filter '*.test.js' -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName })
    if ($files.Count -eq 0) {
        Write-Host '    no tests found'
        return
    }
    Push-Location $Root
    try {
        Invoke-Tool 'node.exe' (@('--test') + $files)
    }
    finally {
        Pop-Location
    }
}

# --- clean ------------------------------------------------------------------

function Invoke-Clean {
    Write-Step 'Removing build artifacts'
    foreach ($path in @($ProdZip, $DevZip, $ProdCrx, $DevDir, $StageDir, (Join-Path $Root 'testee'))) {
        if (Test-Path $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    Get-ChildItem -Path $Root -Filter '*.zip' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $Root -Filter '*.xpi' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Path $EdgeDir -Filter '*.zip' -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

# --- package ----------------------------------------------------------------

# Collect the files shipped with the extension.
# JS files are discovered from the directory, so adding one cannot be forgotten.
function Copy-ExtensionFile([string]$Destination) {
    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Copy-Item (Join-Path $EdgeDir 'manifest.json') -Destination $Destination
    Copy-Item (Join-Path $EdgeDir 'misc')     -Destination $Destination -Recurse
    Copy-Item (Join-Path $EdgeDir '_locales') -Destination $Destination -Recurse
    Get-ChildItem -Path $EdgeDir -Filter '*.js' -File | Copy-Item -Destination $Destination
}

# Rename the dev edition so it can be installed alongside the production one.
function Rename-ToDevEdition([string]$Directory) {
    $localeDir = Join-Path $Directory '_locales'
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    Get-ChildItem -Path $localeDir -Recurse -Filter 'messages.json' -File | ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
        $text = $text -replace 'BrowserGuard', $DevNameSuffix
        [System.IO.File]::WriteAllText($_.FullName, $text, $utf8NoBom)
    }
}

function New-ZipFromDirectory([string]$SourceDir, [string]$DestinationZip) {
    if (Test-Path $DestinationZip) {
        Remove-Item -LiteralPath $DestinationZip -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    # includeBaseDirectory = $false so that manifest.json sits at the zip root.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDir,
        $DestinationZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Invoke-Package {
    Write-Step 'Building the production package'
    $prodStage = Join-Path $StageDir 'edge'
    Copy-ExtensionFile $prodStage
    New-ZipFromDirectory $prodStage $ProdZip
    Write-Host "    $ProdZip"

    Write-Step 'Building the developer package'
    # edge/dev can be loaded directly via "Load unpacked" in the browser.
    Copy-ExtensionFile $DevDir
    Rename-ToDevEdition $DevDir
    New-ZipFromDirectory $DevDir $DevZip
    Write-Host "    $DevZip"
    Write-Host "    $DevDir (unpacked)"

    if (Test-Path $StageDir) {
        Remove-Item -LiteralPath $StageDir -Recurse -Force
    }
}

# --- crx --------------------------------------------------------------------

# Prefer the stable channel: the App Paths registry entry may point at Beta/Dev,
# so it is only consulted after the well-known install locations.
function Get-EdgePath {
    $candidates = @()
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($base) {
            $candidates += (Join-Path $base 'Microsoft\Edge\Application\msedge.exe')
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $registryKeys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe'
    )
    foreach ($key in $registryKeys) {
        if (Test-Path $key) {
            $path = (Get-ItemProperty -LiteralPath $key).'(default)'
            if ($path -and (Test-Path $path)) {
                return $path
            }
        }
    }

    $command = Get-Command 'msedge.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    throw 'Could not find Microsoft Edge (msedge.exe).'
}

function Invoke-Crx {
    $edge = Get-EdgePath
    Write-Step "Edge: $edge"

    # Edge writes <directory name>.crx, so stage under the artifact name.
    $stage = Join-Path $StageDir 'BrowserGuardEdge'
    Copy-ExtensionFile $stage

    $producedCrx = "$stage.crx"
    $producedPem = "$stage.pem"
    Remove-Item -LiteralPath $producedCrx, $producedPem -Force -ErrorAction SilentlyContinue

    $edgeArgs = @(
        "--pack-extension=$stage",
        '--no-message-box',
        '--no-first-run',
        "--user-data-dir=$(Join-Path $StageDir 'edge-profile')"
    )

    $useReleaseKey = Test-Path $ReleaseKey
    if ($useReleaseKey) {
        $edgeArgs += "--pack-extension-key=$ReleaseKey"
        Write-Step "Signing key: $ReleaseKey (extension ID is stable)"
    }
    else {
        Write-Warning 'pem\edge.pem is missing, so Edge will generate a key. The extension ID changes on every build.'
        Write-Warning 'For an enterprise release build, place the designated key at pem\edge.pem.'
    }

    Write-Step 'Building the crx'
    & $edge @edgeArgs

    # msedge.exe returns immediately, so poll until the output file shows up.
    $timeoutSeconds = 120
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $producedCrx)) {
        if ($stopwatch.Elapsed.TotalSeconds -gt $timeoutSeconds) {
            throw "The crx was not produced within $timeoutSeconds seconds. Check the contents of manifest.json."
        }
        Start-Sleep -Milliseconds 250
    }

    Move-Item -LiteralPath $producedCrx -Destination $ProdCrx -Force
    if (-not $useReleaseKey -and (Test-Path $producedPem)) {
        # The auto-generated key is throwaway; do not leave it behind.
        Remove-Item -LiteralPath $producedPem -Force
    }
    Remove-Item -LiteralPath $StageDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "    $ProdCrx"
}

# --- help -------------------------------------------------------------------

function Show-Help {
    @'
Usage: make.bat [target]

  all           Run deps, lint, test, clean and package in order (default)
  packages      Same as all
  deps          npm install (only when not installed yet)
  lint          ESLint plus a JSON syntax check
  test          Unit tests (node --test)
  format        ESLint --fix
  clean         Remove the zip files, the dev directory and other artifacts
  package       Build the packages without running lint
  crx           Build a crx with Edge (for enterprise distribution)
  help          Show this help

Artifacts:
  BrowserGuardEdge.zip      Production
  BrowserGuardEdgeDev.zip   Developer edition (different extension name)
  BrowserGuardEdge.crx      Enterprise distribution (crx target only)
  edge/dev/                 Unpacked developer edition

Signing key:
  When pem\edge.pem exists it is used to sign, keeping the extension ID stable.
  Otherwise Edge generates a key and the ID changes on every build.
'@ | Write-Host
}

# --- entry point ------------------------------------------------------------

try {
    switch ($Target) {
        'help'         { Show-Help }
        'deps'         { Invoke-Deps }
        'lint'         { Invoke-Lint }
        'format'       { Invoke-Lint -Fix }
        'test'         { Invoke-Test }
        'clean'        { Invoke-Clean }
        'package'      { Invoke-Clean; Invoke-Package }
        'crx'          { Invoke-Crx }
        default {
            Invoke-Deps
            Invoke-Lint
            Invoke-Test
            Invoke-Clean
            Invoke-Package
            Write-Host ''
            Write-Host 'Build completed.' -ForegroundColor Green
        }
    }
}
catch {
    Write-Host ''
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
