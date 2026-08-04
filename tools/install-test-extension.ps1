#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the extension and the native messaging host into Edge for testing.

.DESCRIPTION
    Packs the developer edition of the extension with a throwaway signing key
    generated on first run, writes a matching update manifest, and registers it
    in the ExtensionSettings policy so that Edge installs it.

    The native messaging host is registered from bin\Debug\net8.0 so that the
    host can be rebuilt and debugged without reinstalling anything.

    The test signing key is never the release key, so the extension ID differs
    from the production one and the two can be installed side by side.

    There is only one registration per native messaging host name, so an
    already installed production host registration IS replaced. The previous
    values are saved and put back by -Uninstall.

    Requires an elevated PowerShell session because these live under HKLM.

.PARAMETER Uninstall
    Removes the policy entry and restores the registry values that were
    replaced. Pass -Purge as well to delete the generated files and the key.

.PARAMETER Purge
    With -Uninstall, deletes the install directory including the test key.
    The next install then generates a new key, and therefore a new ID.

.PARAMETER RestartEdge
    Stops every Edge process and starts it again once everything is registered,
    so that Edge picks up the registrations without closing it by hand. Only the
    processes are touched, nothing in the profile. Unsaved work in the browser
    is lost.

    Note that restarting does not by itself pull in a rebuilt extension: Edge
    keeps the copy it installed until its own update check runs.

.PARAMETER InstallRoot
    Where the packed crx, the manifests, the test config and the test key are
    kept. Defaults to .testinstall in the repository root.

.EXAMPLE
    .\tools\install-test-extension.ps1
    Builds, registers, and reports what to look for in Edge.

.EXAMPLE
    .\tools\install-test-extension.ps1 -WhatIf
    Builds the files but only previews the registry changes.

.EXAMPLE
    .\tools\install-test-extension.ps1 -Uninstall -Purge
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Uninstall,
    [switch]$Purge,
    [switch]$RestartEdge,
    [string]$InstallRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$WebExtRoot  = Join-Path $RepoRoot 'webextensions'
$SourceDir   = Join-Path $WebExtRoot 'edge\dev'
$HostProject = Join-Path $RepoRoot 'BrowserGuard\BrowserGuard.csproj'
$HostExe     = Join-Path $RepoRoot 'BrowserGuard\bin\Debug\net8.0\BrowserGuard.exe'

if (-not $InstallRoot) {
    $InstallRoot = Join-Path $RepoRoot '.testinstall'
}

$TestKey      = Join-Path $InstallRoot 'test.pem'
$StageDir     = Join-Path $InstallRoot 'BrowserGuardEdgeTest'
$CrxPath      = "$StageDir.crx"
$UpdateXml    = Join-Path $InstallRoot 'manifest.xml'
$HostManifest = Join-Path $InstallRoot 'edge.json'
$TestConfig   = Join-Path $InstallRoot 'BrowserGuard.json'
$BackupFile   = Join-Path $InstallRoot 'registry-backup.json'

$HostName      = 'com.clear_code.browser_guard'
$PolicyKey     = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'
$SettingsValue = 'ExtensionSettings'
$NativeHostKey = "HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\$HostName"
$OwnKey        = 'HKLM:\Software\BrowserGuard'

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Elevated {
    # -WhatIf only previews the registry changes, so elevation is not needed then.
    if ($WhatIfPreference) {
        return
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script writes HKLM values, so it must run in an elevated PowerShell session.'
    }
}

function ConvertTo-FileUrl([string]$Path) {
    return 'file:///' + ($Path -replace '\\', '/' -replace ' ', '%20')
}

function Get-EdgePath {
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($base) {
            $candidate = Join-Path $base 'Microsoft\Edge\Application\msedge.exe'
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }
    throw 'Could not find Microsoft Edge (msedge.exe).'
}

# The extension ID is the first 128 bits of SHA-256 over the DER public key,
# with every nibble mapped onto a-p.
function Get-ExtensionIdFromKey([string]$KeyPath) {
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([System.IO.File]::ReadAllText($KeyPath))
    $hash = [System.Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())
    return -join ($hash[0..15] | ForEach-Object {
        [char](97 + ($_ -shr 4))
        [char](97 + ($_ -band 0x0F))
    })
}

# Taken from the shipped update manifest so the ID is not duplicated here.
function Get-ProductionExtensionId {
    $path = Join-Path $RepoRoot 'Resources\manifest.xml'
    if (-not (Test-Path $path)) {
        return $null
    }
    $match = [regex]::Match((Get-Content $path -Raw), "appid='([a-p]{32})'")
    if ($match.Success) {
        return $match.Groups[1].Value
    }
    return $null
}

# Edge only re-downloads a force installed extension when the update manifest
# advertises a newer version, so every run gets a version derived from the clock.
# Components have to stay below 65536, hence days and minutes rather than a
# full timestamp.
function Get-TestVersion([string]$BaseVersion) {
    $parts = $BaseVersion.Split('.')
    $major = $parts[0]
    $minor = if ($parts.Length -gt 1) { $parts[1] } else { '0' }
    $now = Get-Date
    $days = [int]($now.Date - [datetime]'2020-01-01').TotalDays
    $minutes = $now.Hour * 60 + $now.Minute
    return "$major.$minor.$days.$minutes"
}

# Rewrites just the version member, leaving the rest of the file untouched.
function Set-ManifestVersion([string]$ManifestPath, [string]$Version) {
    $text = [System.IO.File]::ReadAllText($ManifestPath)
    $updated = [regex]::Replace($text, '("version"\s*:\s*")[^"]*(")', "`${1}$Version`${2}")
    [System.IO.File]::WriteAllText($ManifestPath, $updated, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-ManifestVersion([string]$ManifestPath) {
    $match = [regex]::Match([System.IO.File]::ReadAllText($ManifestPath), '"version"\s*:\s*"([^"]*)"')
    if (-not $match.Success) {
        throw "No version found in $ManifestPath"
    }
    return $match.Groups[1].Value
}

function Invoke-EdgePack([string]$Directory, [string]$KeyPath) {
    $edge = Get-EdgePath
    $produced = "$Directory.crx"
    # Building the artifacts is not what -WhatIf is meant to preview; only the
    # registry changes are. Opt the file operations out of it.
    Remove-Item -LiteralPath $produced -Force -ErrorAction SilentlyContinue -WhatIf:$false

    $edgeArgs = @(
        "--pack-extension=$Directory",
        '--no-message-box',
        '--no-first-run',
        "--user-data-dir=$(Join-Path $InstallRoot 'edge-profile')"
    )
    if ($KeyPath -and (Test-Path $KeyPath)) {
        $edgeArgs += "--pack-extension-key=$KeyPath"
    }

    & $edge @edgeArgs

    # msedge.exe returns immediately, so poll until the output file shows up.
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $produced)) {
        if ($stopwatch.Elapsed.TotalSeconds -gt 120) {
            throw 'Edge did not produce the crx within 120 seconds.'
        }
        Start-Sleep -Milliseconds 250
    }
    return $produced
}

# Only the processes are touched; nothing in the profile is modified.
function Restart-Edge {
    $processes = Get-Process -Name 'msedge' -ErrorAction SilentlyContinue
    if ($processes) {
        if ($PSCmdlet.ShouldProcess('msedge', "Stop $($processes.Count) process(es)")) {
            Write-Host "    stopping $($processes.Count) process(es)"
            $processes | Stop-Process -Force -ErrorAction SilentlyContinue -WhatIf:$false

            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            while (Get-Process -Name 'msedge' -ErrorAction SilentlyContinue) {
                if ($stopwatch.Elapsed.TotalSeconds -gt 30) {
                    throw 'Edge is still running after 30 seconds.'
                }
                Start-Sleep -Milliseconds 250
            }
        }
    }
    else {
        Write-Host '    Edge was not running'
    }

    if ($PSCmdlet.ShouldProcess('Microsoft Edge', 'Start')) {
        Start-Process -FilePath (Get-EdgePath) -WhatIf:$false | Out-Null
        Write-Host '    started'
    }
}

# ExtensionInstallForcelist would only supply the update URL for the first
# install; afterwards Edge looks at update_url inside the extension's own
# manifest, which a self hosted build does not have. ExtensionSettings with
# override_update_url makes Edge keep using this URL for update checks too,
# which is why it is used on its own here.
#
# The value covers every extension, so the existing JSON is preserved and only
# this extension's entry is added.
function Set-ExtensionSettingsEntry([string]$ExtensionId, [string]$UpdateUrl) {
    $settings = [ordered]@{}
    $existing = Get-RegistryValue $PolicyKey $SettingsValue
    if ($existing) {
        try {
            foreach ($member in ($existing | ConvertFrom-Json).PSObject.Properties) {
                $settings[$member.Name] = $member.Value
            }
        }
        catch {
            Write-Warning 'The existing ExtensionSettings value is not valid JSON and will be replaced.'
        }
    }

    $settings[$ExtensionId] = [ordered]@{
        installation_mode   = 'force_installed'
        update_url          = $UpdateUrl
        override_update_url = $true
    }

    $json = $settings | ConvertTo-Json -Depth 10 -Compress
    if ($PSCmdlet.ShouldProcess("$PolicyKey\$SettingsValue", "Set to $json")) {
        Set-ItemProperty -Path $PolicyKey -Name $SettingsValue -Value $json -Type String -WhatIf:$false
        Write-Host "    $json"
    }
}

function Remove-ExtensionSettingsEntry([string]$ExtensionId) {
    $existing = Get-RegistryValue $PolicyKey $SettingsValue
    if (-not $existing) {
        return
    }

    $settings = [ordered]@{}
    try {
        foreach ($member in ($existing | ConvertFrom-Json).PSObject.Properties) {
            if ($member.Name -ne $ExtensionId) {
                $settings[$member.Name] = $member.Value
            }
        }
    }
    catch {
        Write-Warning 'The existing ExtensionSettings value is not valid JSON, leaving it alone.'
        return
    }

    if ($settings.Count -eq 0) {
        if ($PSCmdlet.ShouldProcess("$PolicyKey\$SettingsValue", 'Remove value')) {
            Remove-ItemProperty -Path $PolicyKey -Name $SettingsValue -WhatIf:$false
            Write-Step 'Removed the ExtensionSettings value'
        }
    }
    else {
        $json = $settings | ConvertTo-Json -Depth 10 -Compress
        if ($PSCmdlet.ShouldProcess("$PolicyKey\$SettingsValue", 'Drop this extension from ExtensionSettings')) {
            Set-ItemProperty -Path $PolicyKey -Name $SettingsValue -Value $json -Type String -WhatIf:$false
            Write-Step 'Dropped this extension from ExtensionSettings'
        }
    }
}

function Get-RegistryValue([string]$Key, [string]$Name) {
    if (-not (Test-Path $Key)) {
        return $null
    }
    $item = Get-Item $Key
    if ($item.GetValueNames() -notcontains $Name) {
        return $null
    }
    return $item.GetValue($Name)
}

# Written once, so that repeated installs never overwrite the original values.
function Save-RegistryBackup {
    if (Test-Path $BackupFile) {
        return
    }
    $backup = [ordered]@{
        NativeHostManifest = Get-RegistryValue $NativeHostKey ''
        ConfigFile         = Get-RegistryValue $OwnKey 'Configfile'
    }
    $backup | ConvertTo-Json | Set-Content -LiteralPath $BackupFile -Encoding UTF8 -WhatIf:$false
    Write-Step "Saved the previous registry values to $BackupFile"
}

function Restore-RegistryBackup {
    if (-not (Test-Path $BackupFile)) {
        Write-Warning 'No registry backup found, leaving the current values alone.'
        return
    }
    $backup = Get-Content $BackupFile -Raw | ConvertFrom-Json

    if ($backup.NativeHostManifest) {
        if ($PSCmdlet.ShouldProcess($NativeHostKey, "Restore to $($backup.NativeHostManifest)")) {
            New-Item -Path $NativeHostKey -Force -WhatIf:$false | Out-Null
            Set-ItemProperty -Path $NativeHostKey -Name '(default)' -Value $backup.NativeHostManifest -WhatIf:$false
            Write-Step "Restored the native messaging host registration"
        }
    }
    elseif (Test-Path $NativeHostKey) {
        if ($PSCmdlet.ShouldProcess($NativeHostKey, 'Remove registry key (was not registered before)')) {
            Remove-Item -Path $NativeHostKey -Recurse -Force -WhatIf:$false
            Write-Step 'Removed the native messaging host registration'
        }
    }

    if ($backup.ConfigFile) {
        if ($PSCmdlet.ShouldProcess($OwnKey, "Restore Configfile to $($backup.ConfigFile)")) {
            Set-ItemProperty -Path $OwnKey -Name 'Configfile' -Value $backup.ConfigFile -WhatIf:$false
            Write-Step 'Restored the config file path'
        }
    }
    elseif ((Get-RegistryValue $OwnKey 'Configfile') -ne $null) {
        if ($PSCmdlet.ShouldProcess($OwnKey, 'Remove Configfile (was not set before)')) {
            Remove-ItemProperty -Path $OwnKey -Name 'Configfile' -WhatIf:$false
            Write-Step 'Removed the config file path'
        }
    }
}

# --- uninstall --------------------------------------------------------------

if ($Uninstall) {
    Assert-Elevated

    if (Test-Path $TestKey) {
        $extensionId = Get-ExtensionIdFromKey $TestKey
        Remove-ExtensionSettingsEntry $extensionId
    }
    else {
        Write-Warning "No test key at $TestKey, so no policy entry could be identified."
    }

    Restore-RegistryBackup

    if ($Purge -and (Test-Path $InstallRoot)) {
        if ($PSCmdlet.ShouldProcess($InstallRoot, 'Delete directory')) {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force -WhatIf:$false
            Write-Step "Deleted $InstallRoot"
        }
    }

    if ($RestartEdge) {
        Write-Step 'Restarting Edge'
        Restart-Edge
        Write-Host ''
        Write-Host 'Done.' -ForegroundColor Green
    }
    else {
        Write-Host ''
        Write-Host 'Restart Edge for the change to take effect.' -ForegroundColor Green
    }
    return
}

# --- install ----------------------------------------------------------------

Assert-Elevated

# Always rebuild: reusing a stale edge\dev would silently package the previous
# version of the extension.
Write-Step 'Building the extension'
& (Join-Path $WebExtRoot 'build.bat') all
if ($LASTEXITCODE -ne 0) {
    throw 'Building the extension failed.'
}
if (-not (Test-Path $SourceDir)) {
    throw "The extension was not staged: $SourceDir"
}

New-Item -ItemType Directory -Path $InstallRoot -Force -WhatIf:$false | Out-Null
Save-RegistryBackup

# --- extension --------------------------------------------------------------

# Let Edge generate the key on first run, so the format is guaranteed to match.
if (-not (Test-Path $TestKey)) {
    Write-Step 'Generating a test signing key'
    $seedDir = Join-Path $InstallRoot 'keyseed'
    Copy-Item $SourceDir -Destination $seedDir -Recurse -Force -WhatIf:$false
    $null = Invoke-EdgePack $seedDir $null
    Move-Item -LiteralPath "$seedDir.pem" -Destination $TestKey -Force -WhatIf:$false
    Remove-Item -LiteralPath $seedDir, "$seedDir.crx" -Recurse -Force -ErrorAction SilentlyContinue -WhatIf:$false
}

$extensionId = Get-ExtensionIdFromKey $TestKey
Write-Step "Test extension ID: $extensionId"

Write-Step 'Packing the crx'
if (Test-Path $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force -WhatIf:$false
}
Copy-Item $SourceDir -Destination $StageDir -Recurse -Force -WhatIf:$false

$stagedManifest = Join-Path $StageDir 'manifest.json'
$version = Get-TestVersion (Get-ManifestVersion $stagedManifest)
Set-ManifestVersion $stagedManifest $version
Write-Host "    version $version"

$null = Invoke-EdgePack $StageDir $TestKey
Write-Host "    $CrxPath"

Write-Step 'Writing the update manifest'
$xml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='$extensionId'>
    <updatecheck codebase='$(ConvertTo-FileUrl $CrxPath)' version='$version' />
  </app>
</gupdate>
"@
[System.IO.File]::WriteAllText($UpdateXml, $xml, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "    $UpdateXml"

# --- native messaging host --------------------------------------------------

Write-Step 'Building the native messaging host (Debug)'
& dotnet build $HostProject -c Debug --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    throw 'Building the native messaging host failed.'
}
if (-not (Test-Path $HostExe)) {
    throw "The host executable was not produced: $HostExe"
}
Write-Host "    $HostExe"

Write-Step 'Writing the native messaging host manifest'
# The production extension is kept in allowed_origins so that an already
# installed production build keeps working while the test build is registered.
$origins = @("chrome-extension://$extensionId/")
$productionId = Get-ProductionExtensionId
if ($productionId -and $productionId -ne $extensionId) {
    $origins += "chrome-extension://$productionId/"
}
$hostJson = [ordered]@{
    name            = $HostName
    description     = 'BrowserGuard Server (Debug build, registered for testing)'
    path            = $HostExe
    type            = 'stdio'
    allowed_origins = $origins
} | ConvertTo-Json
[System.IO.File]::WriteAllText($HostManifest, $hostJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "    $HostManifest"

Write-Step 'Writing the test config'
if (-not (Test-Path $TestConfig)) {
    Copy-Item (Join-Path $RepoRoot 'Resources\BrowserGuard.json') -Destination $TestConfig -WhatIf:$false
}
Write-Host "    $TestConfig"

# --- registry ---------------------------------------------------------------

Write-Step 'Registering ExtensionSettings'
Set-ExtensionSettingsEntry $extensionId (ConvertTo-FileUrl $UpdateXml)

Write-Step 'Registering the native messaging host'
if ($PSCmdlet.ShouldProcess($NativeHostKey, "Set to $HostManifest")) {
    New-Item -Path $NativeHostKey -Force -WhatIf:$false | Out-Null
    Set-ItemProperty -Path $NativeHostKey -Name '(default)' -Value $HostManifest -WhatIf:$false
    Write-Host "    $HostManifest"
}

Write-Step 'Pointing the host at the test config'
if ($PSCmdlet.ShouldProcess("$OwnKey\Configfile", "Set to $TestConfig")) {
    New-Item -Path $OwnKey -Force -WhatIf:$false | Out-Null
    Set-ItemProperty -Path $OwnKey -Name 'Configfile' -Value $TestConfig -Type String -WhatIf:$false
    Write-Host "    $TestConfig"
}

if ($RestartEdge) {
    Write-Step 'Restarting Edge'
    Restart-Edge
}

# --- done -------------------------------------------------------------------

Write-Host ''
Write-Host "Done. Packaged version $version" -ForegroundColor Green
Write-Host ''
if (-not $RestartEdge) {
    Write-Host 'Restart Edge so it sees the registrations, or pass -RestartEdge'
    Write-Host 'to have this script do it.'
    Write-Host ''
}
Write-Host 'Restarting is not enough to pull in a rebuilt extension: Edge keeps'
Write-Host 'the copy it installed until its own update check runs, which can take'
Write-Host 'hours. To apply this build now, open edge://extensions, turn on'
Write-Host 'Developer mode and press Update.'
Write-Host ''
Write-Host 'Then confirm on edge://extensions that the version reads'
Write-Host "  $version"
Write-Host 'If it still reads the old version, the running code is the old build.'
Write-Host ''
Write-Host "  edge://policy      - ExtensionSettings should list $extensionId"
Write-Host ''
Write-Host 'The native messaging host now runs from the Debug build:'
Write-Host "  $HostExe"
Write-Host '  Rebuild it with: dotnet build BrowserGuard\BrowserGuard.csproj -c Debug'
Write-Host '  Edge starts the host per message, so a rebuild takes effect without re-running this script.'
Write-Host ''
Write-Host 'Edit the test config to turn features on:'
Write-Host "  $TestConfig"
Write-Host "  Host log: $env:APPDATA\BrowserGuard\BrowserGuard.log"
Write-Host ''
Write-Host 'To put everything back:'
Write-Host '  .\tools\install-test-extension.ps1 -Uninstall'
