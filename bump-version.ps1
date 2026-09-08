#Requires -Version 5.1
<#
.SYNOPSIS
    Sets the release version everywhere it is written down.

.DESCRIPTION
    The version lives in several files that have to agree: the Inno Setup
    script, the extension manifest and the MSI project. Each one is rewritten
    by matching the field rather than the old value, so there is no way to pass
    the wrong "current version" and have the script quietly do nothing.

    A new MSI ProductCode is generated at the same time. It has to change from
    one version to the next or MajorUpgrade never fires, and it is pinned rather
    than left to WiX because BrowserGuard.iss names it to remove the MSI package
    when the Inno Setup package is removed. Both files are written together.

.PARAMETER Version
    The new version, as four numbers: 1.2.0.0

.EXAMPLE
    .\bump-version.ps1 1.1.0.0

.EXAMPLE
    .\bump-version.ps1 1.1.0.0 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter(Mandatory, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$changes = [System.Collections.Generic.List[object]]::new()

# BrowserGuard.iss is UTF-8 *with* BOM, which Inno Setup needs for its Japanese
# strings, while everything else is written without one. Set-Content would pick
# for itself -- and differently under Windows PowerShell and PowerShell 7 -- so
# each file is written back the way it was found.
function Read-TextFile {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = if ($hasBom) { 3 } else { 0 }
    return [pscustomobject]@{
        Text   = [System.Text.Encoding]::UTF8.GetString($bytes, $offset, $bytes.Length - $offset)
        HasBom = $hasBom
    }
}

function Write-TextFile {
    param([string]$Path, [string]$Text, [bool]$HasBom)

    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($HasBom))
}

# Optional means the text is prose that may have been reworded: say so and carry
# on, rather than stopping a release for a documentation line.
function Set-Version {
    param(
        [string]$RelativePath,
        [string]$Pattern,
        [string]$Replacement,
        [string]$What,
        [switch]$Optional
    )

    $path = Join-Path $PSScriptRoot $RelativePath
    if (-not (Test-Path $path)) {
        if ($Optional) {
            Write-Warning "not found, skipped: $RelativePath"
            return
        }
        throw "not found: $RelativePath"
    }

    $file = Read-TextFile $path
    $found = [regex]::Matches($file.Text, $Pattern)
    if ($found.Count -eq 0) {
        if ($Optional) {
            Write-Warning "nothing to change in $RelativePath ($What)"
            return
        }
        throw "could not find $What in $RelativePath"
    }

    $before = $found[0].Value
    $updated = [regex]::Replace($file.Text, $Pattern, $Replacement)
    if ($updated -eq $file.Text) {
        $changes.Add([pscustomobject]@{ File = $RelativePath; Was = $before; Now = '(unchanged)' })
        return
    }

    $after = [regex]::Match($updated, $Pattern).Value
    if ($PSCmdlet.ShouldProcess($RelativePath, "set $What to $Version")) {
        Write-TextFile -Path $path -Text $updated -HasBom $file.HasBom
    }
    $changes.Add([pscustomobject]@{ File = $RelativePath; Was = $before; Now = $after })
}

# Read before the manifest is rewritten: README refers to this value as the one
# that means "the build did not update".
$manifestPath = Join-Path $PSScriptRoot 'webextensions\edge\manifest.json'
$previousExtensionVersion = ([regex]::Match(
    (Read-TextFile $manifestPath).Text, '"version"\s*:\s*"([^"]*)"')).Groups[1].Value

$short = ($Version -split '\.')[0..1] -join '.'

Set-Version -RelativePath 'BrowserGuard.iss' `
    -Pattern '(?m)^(#define\s+AppVersion\s+")[^"]*(")' `
    -Replacement "`${1}$Version`${2}" `
    -What 'AppVersion'

Set-Version -RelativePath 'webextensions\edge\manifest.json' `
    -Pattern '("version"\s*:\s*")[^"]*(")' `
    -Replacement "`${1}$Version`${2}" `
    -What 'the extension version'

Set-Version -RelativePath 'BrowserGuardMsiSetup\BrowserGuardSetup.wixproj' `
    -Pattern '(<Version>)[^<]*(</Version>)' `
    -Replacement "`${1}$Version`${2}" `
    -What 'the MSI version'

Set-Version -RelativePath 'BrowserGuardMsiSetup\Package.wxs' `
    -Pattern '(BrowserGuardSetup-)\d+(?:\.\d+){3}(\.msi)' `
    -Replacement "`${1}$Version`${2}" `
    -What 'the example command line' `
    -Optional

Set-Version -RelativePath 'docs\BrowserGuardUserGuide.md' `
    -Pattern '(?m)^(title:.*\sv)\d+\.\d+(\s*)$' `
    -Replacement "`${1}$short`${2}" `
    -What 'the title version' `
    -Optional

if ($previousExtensionVersion) {
    Set-Version -RelativePath 'README.md' `
        -Pattern ('`' + [regex]::Escape($previousExtensionVersion) + '`') `
        -Replacement ('`' + $Version + '`') `
        -What 'the extension version it names' `
        -Optional
}

# The two sides uninstall each other, so they have to agree on this. Package.wxs
# writes it bare and BrowserGuard.iss in braces, the form msiexec takes.
$productCode = [guid]::NewGuid().ToString().ToUpper()

Set-Version -RelativePath 'BrowserGuardMsiSetup\Package.wxs' `
    -Pattern '(ProductCode=")[^"]*(")' `
    -Replacement "`${1}$productCode`${2}" `
    -What 'the MSI ProductCode'

Set-Version -RelativePath 'BrowserGuard.iss' `
    -Pattern "(MsiProductCode\s*=\s*'\{)[^}]*(\}';)" `
    -Replacement "`${1}$productCode`${2}" `
    -What 'the MSI ProductCode'

Write-Host ''
$changes | Format-Table -AutoSize
Write-Host "Version set to $Version, MSI ProductCode to $productCode."
Write-Host 'The host assembly carries no version of its own; see BrowserGuard.csproj.'
