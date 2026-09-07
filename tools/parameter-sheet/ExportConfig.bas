Option Explicit

' BrowserGuard パラメータシート: 設定ファイル (JSON) の出力
'
' 各機能シートの入力値を読み取り、Resources/BrowserGuard.json と同じ構造の
' JSON を組み立てて書き出します。値の参照はシート上のテーブル名で行うため、
' 行や列を挿入してもこのコードを直す必要はありません。
'
'   T_<機能名>            単一値のパラメータ (「内部パラメータ名」列で引く)
'   L_<機能名>_<項目名>   文字列リスト (1 列目のみを読む)
'   A_<機能名>_<項目名>   オブジェクトの配列 (列の並びで読む)

Private Const EXPORT_FOLDER_NAME As String = "BrowserGuard_export"
Private Const OUTPUT_FILE_NAME As String = "BrowserGuard.json"

Private Const VALUE_YES As String = "有効"

Private Const ACTION_WARN_LABEL As String = "警告のみ"
Private Const ACTION_TERMINATE_LABEL As String = "ブラウザーを終了する"

Private Const COL_PARAM_NAME As String = "内部パラメータ名"
Private Const COL_PARAM_VALUE As String = "値"


' ==========================================
' メインエントリ
' ==========================================
Public Sub ExportSetting()
    Dim problems As Collection
    Dim destFolder As String
    Dim outputPath As String
    Dim jsonText As String

    On Error GoTo ErrHandler

    Set problems = CollectProblems()
    If problems.Count > 0 Then
        MsgBox "入力内容に問題があります。修正してから再実行してください。" & vbCrLf & vbCrLf & _
               JoinCollection(problems, vbCrLf), vbExclamation, "入力エラー"
        Exit Sub
    End If

    If MsgBox(ThisWorkbook.Path & "\" & EXPORT_FOLDER_NAME & vbCrLf & _
              "に現在の設定を " & OUTPUT_FILE_NAME & " として出力します。よろしいですか？", _
              vbOKCancel + vbQuestion, "確認") <> vbOK Then Exit Sub

    jsonText = BuildJson()

    destFolder = GetOutputFolder()
    outputPath = destFolder & "\" & OUTPUT_FILE_NAME
    SaveTextFileUtf8 jsonText, outputPath

    MsgBox "出力しました。" & vbCrLf & vbCrLf & outputPath, vbInformation, "完了"
    Exit Sub

ErrHandler:
    MsgBox "出力に失敗しました。" & vbCrLf & vbCrLf & Err.Description, vbExclamation, "エラー"
End Sub


' ==========================================
' JSON の組み立て
' ==========================================
' Public にしてあるのは、Excel を開かずに出力内容を検証できるようにするため
' (tools\parameter-sheet\test-parameter-sheet.ps1 が Application.Run で呼びます)。
Public Function BuildJson() As String
    Dim s As String

    s = "{" & vbCrLf
    s = s & BuildNetLogger() & "," & vbCrLf
    s = s & BuildUploadGuard() & "," & vbCrLf
    s = s & BuildSettingPageFilter() & "," & vbCrLf
    s = s & BuildStartupLauncher() & "," & vbCrLf
    s = s & BuildUsageTimeLimit() & "," & vbCrLf
    s = s & BuildTabCountLimit() & "," & vbCrLf
    s = s & BuildUploadFileBridge() & vbCrLf
    s = s & "}" & vbCrLf

    BuildJson = s
End Function

Private Function BuildNetLogger() As String
    Const T As String = "T_NetLogger"
    Dim s As String

    s = "  ""NetLogger"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""UrlAccess"": " & JBool(ParamBool(T, "UrlAccess")) & "," & vbCrLf
    s = s & "    ""Browsing"": " & JBool(ParamBool(T, "Browsing")) & "," & vbCrLf
    s = s & "    ""Upload"": " & JBool(ParamBool(T, "Upload")) & "," & vbCrLf
    s = s & "    ""Download"": " & JBool(ParamBool(T, "Download")) & "," & vbCrLf
    s = s & "    ""Print"": " & JBool(ParamBool(T, "Print")) & "," & vbCrLf
    s = s & "    ""LocalFile"": {" & vbCrLf
    s = s & "      ""Enabled"": " & JBool(ParamBool(T, "LocalFile.Enabled")) & "," & vbCrLf
    s = s & "      ""Directory"": " & JStr(ParamText(T, "LocalFile.Directory")) & "," & vbCrLf
    s = s & "      ""MaxDays"": " & JNum(ParamLong(T, "LocalFile.MaxDays")) & "," & vbCrLf
    s = s & "      ""MaxSizeMB"": " & JNum(ParamLong(T, "LocalFile.MaxSizeMB")) & vbCrLf
    s = s & "    }," & vbCrLf
    s = s & "    ""Sender"": {" & vbCrLf
    s = s & "      ""Enabled"": " & JBool(ParamBool(T, "Sender.Enabled")) & "," & vbCrLf
    s = s & "      ""Endpoint"": " & JStr(ParamText(T, "Sender.Endpoint")) & "," & vbCrLf
    s = s & "      ""Spool"": {" & vbCrLf
    s = s & "        ""Enabled"": " & JBool(ParamBool(T, "Sender.Spool.Enabled")) & "," & vbCrLf
    s = s & "        ""MaxSizeMB"": " & JNum(ParamLong(T, "Sender.Spool.MaxSizeMB")) & "," & vbCrLf
    s = s & "        ""Retry"": {" & vbCrLf
    s = s & "          ""Enabled"": " & JBool(ParamBool(T, "Sender.Spool.Retry.Enabled")) & "," & vbCrLf
    s = s & "          ""IntervalMinutes"": " & JNum(ParamLong(T, "Sender.Spool.Retry.IntervalMinutes")) & vbCrLf
    s = s & "        }" & vbCrLf
    s = s & "      }" & vbCrLf
    s = s & "    }" & vbCrLf
    s = s & "  }"

    BuildNetLogger = s
End Function

Private Function BuildUploadGuard() As String
    Const T As String = "T_UploadGuard"
    Dim s As String

    s = "  ""UploadGuard"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""BlockedExtensions"": " & JStrArray(ListValues("L_UploadGuard_BlockedExtensions"), 4) & "," & vbCrLf
    s = s & "    ""AllowedExtensions"": " & JStrArray(ListValues("L_UploadGuard_AllowedExtensions"), 4) & "," & vbCrLf
    s = s & "    ""AllowedPaths"": " & JStrArray(ListValues("L_UploadGuard_AllowedPaths"), 4) & "," & vbCrLf
    s = s & "    ""BlockedPaths"": " & JStrArray(ListValues("L_UploadGuard_BlockedPaths"), 4) & vbCrLf
    s = s & "  }"

    BuildUploadGuard = s
End Function

Private Function BuildSettingPageFilter() As String
    Const T As String = "T_SettingPageFilter"
    Dim s As String

    s = "  ""SettingPageFilter"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""NotifyOnBlocked"": " & JBool(ParamBool(T, "NotifyOnBlocked")) & "," & vbCrLf
    s = s & "    ""BlockedPrefixes"": " & JStrArray(ListValues("L_SettingPageFilter_BlockedPrefixes"), 4) & vbCrLf
    s = s & "  }"

    BuildSettingPageFilter = s
End Function

Private Function BuildStartupLauncher() As String
    Const T As String = "T_StartupLauncher"
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim s As String
    Dim body As String
    Dim isFirst As Boolean

    Set lo = GetTable("A_StartupLauncher_Programs")
    Set targetRows = NonEmptyRows(lo, 1)

    s = "  ""StartupLauncher"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf

    If targetRows.Count = 0 Then
        s = s & "    ""Programs"": []" & vbCrLf
    Else
        isFirst = True
        For Each r In targetRows
            If Not isFirst Then body = body & "," & vbCrLf
            isFirst = False
            body = body & "      {" & vbCrLf
            body = body & "        ""Path"": " & JStr(CellText(lo, CLng(r), 1)) & "," & vbCrLf
            body = body & "        ""Arguments"": " & JStrArray(SplitLines(CellText(lo, CLng(r), 2)), 8) & "," & vbCrLf
            body = body & "        ""WorkingDirectory"": " & JStr(CellText(lo, CLng(r), 3)) & "," & vbCrLf
            body = body & "        ""EnvironmentVariables"": " & JEnvObject(CellText(lo, CLng(r), 4), 8) & "," & vbCrLf
            body = body & "        ""Sha256"": " & JStr(CellText(lo, CLng(r), 5)) & vbCrLf
            body = body & "      }"
        Next r
        s = s & "    ""Programs"": [" & vbCrLf & body & vbCrLf & "    ]" & vbCrLf
    End If

    s = s & "  }"
    BuildStartupLauncher = s
End Function

Private Function BuildUsageTimeLimit() As String
    Const T As String = "T_UsageTimeLimit"
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim s As String
    Dim body As String
    Dim isFirst As Boolean

    Set lo = GetTable("A_UsageTimeLimit_AllowedTimeRanges")
    Set targetRows = NonEmptyRows(lo, 1)

    s = "  ""UsageTimeLimit"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""MaxContinuousMinutes"": " & JNum(ParamLong(T, "MaxContinuousMinutes")) & "," & vbCrLf

    If targetRows.Count = 0 Then
        s = s & "    ""AllowedTimeRanges"": []," & vbCrLf
    Else
        isFirst = True
        For Each r In targetRows
            If Not isFirst Then body = body & "," & vbCrLf
            isFirst = False
            body = body & "      {" & vbCrLf
            body = body & "        ""Start"": " & JStr(TimeText(lo, CLng(r), 1)) & "," & vbCrLf
            body = body & "        ""End"": " & JStr(TimeText(lo, CLng(r), 2)) & vbCrLf
            body = body & "      }"
        Next r
        s = s & "    ""AllowedTimeRanges"": [" & vbCrLf & body & vbCrLf & "    ]," & vbCrLf
    End If

    s = s & "    ""OnExceeded"": {" & vbCrLf
    s = s & "      ""Action"": " & JStr(ActionValue(ParamText(T, "OnExceeded.Action"))) & "," & vbCrLf
    s = s & "      ""GraceSeconds"": " & JNum(ParamLong(T, "OnExceeded.GraceSeconds")) & "," & vbCrLf
    s = s & "      ""ReWarnIntervalMinutes"": " & JNum(ParamLong(T, "OnExceeded.ReWarnIntervalMinutes")) & vbCrLf
    s = s & "    }" & vbCrLf
    s = s & "  }"

    BuildUsageTimeLimit = s
End Function

Private Function BuildTabCountLimit() As String
    Const T As String = "T_TabCountLimit"
    Dim s As String

    s = "  ""TabCountLimit"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""MaxCount"": " & JNum(ParamLong(T, "MaxCount")) & vbCrLf
    s = s & "  }"

    BuildTabCountLimit = s
End Function

Private Function BuildUploadFileBridge() As String
    Const T As String = "T_UploadFileBridge"
    Dim s As String

    s = "  ""UploadFileBridge"": {" & vbCrLf
    s = s & "    ""Enabled"": " & JBool(ParamBool(T, "Enabled")) & "," & vbCrLf
    s = s & "    ""Destination"": " & JStr(ParamText(T, "Destination")) & "," & vbCrLf
    s = s & "    ""MaxSizeMB"": " & JNum(ParamLong(T, "MaxSizeMB")) & "," & vbCrLf
    s = s & "    ""BlockedExtensions"": " & JStrArray(ListValues("L_UploadFileBridge_BlockedExtensions"), 4) & "," & vbCrLf
    s = s & "    ""AllowedExtensions"": " & JStrArray(ListValues("L_UploadFileBridge_AllowedExtensions"), 4) & "," & vbCrLf
    s = s & "    ""BlockedUrls"": " & JStrArray(ListValues("L_UploadFileBridge_BlockedUrls"), 4) & "," & vbCrLf
    s = s & "    ""AllowedUrls"": " & JStrArray(ListValues("L_UploadFileBridge_AllowedUrls"), 4) & vbCrLf
    s = s & "  }"

    BuildUploadFileBridge = s
End Function


' ==========================================
' 入力チェック
' ==========================================
' BuildJson と同じ理由で Public。問題がなければ空文字を返します。
Public Function ValidationMessage() As String
    ValidationMessage = JoinCollection(CollectProblems(), vbLf)
End Function

Private Function CollectProblems() As Collection
    Dim problems As New Collection
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim startText As String
    Dim endText As String

    CheckLong problems, "T_NetLogger", "LocalFile.MaxDays", "監査ログ"
    CheckLong problems, "T_NetLogger", "LocalFile.MaxSizeMB", "監査ログ"
    CheckLong problems, "T_NetLogger", "Sender.Spool.MaxSizeMB", "監査ログ"
    CheckLong problems, "T_NetLogger", "Sender.Spool.Retry.IntervalMinutes", "監査ログ"
    CheckLong problems, "T_UsageTimeLimit", "MaxContinuousMinutes", "使用時間の制限"
    CheckLong problems, "T_UsageTimeLimit", "OnExceeded.GraceSeconds", "使用時間の制限"
    CheckLong problems, "T_UsageTimeLimit", "OnExceeded.ReWarnIntervalMinutes", "使用時間の制限"
    CheckLong problems, "T_TabCountLimit", "MaxCount", "タブ数の上限"
    CheckLong problems, "T_UploadFileBridge", "MaxSizeMB", "アップロードの控え"

    If ParamBool("T_NetLogger", "Sender.Enabled") And ParamText("T_NetLogger", "Sender.Endpoint") = "" Then
        problems.Add "・監査ログ: 送信を有効にする場合は送信先エンドポイントが必要です。"
    End If

    If ParamBool("T_UploadFileBridge", "Enabled") And ParamText("T_UploadFileBridge", "Destination") = "" Then
        problems.Add "・アップロードの控え: 有効にする場合は保存先が必要です。"
    End If

    If ActionValue(ParamText("T_UsageTimeLimit", "OnExceeded.Action")) = "" Then
        problems.Add "・使用時間の制限: 「上限を超えたときの動作」は「" & ACTION_WARN_LABEL & _
                     "」または「" & ACTION_TERMINATE_LABEL & "」から選んでください。"
    End If

    Set lo = GetTable("A_StartupLauncher_Programs")
    Set targetRows = NonEmptyRows(lo, 0)
    For Each r In targetRows
        If CellText(lo, CLng(r), 1) = "" Then
            problems.Add "・起動時プログラム実行: " & r & " 行目に実行ファイルのパスがありません。"
        End If
    Next r

    Set lo = GetTable("A_UsageTimeLimit_AllowedTimeRanges")
    Set targetRows = NonEmptyRows(lo, 0)
    For Each r In targetRows
        startText = TimeText(lo, CLng(r), 1)
        endText = TimeText(lo, CLng(r), 2)
        If Not IsHourMinute(startText) Or Not IsHourMinute(endText) Then
            problems.Add "・使用時間の制限: " & r & " 行目の時刻は HH:mm 形式で入力してください。"
        End If
    Next r

    Set CollectProblems = problems
End Function

Private Sub CheckLong(ByVal problems As Collection, ByVal tableName As String, _
                      ByVal paramName As String, ByVal sheetLabel As String)
    Dim v As Variant

    v = ParamRaw(tableName, paramName)
    If Trim$(CStr(v & "")) = "" Then Exit Sub

    If Not IsNumeric(v) Then
        problems.Add "・" & sheetLabel & ": " & paramName & " には数値を入力してください。"
    ElseIf CDbl(v) < 0 Or CDbl(v) <> Int(CDbl(v)) Then
        problems.Add "・" & sheetLabel & ": " & paramName & " には 0 以上の整数を入力してください。"
    End If
End Sub

Private Function IsHourMinute(ByVal s As String) As Boolean
    Dim hourPart As String
    Dim minutePart As String

    If Len(s) <> 5 Or Mid$(s, 3, 1) <> ":" Then Exit Function

    hourPart = Left$(s, 2)
    minutePart = Right$(s, 2)
    If Not IsNumeric(hourPart) Or Not IsNumeric(minutePart) Then Exit Function

    IsHourMinute = (CLng(hourPart) <= 23 And CLng(minutePart) <= 59)
End Function


' ==========================================
' シートの読み取り
' ==========================================
Private Function GetTable(ByVal tableName As String) As ListObject
    Dim ws As Worksheet
    Dim lo As ListObject

    For Each ws In ThisWorkbook.Worksheets
        For Each lo In ws.ListObjects
            If StrComp(lo.Name, tableName, vbTextCompare) = 0 Then
                Set GetTable = lo
                Exit Function
            End If
        Next lo
    Next ws

    Err.Raise vbObjectError + 513, , "テーブルが見つかりません: " & tableName
End Function

Private Function ParamRaw(ByVal tableName As String, ByVal paramName As String) As Variant
    Dim lo As ListObject
    Dim nameCol As Long
    Dim valueCol As Long
    Dim i As Long

    Set lo = GetTable(tableName)
    If lo.DataBodyRange Is Nothing Then
        Err.Raise vbObjectError + 514, , tableName & " が空です。"
    End If

    nameCol = lo.ListColumns(COL_PARAM_NAME).Index
    valueCol = lo.ListColumns(COL_PARAM_VALUE).Index

    For i = 1 To lo.DataBodyRange.Rows.Count
        If Trim$(CStr(lo.DataBodyRange.Cells(i, nameCol).Value & "")) = paramName Then
            ParamRaw = lo.DataBodyRange.Cells(i, valueCol).Value
            Exit Function
        End If
    Next i

    Err.Raise vbObjectError + 515, , tableName & " に " & paramName & " の行がありません。"
End Function

Private Function ParamText(ByVal tableName As String, ByVal paramName As String) As String
    ParamText = Trim$(CStr(ParamRaw(tableName, paramName) & ""))
End Function

Private Function ParamBool(ByVal tableName As String, ByVal paramName As String) As Boolean
    ParamBool = (ParamText(tableName, paramName) = VALUE_YES)
End Function

Private Function ParamLong(ByVal tableName As String, ByVal paramName As String) As Long
    Dim v As Variant

    v = ParamRaw(tableName, paramName)
    If Trim$(CStr(v & "")) = "" Then Exit Function
    ParamLong = CLng(v)
End Function

' 1 列目だけを読むリスト。空欄の行は飛ばす。
Private Function ListValues(ByVal tableName As String) As Collection
    Dim lo As ListObject
    Dim result As New Collection
    Dim i As Long
    Dim s As String

    Set lo = GetTable(tableName)
    If Not lo.DataBodyRange Is Nothing Then
        For i = 1 To lo.DataBodyRange.Rows.Count
            s = Trim$(CStr(lo.DataBodyRange.Cells(i, 1).Value & ""))
            If s <> "" Then result.Add s
        Next i
    End If

    Set ListValues = result
End Function

' どこか 1 つでも埋まっている行の番号。keyCol が 0 なら全列を見る。
Private Function NonEmptyRows(ByVal lo As ListObject, ByVal keyCol As Long) As Collection
    Dim result As New Collection
    Dim i As Long
    Dim j As Long
    Dim used As Boolean

    If lo.DataBodyRange Is Nothing Then
        Set NonEmptyRows = result
        Exit Function
    End If

    For i = 1 To lo.DataBodyRange.Rows.Count
        If keyCol > 0 Then
            used = (Trim$(CStr(lo.DataBodyRange.Cells(i, keyCol).Value & "")) <> "")
        Else
            used = False
            For j = 1 To lo.ListColumns.Count
                If Trim$(CStr(lo.DataBodyRange.Cells(i, j).Value & "")) <> "" Then used = True
            Next j
        End If
        If used Then result.Add i
    Next i

    Set NonEmptyRows = result
End Function

Private Function CellText(ByVal lo As ListObject, ByVal rowIndex As Long, ByVal colIndex As Long) As String
    CellText = Trim$(CStr(lo.DataBodyRange.Cells(rowIndex, colIndex).Value & ""))
End Function

' 時刻はシリアル値で入ることもあるので、どちらでも HH:mm に揃える。
Private Function TimeText(ByVal lo As ListObject, ByVal rowIndex As Long, ByVal colIndex As Long) As String
    Dim v As Variant

    v = lo.DataBodyRange.Cells(rowIndex, colIndex).Value
    If Trim$(CStr(v & "")) = "" Then Exit Function

    If IsNumeric(v) Then
        TimeText = Format$(CDate(v), "hh:nn")
    Else
        TimeText = Trim$(CStr(v))
    End If
End Function

Private Function ActionValue(ByVal label As String) As String
    Select Case label
        Case ACTION_WARN_LABEL: ActionValue = "WarnOnly"
        Case ACTION_TERMINATE_LABEL: ActionValue = "Terminate"
    End Select
End Function

' セル内改行で区切られた値。空行は捨てる。
Private Function SplitLines(ByVal text As String) As Collection
    Dim result As New Collection
    Dim parts() As String
    Dim i As Long

    If Trim$(text) <> "" Then
        parts = Split(Replace(Replace(text, vbCrLf, vbLf), vbCr, vbLf), vbLf)
        For i = LBound(parts) To UBound(parts)
            If Trim$(parts(i)) <> "" Then result.Add Trim$(parts(i))
        Next i
    End If

    Set SplitLines = result
End Function

Private Function JoinCollection(ByVal items As Collection, ByVal delimiter As String) As String
    Dim item As Variant
    Dim s As String

    For Each item In items
        If s <> "" Then s = s & delimiter
        s = s & CStr(item)
    Next item

    JoinCollection = s
End Function


' ==========================================
' JSON の値
' ==========================================
Private Function JBool(ByVal value As Boolean) As String
    JBool = IIf(value, "true", "false")
End Function

Private Function JNum(ByVal value As Long) As String
    JNum = CStr(value)
End Function

Private Function JStr(ByVal value As String) As String
    Dim i As Long
    Dim ch As String
    Dim code As Long
    Dim out As String

    For i = 1 To Len(value)
        ch = Mid$(value, i, 1)
        Select Case ch
            Case """": out = out & "\"""
            Case "\": out = out & "\\"
            Case vbLf: out = out & "\n"
            Case vbCr: out = out & "\r"
            Case vbTab: out = out & "\t"
            Case Else
                ' AscW は &H8000 以降を負で返すため、制御文字と混ざらないよう補正する。
                code = AscW(ch)
                If code < 0 Then code = code + 65536
                If code < 32 Or code = &H7F Then
                    out = out & "\u" & Right$("000" & Hex$(code), 4)
                Else
                    out = out & ch
                End If
        End Select
    Next i

    JStr = """" & out & """"
End Function

' indentSpaces は "キー": [ を書いている行の字下げ。
Private Function JStrArray(ByVal items As Collection, ByVal indentSpaces As Long) As String
    Dim i As Long
    Dim s As String

    If items.Count = 0 Then
        JStrArray = "[]"
        Exit Function
    End If

    s = "[" & vbCrLf
    For i = 1 To items.Count
        s = s & Space$(indentSpaces + 2) & JStr(CStr(items(i)))
        If i < items.Count Then s = s & ","
        s = s & vbCrLf
    Next i
    s = s & Space$(indentSpaces) & "]"

    JStrArray = s
End Function

' NAME=VALUE を 1 行ずつ書いたセルを JSON オブジェクトにする。
Private Function JEnvObject(ByVal text As String, ByVal indentSpaces As Long) As String
    Dim sourceLines As Collection
    Dim sourceLine As Variant
    Dim keys As New Collection
    Dim values As New Collection
    Dim p As Long
    Dim i As Long
    Dim s As String

    Set sourceLines = SplitLines(text)
    For Each sourceLine In sourceLines
        p = InStr(CStr(sourceLine), "=")
        If p > 1 Then
            keys.Add Trim$(Left$(CStr(sourceLine), p - 1))
            values.Add Trim$(Mid$(CStr(sourceLine), p + 1))
        End If
    Next sourceLine

    If keys.Count = 0 Then
        JEnvObject = "{}"
        Exit Function
    End If

    s = "{" & vbCrLf
    For i = 1 To keys.Count
        s = s & Space$(indentSpaces + 2) & JStr(CStr(keys(i))) & ": " & JStr(CStr(values(i)))
        If i < keys.Count Then s = s & ","
        s = s & vbCrLf
    Next i
    s = s & Space$(indentSpaces) & "}"

    JEnvObject = s
End Function


' ==========================================
' ファイル出力
' ==========================================
Private Function GetOutputFolder() As String
    Dim basePath As String
    Dim stampedPath As String

    If ThisWorkbook.Path = "" Then
        Err.Raise vbObjectError + 516, , "先にこのブックを保存してください。"
    End If

    basePath = ThisWorkbook.Path & "\" & EXPORT_FOLDER_NAME
    EnsureFolder basePath

    stampedPath = basePath & "\" & Format$(Now(), "yyyymmdd_hhnnss")
    EnsureFolder stampedPath

    GetOutputFolder = stampedPath
End Function

Private Sub EnsureFolder(ByVal folderPath As String)
    If Dir(folderPath, vbDirectory) = "" Then MkDir folderPath
End Sub

' ADODB.Stream の UTF-8 は BOM を付けるため、バイナリに移して 3 バイト落とす。
Private Sub SaveTextFileUtf8(ByVal content As String, ByVal filePath As String)
    Dim textStream As Object
    Dim binaryStream As Object

    Set textStream = CreateObject("ADODB.Stream")
    textStream.Type = 2
    textStream.Charset = "UTF-8"
    textStream.Open
    textStream.WriteText content
    textStream.Position = 0
    textStream.Type = 1
    textStream.Position = 3

    Set binaryStream = CreateObject("ADODB.Stream")
    binaryStream.Type = 1
    binaryStream.Open
    textStream.CopyTo binaryStream
    binaryStream.SaveToFile filePath, 2
    binaryStream.Close
    textStream.Close
End Sub
