# docs\parameter-sheet.xlsm の出力を検証します。
#
#   1. 初期状態のまま出力すると Resources\BrowserGuard.json と一致すること
#   2. 各シートに値を入れると、それが JSON に正しく載ること
#      (Windows のパスの \ が \\ になる、といったエスケープを含む)
#   3. 入力チェックが誤りを拾うこと
#
# ブックは読み取り専用で開き、変更は保存しません。
#
#   pwsh -File tools\parameter-sheet\test-parameter-sheet.ps1

[CmdletBinding()]
param(
    [string]$WorkbookPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
if (-not $WorkbookPath) {
    $WorkbookPath = Join-Path $repoRoot 'docs\parameter-sheet.xlsm'
}
$referencePath = Join-Path $repoRoot 'Resources\BrowserGuard.json'

$script:failures = 0

# PowerShell 7 からは $xl.Run(...) が COM の省略引数を渡せないため、InvokeMember で呼ぶ。
# 戻り値が空文字のときは $null になるので、文字列に揃えて返す。
function Invoke-Macro {
    param($Excel, [string]$Name)
    [string]$Excel.GetType().InvokeMember('Run', 'InvokeMethod', $null, $Excel, @($Name))
}

function Assert-Equal {
    param([string]$Label, $Expected, $Actual)

    if ($Expected -eq $Actual) {
        Write-Host "  ok   $Label"
    }
    else {
        Write-Host "  FAIL $Label" -ForegroundColor Red
        Write-Host "       expected: $Expected" -ForegroundColor Red
        Write-Host "       actual:   $Actual" -ForegroundColor Red
        $script:failures++
    }
}

function Assert-True {
    param([string]$Label, [bool]$Condition)
    Assert-Equal -Label $Label -Expected $true -Actual $Condition
}

# テーブルの「値」列のセルを、内部パラメータ名で引く。
function Get-ParamCell {
    param($Workbook, [string]$TableName, [string]$ParamName)

    foreach ($sheet in $Workbook.Worksheets) {
        foreach ($table in $sheet.ListObjects) {
            if ($table.Name -ne $TableName) { continue }
            $nameCol = $table.ListColumns('内部パラメータ名').Index
            $valueCol = $table.ListColumns('値').Index
            for ($i = 1; $i -le $table.DataBodyRange.Rows.Count; $i++) {
                if ("$($table.DataBodyRange.Cells($i, $nameCol).Value2)".Trim() -eq $ParamName) {
                    return $table.DataBodyRange.Cells($i, $valueCol)
                }
            }
        }
    }
    throw "$TableName に $ParamName がありません"
}

function Get-Table {
    param($Workbook, [string]$TableName)

    foreach ($sheet in $Workbook.Worksheets) {
        foreach ($table in $sheet.ListObjects) {
            if ($table.Name -eq $TableName) { return $table }
        }
    }
    throw "テーブルが見つかりません: $TableName"
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false

try {
    $wb = $xl.Workbooks.Open($WorkbookPath, $false, $true)  # UpdateLinks, ReadOnly

    Write-Host '初期状態の出力'
    $problems = Invoke-Macro $xl 'ValidationMessage'
    Assert-Equal -Label '入力チェックを通る' -Expected '' -Actual $problems

    $json = Invoke-Macro $xl 'BuildJson'
    $expected = (Get-Content -Raw -Encoding UTF8 $referencePath) -replace "`r`n", "`n"
    $actual = $json -replace "`r`n", "`n"
    Assert-Equal -Label 'Resources\BrowserGuard.json と一致する' -Expected $expected.Trim() -Actual $actual.Trim()

    Write-Host ''
    Write-Host '値を入れたときの出力'

    (Get-ParamCell $wb 'T_NetLogger' 'Enabled').Value2 = '有効'
    (Get-ParamCell $wb 'T_NetLogger' 'Upload').Value2 = '有効'
    (Get-ParamCell $wb 'T_NetLogger' 'LocalFile.Enabled').Value2 = '有効'
    (Get-ParamCell $wb 'T_NetLogger' 'LocalFile.Directory').Value2 = 'C:\BrowserGuard\netlog\%MACHINENAME%'
    (Get-ParamCell $wb 'T_NetLogger' 'LocalFile.MaxDays').Value2 = 90
    (Get-ParamCell $wb 'T_NetLogger' 'Sender.Enabled').Value2 = '有効'
    (Get-ParamCell $wb 'T_NetLogger' 'Sender.Endpoint').Value2 = 'https://collector.example.jp/netlog'

    (Get-ParamCell $wb 'T_UsageTimeLimit' 'Enabled').Value2 = '有効'
    (Get-ParamCell $wb 'T_UsageTimeLimit' 'MaxContinuousMinutes').Value2 = 45
    (Get-ParamCell $wb 'T_UsageTimeLimit' 'OnExceeded.Action').Value2 = 'ブラウザーを終了する'

    $ranges = Get-Table $wb 'A_UsageTimeLimit_AllowedTimeRanges'
    $ranges.DataBodyRange.Cells(1, 1).Value2 = '09:00'
    $ranges.DataBodyRange.Cells(1, 2).Value2 = '12:00'
    $ranges.DataBodyRange.Cells(2, 1).Value2 = '22:00'
    $ranges.DataBodyRange.Cells(2, 2).Value2 = '02:00'

    (Get-ParamCell $wb 'T_StartupLauncher' 'Enabled').Value2 = '有効'
    $programs = Get-Table $wb 'A_StartupLauncher_Programs'
    $programs.DataBodyRange.Cells(1, 1).Value2 = 'C:\Program Files\Audit\EnvDump.exe'
    $programs.DataBodyRange.Cells(1, 2).Value2 = "--mode`n監査"
    $programs.DataBodyRange.Cells(1, 3).Value2 = 'C:\Program Files\Audit'
    $programs.DataBodyRange.Cells(1, 4).Value2 = "BROWSERGUARD_STARTUP=1`nLABEL=引用符 `" を含む値"
    $programs.DataBodyRange.Cells(1, 5).Value2 = 'ABC123'

    $blocked = Get-Table $wb 'L_UploadFileBridge_BlockedUrls'
    $blocked.DataBodyRange.Cells(1, 1).Value2 = '^https://localhost'

    $json = Invoke-Macro $xl 'BuildJson'
    Assert-Equal -Label '入力チェックを通る' -Expected '' -Actual (Invoke-Macro $xl 'ValidationMessage')

    $config = $json | ConvertFrom-Json
    Assert-True  -Label 'JSON として解釈できる' -Condition ($null -ne $config)
    Assert-Equal -Label 'NetLogger.Enabled' -Expected $true -Actual $config.NetLogger.Enabled
    Assert-Equal -Label 'NetLogger.Browsing は無効のまま' -Expected $false -Actual $config.NetLogger.Browsing
    Assert-Equal -Label 'LocalFile.Directory (\ のエスケープ)' `
        -Expected 'C:\BrowserGuard\netlog\%MACHINENAME%' -Actual $config.NetLogger.LocalFile.Directory
    Assert-True  -Label 'JSON 上で \ が 2 文字になっている' `
        -Condition ($json -match [regex]::Escape('C:\\BrowserGuard\\netlog\\%MACHINENAME%'))
    Assert-Equal -Label 'LocalFile.MaxDays' -Expected 90 -Actual $config.NetLogger.LocalFile.MaxDays
    Assert-Equal -Label 'Sender.Endpoint' `
        -Expected 'https://collector.example.jp/netlog' -Actual $config.NetLogger.Sender.Endpoint

    Assert-Equal -Label 'OnExceeded.Action が内部値になる' -Expected 'Terminate' -Actual $config.UsageTimeLimit.OnExceeded.Action
    Assert-Equal -Label 'AllowedTimeRanges の件数' -Expected 2 -Actual $config.UsageTimeLimit.AllowedTimeRanges.Count
    Assert-Equal -Label 'AllowedTimeRanges[0].Start' -Expected '09:00' -Actual $config.UsageTimeLimit.AllowedTimeRanges[0].Start
    Assert-Equal -Label 'AllowedTimeRanges[1].End (日をまたぐ)' -Expected '02:00' -Actual $config.UsageTimeLimit.AllowedTimeRanges[1].End

    Assert-Equal -Label 'Programs の件数' -Expected 1 -Actual $config.StartupLauncher.Programs.Count
    Assert-Equal -Label 'Programs[0].Path' -Expected 'C:\Program Files\Audit\EnvDump.exe' -Actual $config.StartupLauncher.Programs[0].Path
    Assert-Equal -Label 'Arguments はセル内改行で分かれる' -Expected 2 -Actual $config.StartupLauncher.Programs[0].Arguments.Count
    Assert-Equal -Label 'Arguments[1] (日本語)' -Expected '監査' -Actual $config.StartupLauncher.Programs[0].Arguments[1]
    Assert-Equal -Label 'EnvironmentVariables の値' -Expected '1' -Actual $config.StartupLauncher.Programs[0].EnvironmentVariables.BROWSERGUARD_STARTUP
    Assert-Equal -Label 'EnvironmentVariables の " のエスケープ' `
        -Expected '引用符 " を含む値' -Actual $config.StartupLauncher.Programs[0].EnvironmentVariables.LABEL

    Assert-Equal -Label 'BlockedUrls[0]' -Expected '^https://localhost' -Actual $config.UploadFileBridge.BlockedUrls[0]
    Assert-Equal -Label '空のリストは [] のまま' -Expected 0 -Actual $config.UploadFileBridge.AllowedUrls.Count

    Write-Host ''
    Write-Host '入力チェック'

    (Get-ParamCell $wb 'T_NetLogger' 'Sender.Endpoint').Value2 = ''
    Assert-True -Label '送信が有効でエンドポイントが空なら止まる' `
        -Condition ((Invoke-Macro $xl 'ValidationMessage') -match '送信先エンドポイント')
    (Get-ParamCell $wb 'T_NetLogger' 'Sender.Endpoint').Value2 = 'https://collector.example.jp/netlog'

    $ranges.DataBodyRange.Cells(1, 1).Value2 = '9時'
    Assert-True -Label '時刻の書式が違えば止まる' -Condition ((Invoke-Macro $xl 'ValidationMessage') -match 'HH:mm')
    $ranges.DataBodyRange.Cells(1, 1).Value2 = '09:00'

    $programs.DataBodyRange.Cells(2, 2).Value2 = '--only-args'
    Assert-True -Label 'パスのない行があれば止まる' -Condition ((Invoke-Macro $xl 'ValidationMessage') -match '実行ファイルのパス')

    $wb.Close($false)
}
finally {
    $xl.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
}

Write-Host ''
if ($script:failures -gt 0) {
    Write-Host "$script:failures 件失敗しました。" -ForegroundColor Red
    exit 1
}
Write-Host 'すべて成功しました。' -ForegroundColor Green
