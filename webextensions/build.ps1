#Requires -Version 5.1
<#
.SYNOPSIS
    BrowserGuard 拡張機能のビルドスクリプト（Windows 用）。
.DESCRIPTION
    Makefile の各ターゲットを Windows ネイティブに置き換えたもの。
    通常は build.bat 経由で呼び出す。
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('all', 'packages', 'deps', 'lint', 'format', 'clean', 'package', 'crx', 'install_hook', 'help')]
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

# リリースビルド時のみ、所定の署名鍵をここに配置する（リポジトリには含めない）。
$ReleaseKey = Join-Path $Root 'pem\edge.pem'

$DevNameSuffix = 'BrowserGuard Enterprise Developer Edition'

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Tool([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path $Exe -Leaf) が終了コード $LASTEXITCODE で失敗しました。"
    }
}

# --- deps -------------------------------------------------------------------

function Invoke-Deps {
    $eslint = Join-Path $NpmBinDir 'eslint.cmd'
    if (Test-Path $eslint) {
        return
    }
    Write-Step 'npm install (開発用依存関係を取得)'
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
    Write-Step 'JSON 構文チェック'
    # package-lock.json のように空文字のプロパティ名を含む JSON は
    # -AsHashtable が無いと弾かれてしまう (PowerShell 6 以降で利用可能)。
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
        throw "JSON 構文エラー: $($failed -join ', ')"
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

# --- clean ------------------------------------------------------------------

function Invoke-Clean {
    Write-Step '生成物を削除'
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

# 拡張機能に同梱するファイルを収集する。
# JS はディレクトリから自動収集するため、ファイル追加時のリスト更新漏れが起きない。
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

# 開発版は拡張機能名を変えて、製品版と併存できるようにする。
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
    # includeBaseDirectory = $false: zip 直下に manifest.json が来るようにする。
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDir,
        $DestinationZip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Invoke-Package {
    Write-Step '製品版パッケージを作成'
    $prodStage = Join-Path $StageDir 'edge'
    Copy-ExtensionFile $prodStage
    New-ZipFromDirectory $prodStage $ProdZip
    Write-Host "    $ProdZip"

    Write-Step '開発版パッケージを作成'
    # edge/dev は「パッケージ化されていない拡張機能を読み込む」でそのまま使える。
    Copy-ExtensionFile $DevDir
    Rename-ToDevEdition $DevDir
    New-ZipFromDirectory $DevDir $DevZip
    Write-Host "    $DevZip"
    Write-Host "    $DevDir (未パッケージ版)"

    if (Test-Path $StageDir) {
        Remove-Item -LiteralPath $StageDir -Recurse -Force
    }
}

# --- crx --------------------------------------------------------------------

# 安定版を優先して探す。レジストリの App Paths は Beta/Dev を指すことがあるため後回しにする。
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
    throw 'Microsoft Edge (msedge.exe) が見つかりませんでした。'
}

function Invoke-Crx {
    $edge = Get-EdgePath
    Write-Step "Edge: $edge"

    # Edge は <ディレクトリ名>.crx を出力するため、成果物名でステージングする。
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
        Write-Step "署名鍵: $ReleaseKey (拡張機能 ID は固定)"
    }
    else {
        Write-Warning "pem\edge.pem がないため Edge が鍵を自動生成します。拡張機能 ID はビルドごとに変わります。"
        Write-Warning '企業内配布用のリリースビルドでは、所定の鍵を pem\edge.pem に配置してください。'
    }

    Write-Step 'crx を作成'
    & $edge @edgeArgs

    # msedge.exe は即座に制御を返すため、出力ファイルの生成をポーリングで待つ。
    $timeoutSeconds = 120
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $producedCrx)) {
        if ($stopwatch.Elapsed.TotalSeconds -gt $timeoutSeconds) {
            throw "crx が $timeoutSeconds 秒以内に生成されませんでした。manifest.json の内容を確認してください。"
        }
        Start-Sleep -Milliseconds 250
    }

    Move-Item -LiteralPath $producedCrx -Destination $ProdCrx -Force
    if (-not $useReleaseKey -and (Test-Path $producedPem)) {
        # 自動生成された鍵は使い捨てなので残さない。
        Remove-Item -LiteralPath $producedPem -Force
    }
    Remove-Item -LiteralPath $StageDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "    $ProdCrx"
}

# --- install_hook -----------------------------------------------------------

function Install-GitHook {
    $hookDir = Join-Path $Root '..\.git\hooks'
    if (-not (Test-Path $hookDir)) {
        throw "git hooks ディレクトリが見つかりません: $hookDir"
    }
    $hookPath = Join-Path $hookDir 'pre-commit'
    $content = "#!/bin/sh`nexec cmd.exe /c `"`$(dirname `"`$0`")/../../webextensions/build.bat`" lint`n"
    [System.IO.File]::WriteAllText($hookPath, $content, (New-Object System.Text.UTF8Encoding($false)))
    Write-Step "pre-commit フックを設置: $hookPath"
}

# --- help -------------------------------------------------------------------

function Show-Help {
    @'
使い方: build.bat [ターゲット]

  all           deps, lint, clean, package を順に実行 (既定)
  packages      all と同じ
  deps          npm install (未インストール時のみ)
  lint          ESLint + JSON 構文チェック
  format        ESLint --fix
  clean         zip や dev ディレクトリなどの生成物を削除
  package       lint を行わずにパッケージのみ作成
  crx           Edge で crx を作成 (企業内配布用)
  install_hook  git の pre-commit フックに lint を設定
  help          このヘルプを表示

生成物:
  BrowserGuardEdge.zip      製品版
  BrowserGuardEdgeDev.zip   開発版 (拡張機能名が異なる)
  BrowserGuardEdge.crx      企業内配布用 (crx ターゲット時のみ)
  edge/dev/                 開発版の未パッケージ版

署名鍵:
  pem\edge.pem があればそれで署名し、拡張機能 ID が固定されます。
  無い場合は Edge が鍵を自動生成するため ID は毎回変わります。
'@ | Write-Host
}

# --- entry point ------------------------------------------------------------

try {
    switch ($Target) {
        'help'         { Show-Help }
        'deps'         { Invoke-Deps }
        'lint'         { Invoke-Lint }
        'format'       { Invoke-Lint -Fix }
        'clean'        { Invoke-Clean }
        'package'      { Invoke-Clean; Invoke-Package }
        'crx'          { Invoke-Crx }
        'install_hook' { Install-GitHook }
        default {
            Invoke-Deps
            Invoke-Lint
            Invoke-Clean
            Invoke-Package
            Write-Host ''
            Write-Host 'ビルドが完了しました。' -ForegroundColor Green
        }
    }
}
catch {
    Write-Host ''
    Write-Host "ビルドに失敗しました: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
