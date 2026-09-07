# docs\parameter-sheet.xlsm を作り直します。
#
# シートの体裁と ExportConfig.bas をこのスクリプトが埋め込むため、パラメータの
# 追加や文言の修正はここと .bas を直してから再実行してください
# (xlsm を直接編集しても、次回の実行で上書きされます)。
#
# 実行には Excel と、[ファイル] > [オプション] > [トラスト センター] >
# [トラスト センターの設定] > [マクロの設定] にある
# 「VBA プロジェクト オブジェクト モデルへのアクセスを信頼する」が必要です。
#
#   pwsh -File tools\parameter-sheet\make-parameter-sheet.ps1

[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'docs\parameter-sheet.xlsm'
}
$vbaPath = Join-Path $scriptDir 'ExportConfig.bas'

# Excel の列挙体 (COM 越しでは名前で参照できないため定数で持つ)
$xlSrcRange = 1
$xlYes = 1
$xlValidateList = 3
$xlValidateWholeNumber = 1
$xlValidAlertStop = 1
$xlGreaterEqual = 7
$xlOpenXMLWorkbookMacroEnabled = 52
$msoShapeRoundedRectangle = 5
$vbextCtStdModule = 1

$COLOR_ACCENT = 7949855      # 濃紺 (ボタン地)
$COLOR_WHITE = 16777215
$COLOR_HEADING = 7949855
$COLOR_MUTED = 8421504

function New-Param {
    param(
        [string]$Group,
        [string]$Label,
        $Value,
        [string]$Name,
        [ValidateSet('bool', 'int', 'text', 'choice')][string]$Kind,
        [string[]]$Choices = @(),
        [string]$Note = ''
    )
    [pscustomobject]@{
        Group = $Group; Label = $Label; Value = $Value
        Name = $Name; Kind = $Kind; Choices = $Choices; Note = $Note
    }
}

function Set-SheetHeading {
    param($Sheet, [string]$Title, [string[]]$Lines)

    $Sheet.Cells(1, 1).Value2 = $Title
    $Sheet.Cells(1, 1).Font.Size = 16
    $Sheet.Cells(1, 1).Font.Bold = $true
    $Sheet.Cells(1, 1).Font.Color = $COLOR_HEADING

    $row = 2
    foreach ($line in $Lines) {
        $Sheet.Cells($row, 1).Value2 = $line
        $Sheet.Cells($row, 1).Font.Color = $COLOR_MUTED
        $row++
    }
    return $row + 1
}

function Set-SectionLabel {
    param($Sheet, [int]$Row, [int]$Column, [string]$Text)

    $Sheet.Cells($Row, $Column).Value2 = $Text
    $Sheet.Cells($Row, $Column).Font.Bold = $true
    $Sheet.Cells($Row, $Column).Font.Color = $COLOR_HEADING
}

function New-Table {
    param($Sheet, [int]$Row, [int]$Column, [string]$Name, [string[]]$Headers, [int]$RowCount)

    for ($i = 0; $i -lt $Headers.Count; $i++) {
        $Sheet.Cells($Row, $Column + $i).Value2 = $Headers[$i]
    }
    $range = $Sheet.Range(
        $Sheet.Cells($Row, $Column),
        $Sheet.Cells($Row + $RowCount, $Column + $Headers.Count - 1))

    $table = $Sheet.ListObjects.Add($xlSrcRange, $range, [Type]::Missing, $xlYes)
    $table.Name = $Name
    $table.TableStyle = 'TableStyleLight9'
    $table.ShowAutoFilter = $false
    return $table
}

function Add-ListValidation {
    param($Range, [string[]]$Choices)

    $Range.Validation.Delete()
    $Range.Validation.Add($xlValidateList, $xlValidAlertStop, 1, ($Choices -join ',')) | Out-Null
    $Range.Validation.IgnoreBlank = $true
    $Range.Validation.InCellDropdown = $true
}

function Add-WholeNumberValidation {
    param($Range)

    $Range.Validation.Delete()
    $Range.Validation.Add($xlValidateWholeNumber, $xlValidAlertStop, $xlGreaterEqual, '0') | Out-Null
    $Range.Validation.IgnoreBlank = $true
    $Range.Validation.ErrorTitle = '入力エラー'
    $Range.Validation.ErrorMessage = '0 以上の整数を入力してください。'
}

# パラメータ表。「値」と「内部パラメータ名」の列名で VBA が引くので、列を足しても壊れない。
function New-ParamTable {
    param($Sheet, [int]$Row, [string]$Name, [object[]]$Params, [switch]$WithGroup)

    $headers = @()
    if ($WithGroup) { $headers += '分類' }
    $headers += @('パラメータ', '値', '内部パラメータ名', '備考')

    $table = New-Table -Sheet $Sheet -Row $Row -Column 1 -Name $Name -Headers $headers -RowCount $Params.Count

    $offset = if ($WithGroup) { 1 } else { 0 }
    for ($i = 0; $i -lt $Params.Count; $i++) {
        $p = $Params[$i]
        $r = $Row + 1 + $i
        if ($WithGroup) { $Sheet.Cells($r, 1).Value2 = $p.Group }
        $Sheet.Cells($r, 1 + $offset).Value2 = $p.Label
        $Sheet.Cells($r, 3 + $offset).Value2 = $p.Name
        $Sheet.Cells($r, 4 + $offset).Value2 = $p.Note

        $valueCell = $Sheet.Cells($r, 2 + $offset)
        switch ($p.Kind) {
            'bool' {
                Add-ListValidation -Range $valueCell -Choices @('有効', '無効')
                $valueCell.Value2 = $p.Value
            }
            'choice' {
                Add-ListValidation -Range $valueCell -Choices $p.Choices
                $valueCell.Value2 = $p.Value
            }
            'int' {
                Add-WholeNumberValidation -Range $valueCell
                $valueCell.NumberFormatLocal = '0'
                $valueCell.Value2 = [int]$p.Value
            }
            'text' {
                $valueCell.NumberFormatLocal = '@'
                if ($p.Value) { $valueCell.Value2 = $p.Value }
            }
        }
        $valueCell.HorizontalAlignment = -4131  # xlLeft
    }

    $noteColumn = 4 + $offset
    $Sheet.Columns($noteColumn).WrapText = $false
    return $table
}

# 文字列の配列。1 列目だけを読むので、見出しは説明的にしてよい。
function New-ListTable {
    param($Sheet, [int]$Row, [int]$Column, [string]$Name, [string]$Header,
          [string[]]$Items = @(), [int]$BlankRows = 15, [int]$Width = 34)

    $rowCount = [Math]::Max($Items.Count + $BlankRows, 5)
    $table = New-Table -Sheet $Sheet -Row $Row -Column $Column -Name $Name -Headers @($Header) -RowCount $rowCount

    $Sheet.Range($Sheet.Cells($Row + 1, $Column), $Sheet.Cells($Row + $rowCount, $Column)).NumberFormatLocal = '@'
    for ($i = 0; $i -lt $Items.Count; $i++) {
        $Sheet.Cells($Row + 1 + $i, $Column).Value2 = $Items[$i]
    }
    $Sheet.Columns($Column).ColumnWidth = $Width
    return $table
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.ScreenUpdating = $false

try {
    if ($xl.Workbooks.Count -eq 0) { $xl.SheetsInNewWorkbook = 1 }
    $wb = $xl.Workbooks.Add()
    while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }

    $sheetNames = @(
        '本パラメータシートについて',
        'エクスポート',
        '監査ログ (NetLogger)',
        'アップロード制御 (UploadGuard)',
        '設定ページの遮断 (SettingPageFilter)',
        '起動時プログラム実行 (StartupLauncher)',
        '使用時間の制限 (UsageTimeLimit)',
        'タブ数の上限 (TabCountLimit)',
        'アップロードの控え (UploadFileBridge)'
    )
    $wb.Worksheets.Item(1).Name = $sheetNames[0]
    for ($i = 1; $i -lt $sheetNames.Count; $i++) {
        $added = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
        $added.Name = $sheetNames[$i]
    }

    # ---------------------------------------------------------------- 説明
    $ws = $wb.Worksheets.Item('本パラメータシートについて')
    $row = Set-SheetHeading -Sheet $ws -Title 'BrowserGuard パラメータシート' -Lines @(
        '本パラメータシートは、BrowserGuard の設定項目を一元的に定義・管理するための文書です。',
        '機能ごとにシートを分けてあり、各シートの「値」列に設定内容を記入します。',
        '記入した内容は「エクスポート」シートのボタンから、BrowserGuard.json として出力できます。'
    )

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text '設定ファイルの配置'
    $row++
    foreach ($line in @(
        'インストーラーは設定ファイルをインストール先の BrowserGuard.json に配置します。',
        '出力したファイルでこれを置き換えると、次回のブラウザー起動から反映されます。',
        '配置先は HKLM\SOFTWARE\BrowserGuard の ConfigFile 値で決まります。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $row++

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text '記入のきまり'
    $row++
    foreach ($line in @(
        '・「値」列だけを編集してください。「内部パラメータ名」は JSON のキーと対応しており、変更すると出力できなくなります。',
        '・有効・無効はプルダウンから選びます。数値は 0 以上の整数です。',
        '・表の行が足りない場合は、表の最終行で Tab キーを押すか、表内で行を挿入して増やせます。',
        '・空欄の行は出力時に読み飛ばされるため、消し忘れの空行があっても問題ありません。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $row++

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text 'シートの一覧'
    $row++
    $toc = @(
        @('シート', '設定する内容'),
        @('監査ログ (NetLogger)', 'URL アクセス・閲覧・アップロード・ダウンロード・印刷の記録と、その保存先・送信先'),
        @('アップロード制御 (UploadGuard)', 'アップロードしてよいローカルファイルの拡張子とパス'),
        @('設定ページの遮断 (SettingPageFilter)', '閲覧を禁止する edge:// などの設定ページ'),
        @('起動時プログラム実行 (StartupLauncher)', 'ブラウザーの起動に合わせて実行するプログラム'),
        @('使用時間の制限 (UsageTimeLimit)', '連続利用時間の上限と、利用を許可する時間帯'),
        @('タブ数の上限 (TabCountLimit)', '同時に開いてよいタブの数'),
        @('アップロードの控え (UploadFileBridge)', 'アップロードしたファイルの控えを残す先と、その対象')
    )
    foreach ($entry in $toc) {
        $ws.Cells($row, 1).Value2 = $entry[0]
        $ws.Cells($row, 2).Value2 = $entry[1]
        if ($entry[0] -eq 'シート') {
            $ws.Range($ws.Cells($row, 1), $ws.Cells($row, 2)).Font.Bold = $true
        }
        $row++
    }
    $ws.Columns(1).ColumnWidth = 40
    $ws.Columns(2).ColumnWidth = 80

    # ------------------------------------------------------------ エクスポート
    $ws = $wb.Worksheets.Item('エクスポート')
    $row = Set-SheetHeading -Sheet $ws -Title 'エクスポート' -Lines @(
        '各シートに記入した内容を、BrowserGuard の設定ファイル (JSON) として書き出します。'
    )

    $button = $ws.Shapes.AddShape($msoShapeRoundedRectangle, 12, 68, 400, 48)
    $button.Name = 'ExportButton'
    $button.Fill.ForeColor.RGB = $COLOR_ACCENT
    $button.Line.Visible = $false
    $button.TextFrame2.TextRange.Text = 'パラメータシートの内容を設定ファイルとして出力する'
    $button.TextFrame2.TextRange.Font.Size = 12
    $button.TextFrame2.TextRange.Font.Bold = $true
    $button.TextFrame2.TextRange.Font.Fill.ForeColor.RGB = $COLOR_WHITE
    $button.TextFrame2.TextRange.ParagraphFormat.Alignment = 2  # msoAlignCenter
    $button.TextFrame2.VerticalAnchor = 3                       # msoAnchorMiddle
    $button.OnAction = 'ExportSetting'

    $row = 10
    foreach ($line in @(
        '出力先: このブックと同じ場所の BrowserGuard_export\<日時>\BrowserGuard.json',
        '',
        '実行するたびに日時のフォルダーを作るため、以前に出力したファイルは上書きされません。',
        '出力前に入力内容を確認し、問題があれば内容を表示して中断します。',
        '',
        'マクロが動かない場合は、ブックを開いたときに表示される「コンテンツの有効化」を押してください。',
        'ダウンロードしたファイルでボタンが反応しない場合は、エクスプローラーでブックのプロパティを開き、',
        '「セキュリティ: 許可する」にチェックを入れてから開き直してください。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $ws.Columns(1).ColumnWidth = 100

    # -------------------------------------------------------------- 通信ログ
    $ws = $wb.Worksheets.Item('監査ログ (NetLogger)')
    $row = Set-SheetHeading -Sheet $ws -Title '監査ログ (NetLogger)' -Lines @(
        'ブラウザーの操作を 1 行 1 JSON の監査ログとして記録します。',
        '記録したログは、この PC への保存と収集サーバーへの送信の、片方または両方に流せます。'
    )

    $netLoggerParams = @(
        (New-Param -Group '基本' -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の設定はすべて無視されます'),
        (New-Param -Group '記録対象' -Label 'URL アクセスを記録する' -Value '無効' -Name 'UrlAccess' -Kind bool),
        (New-Param -Group '記録対象' -Label 'ページの閲覧を記録する' -Value '無効' -Name 'Browsing' -Kind bool),
        (New-Param -Group '記録対象' -Label 'アップロードを記録する' -Value '無効' -Name 'Upload' -Kind bool),
        (New-Param -Group '記録対象' -Label 'ダウンロードを記録する' -Value '無効' -Name 'Download' -Kind bool),
        (New-Param -Group '記録対象' -Label '印刷を記録する' -Value '無効' -Name 'Print' -Kind bool),
        (New-Param -Group 'ローカル保存' -Label 'ログをこの PC に保存する' -Value '無効' -Name 'LocalFile.Enabled' -Kind bool),
        (New-Param -Group 'ローカル保存' -Label '保存先ディレクトリ' -Value '' -Name 'LocalFile.Directory' -Kind text -Note '空欄なら %LOCALAPPDATA%\BrowserGuard\netlog。%MACHINENAME% %USER% %DATE% %YYYY% %MM% %DD% と環境変数を展開します'),
        (New-Param -Group 'ローカル保存' -Label 'ログの保持日数' -Value 30 -Name 'LocalFile.MaxDays' -Kind int -Note '日付が変わるとファイルを切り替え、この日数を過ぎたものを消します。0 は無制限'),
        (New-Param -Group 'ローカル保存' -Label '1 日あたりのファイル最大サイズ (MB)' -Value 0 -Name 'LocalFile.MaxSizeMB' -Kind int -Note '超えた分は分割します。0 は分割しません'),
        (New-Param -Group '送信' -Label 'ログを収集サーバーに送信する' -Value '無効' -Name 'Sender.Enabled' -Kind bool),
        (New-Param -Group '送信' -Label '送信先エンドポイント' -Value '' -Name 'Sender.Endpoint' -Kind text -Note '送信を有効にする場合は必須です (例: https://collector.example.jp/netlog)'),
        (New-Param -Group '保留' -Label '送信できなかったログを保留する' -Value '無効' -Name 'Sender.Spool.Enabled' -Kind bool -Note '無効の場合、送信できなかったログはその場で失われます'),
        (New-Param -Group '保留' -Label '保留ファイルの最大サイズ (MB)' -Value 10 -Name 'Sender.Spool.MaxSizeMB' -Kind int -Note '超えた分は古いものから捨てます。0 は無制限'),
        (New-Param -Group '再送' -Label '保留したログを再送する' -Value '有効' -Name 'Sender.Spool.Retry.Enabled' -Kind bool -Note '無効にした場合、保留したログは手作業で回収することになります'),
        (New-Param -Group '再送' -Label '再送の間隔 (分)' -Value 5 -Name 'Sender.Spool.Retry.IntervalMinutes' -Kind int)
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_NetLogger' -Params $netLoggerParams -WithGroup | Out-Null
    $ws.Columns(1).ColumnWidth = 14
    $ws.Columns(2).ColumnWidth = 38
    $ws.Columns(3).ColumnWidth = 46
    $ws.Columns(4).ColumnWidth = 34
    $ws.Columns(5).ColumnWidth = 100

    # ------------------------------------------------------ アップロード制限
    $ws = $wb.Worksheets.Item('アップロード制御 (UploadGuard)')
    $row = Set-SheetHeading -Sheet $ws -Title 'アップロード制御 (UploadGuard)' -Lines @(
        'アップロードしてよいローカルファイルを、拡張子とパスで絞り込みます。',
        'ブロックの一覧を先に見るため、両方に該当するものはブロックされます。',
        '許可の一覧が空の場合、その条件では絞り込みません (拡張子の許可が空なら、ブロック以外のすべての拡張子が通ります)。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_UploadGuard' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の一覧はすべて無視されます')
    ) | Out-Null

    # 一覧は A 列と C 列に置く。パラメータ表と列を共有するため、
    # 列幅がどちらにも合うように 4 列だけで組む。
    $listRow = $row + 4
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text '拡張子'
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 1 -Name 'L_UploadGuard_BlockedExtensions' `
        -Header 'ブロックする拡張子' -Items @('.exe', '.bat', '.cmd', '.js', '.vbs') -BlankRows 5 | Out-Null
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 3 -Name 'L_UploadGuard_AllowedExtensions' `
        -Header '許可する拡張子' -BlankRows 10 | Out-Null

    $listRow = $listRow + 13
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text 'パス (正規表現)'
    $ws.Cells($listRow + 1, 1).Value2 = 'ファイルのフルパスに対して照合します。区切りの \ は \\ と書きます。'
    $ws.Cells($listRow + 1, 1).Font.Color = $COLOR_MUTED
    New-ListTable -Sheet $ws -Row ($listRow + 2) -Column 1 -Name 'L_UploadGuard_BlockedPaths' `
        -Header 'ブロックするパス' -BlankRows 8 | Out-Null
    New-ListTable -Sheet $ws -Row ($listRow + 2) -Column 3 -Name 'L_UploadGuard_AllowedPaths' `
        -Header '許可するパス' -BlankRows 8 | Out-Null

    $ws.Columns(1).ColumnWidth = 40
    $ws.Columns(2).ColumnWidth = 14
    $ws.Columns(3).ColumnWidth = 32
    $ws.Columns(4).ColumnWidth = 70

    # -------------------------------------------------------- 設定ページ制限
    $ws = $wb.Worksheets.Item('設定ページの遮断 (SettingPageFilter)')
    $row = Set-SheetHeading -Sheet $ws -Title '設定ページの遮断 (SettingPageFilter)' -Lines @(
        'ブラウザーの設定ページを開けなくします。',
        '一覧の文字列で前方一致した URL がブロックの対象です。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_SettingPageFilter' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の一覧は無視されます'),
        (New-Param -Label 'ブロック時に通知を表示する' -Value '有効' -Name 'NotifyOnBlocked' -Kind bool -Note '無効にすると、何も表示せずページを閉じます')
    ) | Out-Null

    $listRow = $row + 5
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text 'ブロックする URL'
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 1 -Name 'L_SettingPageFilter_BlockedPrefixes' `
        -Header 'URL の先頭に一致する文字列' `
        -Items @('edge://settings', 'edge://extensions', 'edge://flags', 'edge://policy') -Width 44 | Out-Null

    $ws.Columns(1).ColumnWidth = 44
    $ws.Columns(2).ColumnWidth = 16
    $ws.Columns(3).ColumnWidth = 26
    $ws.Columns(4).ColumnWidth = 60

    # ------------------------------------------------------ 起動時プログラム
    $ws = $wb.Worksheets.Item('起動時プログラム実行 (StartupLauncher)')
    $row = Set-SheetHeading -Sheet $ws -Title '起動時プログラム実行 (StartupLauncher)' -Lines @(
        'ブラウザーの起動に合わせてプログラムを実行します。',
        '実行ファイルのパスは記入したとおりに使われます (環境変数は展開されません)。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_StartupLauncher' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の一覧は無視されます')
    ) | Out-Null

    $listRow = $row + 4
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text '実行するプログラム'
    foreach ($line in @(
        '「引数」と「環境変数」は 1 つにつき 1 行で書きます (セル内の改行は Alt + Enter)。環境変数は 名前=値 の形式です。',
        '「SHA-256」を記入すると、実行前にハッシュを照合し、一致しない場合は実行しません。空欄なら照合しません。')) {
        $ws.Cells($listRow + 1, 1).Value2 = $line
        $ws.Cells($listRow + 1, 1).Font.Color = $COLOR_MUTED
        $listRow++
    }

    $programsTable = New-Table -Sheet $ws -Row ($listRow + 2) -Column 1 -Name 'A_StartupLauncher_Programs' `
        -Headers @('実行ファイルのパス', '引数', '作業ディレクトリ', '環境変数', 'SHA-256') -RowCount 10
    $programsTable.DataBodyRange.NumberFormatLocal = '@'
    $programsTable.DataBodyRange.VerticalAlignment = -4160  # xlTop
    $programsTable.DataBodyRange.WrapText = $true

    $ws.Columns(1).ColumnWidth = 46
    $ws.Columns(2).ColumnWidth = 24
    $ws.Columns(3).ColumnWidth = 32
    $ws.Columns(4).ColumnWidth = 30
    $ws.Columns(5).ColumnWidth = 68

    # ---------------------------------------------------------- 利用時間制限
    $ws = $wb.Worksheets.Item('使用時間の制限 (UsageTimeLimit)')
    $row = Set-SheetHeading -Sheet $ws -Title '使用時間の制限 (UsageTimeLimit)' -Lines @(
        '連続して使える時間と、使ってよい時間帯を決めます。',
        'どちらかに反した時点で、下の「上限を超えたときの動作」を行います。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_UsageTimeLimit' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の設定はすべて無視されます'),
        (New-Param -Label '連続して使える時間の上限 (分)' -Value 0 -Name 'MaxContinuousMinutes' -Kind int -Note '0 は無制限'),
        (New-Param -Label '上限を超えたときの動作' -Value '警告のみ' -Name 'OnExceeded.Action' -Kind choice -Choices @('警告のみ', 'ブラウザーを終了する')),
        (New-Param -Label '終了するまでの猶予 (秒)' -Value 60 -Name 'OnExceeded.GraceSeconds' -Kind int -Note '「ブラウザーを終了する」場合に、警告してから終了するまでの時間'),
        (New-Param -Label '再警告の間隔 (分)' -Value 10 -Name 'OnExceeded.ReWarnIntervalMinutes' -Kind int -Note '「警告のみ」の場合に、警告を出し直す間隔')
    ) | Out-Null

    $listRow = $row + 8
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text '利用を許可する時間帯'
    foreach ($line in @(
        '24 時間表記の HH:mm で記入します。1 行も書かなければ時間帯では制限しません。',
        '終了が開始より後にならない場合は日をまたぐ指定です (22:00 - 02:00 なら 4 時間)。')) {
        $ws.Cells($listRow + 1, 1).Value2 = $line
        $ws.Cells($listRow + 1, 1).Font.Color = $COLOR_MUTED
        $listRow++
    }

    $rangesTable = New-Table -Sheet $ws -Row ($listRow + 2) -Column 1 -Name 'A_UsageTimeLimit_AllowedTimeRanges' `
        -Headers @('開始時刻', '終了時刻') -RowCount 10
    $rangesTable.DataBodyRange.NumberFormatLocal = '@'

    $ws.Columns(1).ColumnWidth = 32
    $ws.Columns(2).ColumnWidth = 16
    $ws.Columns(3).ColumnWidth = 34
    $ws.Columns(4).ColumnWidth = 70

    # ------------------------------------------------------------ タブ数制限
    $ws = $wb.Worksheets.Item('タブ数の上限 (TabCountLimit)')
    $row = Set-SheetHeading -Sheet $ws -Title 'タブ数の上限 (TabCountLimit)' -Lines @(
        '同時に開けるタブの数を制限します。',
        'タブはウィンドウをまたいで数えるため、別のウィンドウを開いても回避できません。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_TabCountLimit' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool),
        (New-Param -Label '同時に開けるタブの数' -Value 0 -Name 'MaxCount' -Kind int -Note '0 は無制限')
    ) | Out-Null

    $ws.Columns(1).ColumnWidth = 32
    $ws.Columns(2).ColumnWidth = 16
    $ws.Columns(3).ColumnWidth = 24
    $ws.Columns(4).ColumnWidth = 40

    # ------------------------------------------------------ アップロード保全
    $ws = $wb.Worksheets.Item('アップロードの控え (UploadFileBridge)')
    $row = Set-SheetHeading -Sheet $ws -Title 'アップロードの控え (UploadFileBridge)' -Lines @(
        'アップロードされたファイルの控えを、何が外に出たかの証跡として残します。',
        '保存先はこの PC ではなくファイルサーバー上を想定しています。',
        '一覧の読み方はアップロード制御と同じで、ブロックが優先し、空の許可一覧は絞り込みません。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_UploadFileBridge' -Params @(
        (New-Param -Label 'この機能を有効にする' -Value '無効' -Name 'Enabled' -Kind bool -Note '無効の場合、以下の設定はすべて無視されます'),
        (New-Param -Label '控えの保存先' -Value '' -Name 'Destination' -Kind text -Note '有効にする場合は必須です。%MACHINENAME% %USER% %DATE% %YYYY% %MM% %DD% と環境変数を展開します'),
        (New-Param -Label '控えを残すファイルの最大サイズ (MB)' -Value 0 -Name 'MaxSizeMB' -Kind int -Note 'これを超えるファイルは控えを残しません。0 は無制限')
    ) | Out-Null

    $listRow = $row + 6
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text '拡張子'
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 1 -Name 'L_UploadFileBridge_BlockedExtensions' `
        -Header '控えを残さない拡張子' -BlankRows 10 | Out-Null
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 3 -Name 'L_UploadFileBridge_AllowedExtensions' `
        -Header '控えを残す拡張子' -BlankRows 10 | Out-Null

    $listRow = $listRow + 13
    Set-SectionLabel -Sheet $ws -Row $listRow -Column 1 -Text 'アップロード先の URL (正規表現)'
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 1 -Name 'L_UploadFileBridge_BlockedUrls' `
        -Header '控えを残さない URL' -BlankRows 10 | Out-Null
    New-ListTable -Sheet $ws -Row ($listRow + 1) -Column 3 -Name 'L_UploadFileBridge_AllowedUrls' `
        -Header '控えを残す URL' -BlankRows 10 | Out-Null

    $ws.Columns(1).ColumnWidth = 40
    $ws.Columns(2).ColumnWidth = 14
    $ws.Columns(3).ColumnWidth = 32
    $ws.Columns(4).ColumnWidth = 70

    # ------------------------------------------------------------- 仕上げ
    foreach ($name in $sheetNames) {
        $sheet = $wb.Worksheets.Item($name)
        $sheet.Activate()
        $xl.ActiveWindow.DisplayGridlines = $false
        $sheet.Range('A1').Select() | Out-Null

        # 備考まで入れると横に広いので、印刷は横向きの幅 1 ページに収める。
        $sheet.PageSetup.Orientation = 2  # xlLandscape
        $sheet.PageSetup.Zoom = $false
        $sheet.PageSetup.FitToPagesWide = 1
        $sheet.PageSetup.FitToPagesTall = $false
        $sheet.PageSetup.CenterFooter = "$name  -  &P / &N"
    }

    $vbaCode = Get-Content -Raw -Encoding UTF8 $vbaPath
    $module = $wb.VBProject.VBComponents.Add($vbextCtStdModule)
    $module.Name = 'ExportConfig'
    $module.CodeModule.AddFromString($vbaCode)

    $wb.Worksheets.Item($sheetNames[0]).Activate()

    $outputDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }
    if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
    $wb.SaveAs($OutputPath, $xlOpenXMLWorkbookMacroEnabled)
    $wb.Close($false)

    Write-Host "作成しました: $OutputPath"
}
finally {
    $xl.ScreenUpdating = $true
    $xl.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
}
