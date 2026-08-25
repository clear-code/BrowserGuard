# BrowserGuard

Microsoft Edge 向けのブラウザー拡張機能と、それと通信するネイティブメッセージングホスト、
および両者をまとめて配布するインストーラーです。

| ディレクトリ | 内容 |
| --- | --- |
| `BrowserGuard/` | ネイティブメッセージングホスト (C# / .NET 8) |
| `BrowserGuard.Tests/` | ホストの単体テスト (xUnit) |
| `webextensions/edge/` | Edge 拡張機能 (Manifest V3) |
| `Resources/` | インストーラーが配置する設定ファイルとマニフェスト |
| `BrowserGuard.iss` | Inno Setup のインストーラー定義 |

## 必要なもの

| ツール | 用途 | 備考 |
| --- | --- | --- |
| .NET SDK 8 以降 | ホストのビルド | プロジェクトの対象は `net8.0` |
| Node.js 20 以降 | 拡張機能の lint とパッケージング | |
| Microsoft Edge | crx の署名・パッケージング | `msedge.exe --pack-extension` を使用 |
| Inno Setup 6 | インストーラーのコンパイル | `ISCC.exe` |

## 署名鍵の配置

**リリースビルドの前に、所定の署名鍵を `webextensions\pem\edge.pem` に配置してください。**

この鍵から拡張機能 ID が決まります。鍵が違うと ID も変わり、[BrowserGuard.iss](BrowserGuard.iss)と
[Resources/manifest.xml](Resources/manifest.xml) に記載された ID と一致しなくなるため、インストーラーのポリシー登録が機能しません。

鍵はリポジトリに含ていないため、組織で管理しているものを都度配置してください。
配置されていない場合、ルートの `make.bat` は何もせずエラー終了します。

## 一括ビルド

リポジトリのルートで実行します。

```bash
make.bat
```

以下を順に行います。途中で失敗した場合はそこで停止します。

1. ネイティブメッセージングホストの publish
2. 拡張機能の lint と zip 作成
3. crx の署名・作成
4. インストーラーのコンパイル

### 生成物

| パス | 内容 |
| --- | --- |
| `SetupOutput\BrowserGuardSetup.exe` | インストーラー |
| `BrowserGuard\bin\Release\net8.0\publish\win-x64\` | ホスト (自己完結型 / win-x64) |
| `webextensions\BrowserGuardEdge.zip` | 拡張機能 (製品版) |
| `webextensions\BrowserGuardEdgeDev.zip` | 拡張機能 (開発版・名称が異なる) |
| `webextensions\BrowserGuardEdge.crx` | 署名済み拡張機能 (インストーラーに同梱) |
| `webextensions\edge\dev\` | 開発版の未パッケージ版 |

## 個別のビルド

### ネイティブメッセージングホスト

```bash
dotnet publish BrowserGuard\BrowserGuard.csproj -p:PublishProfile=FolderProfile
```

出力先は `BrowserGuard\bin\Release\net8.0\publish\win-x64\` です。
このパスは [BrowserGuard.iss](BrowserGuard.iss) の `[Files]` が参照しているため、変更する場合は両方を合わせてください。

### 拡張機能

`webextensions` ディレクトリで実行します。

```bash
make.bat help
```

| ターゲット | 内容 |
| --- | --- |
| `all` (既定) | `deps` → `lint` → `test` → `clean` → パッケージ作成 |
| `deps` | `npm install` (未インストール時のみ) |
| `lint` | ESLint と JSON の構文チェック |
| `test` | 単体テスト (`node --test`) |
| `format` | ESLint `--fix` |
| `clean` | 生成物の削除 |
| `package` | lint を行わずパッケージのみ作成 |
| `crx` | crx の作成 |

`crx` は `pem\edge.pem` があればそれで署名し、拡張機能 ID が固定されます。
無い場合は Edge が使い捨ての鍵を生成するため、ID がビルドごとに変わります
(警告は出ますが処理は続行します。動作確認用の挙動です)。

開発中に Edge へ読み込む場合は、`edge\dev\` を「パッケージ化されていない拡張機能を読み込む」で指定してください。

### インストーラー

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" BrowserGuard.iss
```

crx と zip を先に作っておく必要があります。

## テスト

```bash
dotnet test BrowserGuard.Tests\BrowserGuard.Tests.csproj
```

拡張機能側の単体テストは `webextensions\test\` にあります。

```bash
cd webextensions && make.bat test
```

Node 標準のテストランナー (`node --test`) を使うため、追加の依存はありません。
`make.bat all` からも実行されます。

ブラウザーの API に触れない部分だけを対象にしています。たとえば
[upload-guard.js](webextensions/edge/upload-guard.js) は `init()` から設定適用を
`applyConfig()` に切り出してあるので、ブラウザーなしで判定ロジックを検証できます。

### 仮の ID で Edge に登録して動作を確認する

リリース用の署名鍵を使わずに、拡張機能とネイティブメッセージングホストの動作を
実機で確認するためのスクリプトです。

```powershell
.\tools\install-test-extension.ps1
```

以下を行います。

1. 初回実行時にテスト用の署名鍵を生成 (**製品版とは異なる拡張機能 ID** になります)
2. 開発版を crx にパッケージングし、更新マニフェストを生成
3. `ExtensionSettings` に登録
4. ネイティブメッセージングホストを `-c Debug` でビルド
5. `bin\Debug\net8.0\BrowserGuard.exe` を指すホストマニフェストを生成して登録
6. テスト用の設定ファイルを生成し、ホストがそれを読むよう登録

HKLM を書き換えるため、**管理者権限の PowerShell** で実行してください。

#### 変更を Edge に反映させる

強制インストールされた拡張機能は、**Edge が更新チェックを実行したときにだけ**新しいビルドに入れ替わります。
Edge は起動直後ではなく数分後にチェックし、以降は数時間おきになるため、
再起動しただけでは古いビルドが動き続けることがあります。

すぐに反映させるには次の操作を行います。

1. `edge://extensions` を開く
2. 「開発者モード」をオンにする
3. 「更新」を押す

反映されたかどうかは、`edge://extensions` に表示されるバージョンで判断できます。
スクリプトは実行するたびに `1.0.<日数>.<分>` 形式の新しいバージョンを埋め込むため、
最後に出力されたバージョンと一致していれば新しいビルドが動いています。
`1.0.0` のままであれば古いビルドのままです。

| コマンド | 内容 |
| --- | --- |
| `install-test-extension.ps1` | ビルドと登録 |
| `install-test-extension.ps1 -RestartEdge` | 登録後に Edge を再起動する |
| `install-test-extension.ps1 -WhatIf` | レジストリを変更せずに内容を確認 |
| `install-test-extension.ps1 -Uninstall` | 登録を解除し、元のレジストリ値に戻す |
| `install-test-extension.ps1 -Uninstall -Purge` | 生成物とテスト鍵も削除 (次回は別の ID になります) |

`-RestartEdge` は Edge のプロセスを終了して起動し直すだけで、プロファイルには触れません。
未保存の作業は失われます。`-Uninstall` と組み合わせることもできます。

`-Purge` を使うと拡張機能 ID が変わります。`edge://extensions` や `edge://policy` で
確認する際は、スクリプトが出力した ID と一致しているか注意してください。

生成物は `.testinstall\` に置かれます (git 管理外)。
`-WhatIf` はレジストリ変更のみを抑止し、crx などの生成は実際に行います。

#### ネイティブメッセージングホストのデバッグ

ホストマニフェストの `path` が `bin\Debug\net8.0\BrowserGuard.exe` を指すため、
ホストを修正したら再ビルドするだけで反映されます。スクリプトの再実行は不要です。

```bash
dotnet build BrowserGuard\BrowserGuard.csproj -c Debug
```

Edge はメッセージのたびにホストを起動するため、再ビルド後の最初のメッセージから
新しいバイナリが使われます。動作は `%APPDATA%\BrowserGuard\BrowserGuard.log` で確認できます。

有効にする機能は `.testinstall\BrowserGuard.json` を編集して切り替えます。

#### 既存の登録の扱い

ネイティブメッセージングホストの登録名は 1 つしかないため、
**製品版がインストール済みの場合はその登録を置き換えます**。
置き換える前の値は `.testinstall\registry-backup.json` に保存し、
`-Uninstall` で元に戻します (設定ファイルのパスも同様)。

ホストマニフェストの `allowed_origins` にはテスト用と製品版の両方の拡張機能 ID を
書き込むため、製品版の拡張機能も引き続きホストと通信できます。

`ExtensionSettings` は全拡張機能をひとつの JSON 値で表すため、
既存の内容を読み込んでこの拡張機能のメンバーだけを追加・更新します。
他の拡張機能の設定には触れません。

## インストーラーの動作

### 拡張機能の強制インストール

インストーラーは crx と更新マニフェストを `{app}\BrowserGuardExtension` に配置します。
配置するだけでは Edge は拡張機能をインストールしないため、
Edge のポリシー `ExtensionSettings` への登録が別途必要です。

タスク選択画面のチェックボックスでこの登録を行えますが、**既定はオフ**です。
グループポリシーで管理する運用を標準とし、レジストリの直接書き込みは
明示的に選択した場合のみ行うためです。

チェックを入れた場合、以下に登録します。

```
HKLM\SOFTWARE\Policies\Microsoft\Edge
  ExtensionSettings (REG_SZ) =
    {"<拡張機能ID>":{"installation_mode":"force_installed",
     "update_url":"file:///<インストール先>/BrowserGuardExtension/manifest.xml",
     "override_update_url":true}}
```

`override_update_url` が必要な理由は、`ExtensionInstallForcelist` の update_url が
初回インストールにしか使われず、更新時には拡張機能自身の `manifest.json` の
`update_url` が参照されるためです。自己ホストのビルドにはそれが無いため、
`ExtensionSettings` でこの URL を更新時にも使わせています。

`ExtensionSettings` は全拡張機能をひとつの JSON 値で表すため、
既存の内容を読み込んでこの拡張機能のメンバーだけを追加・削除します。
他の拡張機能の設定は保持し、空になった場合のみ値ごと削除します。
JSON の操作は `BrowserGuard.exe policy` サブコマンドが行います
([PolicyCommand.cs](BrowserGuard/PolicyCommand.cs))。

アンインストール時は、このインストーラーが登録した場合にのみ削除します
(手動設定やグループポリシー由来のエントリは残します)。

グループポリシーで同じポリシーが構成されている環境では、
`gpupdate` のたびにグループポリシー側の設定で上書きされます。
実際に適用されている設定は `edge://policy` で確認してください
(`gpedit.msc` はグループポリシーが配信した内容のみを表示するため、
レジストリに直接書き込んだ内容は表示されません)。

### サイレントインストール

```bash
SetupOutput\BrowserGuardSetup.exe /VERYSILENT
```

この場合もポリシー登録は行われません。登録するには明示的に指定します。

```bash
SetupOutput\BrowserGuardSetup.exe /VERYSILENT /TASKS="extensionpolicy"
```

### 設定ファイル

インストーラーは [Resources/BrowserGuard.json](Resources/BrowserGuard.json) を `{app}\BrowserGuard.json` に配置します。
`onlyifdoesntexist` 指定のため、既に存在する場合は上書きしません (アップグレード時に設定が保持されます)。

各機能の有効・無効はこのファイルで切り替えます。初期状態ではすべて無効です。

## 補足

`webextensions/Makefile` は Linux 環境向けの旧ビルド定義です。
現在の Windows でのビルド手順には対応しておらず (crx ターゲットが無く、
ESLint 9 で廃止されたオプションを使用しています)、メンテナンスされていません。

`.github/workflows/webextension.yml` は CI 用で、`windows-latest` 上で
拡張機能の lint・zip・crx を作成します。CI では署名鍵を配置しないため、
生成される crx の拡張機能 ID は毎回変わります。動作確認用と位置づけてください。
