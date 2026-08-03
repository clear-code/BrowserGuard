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
配置されていない場合、ルートの `build.bat` は何もせずエラー終了します。

## 一括ビルド

リポジトリのルートで実行します。

```bash
build.bat
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
| `BrowserGuard\bin\Release\net8.0\publish\win-x86\` | ホスト (自己完結型 / win-x86) |
| `webextensions\BrowserGuardEdge.zip` | 拡張機能 (製品版) |
| `webextensions\BrowserGuardEdgeDev.zip` | 拡張機能 (開発版・名称が異なる) |
| `webextensions\BrowserGuardEdge.crx` | 署名済み拡張機能 (インストーラーに同梱) |
| `webextensions\edge\dev\` | 開発版の未パッケージ版 |

## 個別のビルド

### ネイティブメッセージングホスト

```bash
dotnet publish BrowserGuard\BrowserGuard.csproj -p:PublishProfile=FolderProfile
```

出力先は `BrowserGuard\bin\Release\net8.0\publish\win-x86\` です。
このパスは [BrowserGuard.iss](BrowserGuard.iss) の `[Files]` が参照しているため、変更する場合は両方を合わせてください。

### 拡張機能

`webextensions` ディレクトリで実行します。

```bash
build.bat help
```

| ターゲット | 内容 |
| --- | --- |
| `all` (既定) | `deps` → `lint` → `clean` → パッケージ作成 |
| `deps` | `npm install` (未インストール時のみ) |
| `lint` | ESLint と JSON の構文チェック |
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

拡張機能側に単体テストはありません。`build.bat lint` が静的チェックを行います。

## インストーラーの動作

### 拡張機能の強制インストール

インストーラーは crx と更新マニフェストを `{app}\BrowserGuardExtension` に配置します。
配置するだけでは Edge は拡張機能をインストールしないため、
Edge のポリシー `ExtensionInstallForcelist` への登録が別途必要です。

タスク選択画面のチェックボックスでこの登録を行えますが、**既定はオフ**です。
グループポリシーで管理する運用を標準とし、レジストリの直接書き込みは
明示的に選択した場合のみ行うためです。

チェックを入れた場合、以下に登録します。

```
HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist
  <空き番号> = <拡張機能ID>;file:///<インストール先>/BrowserGuardExtension/manifest.xml
```

同じ拡張機能 ID のエントリが既にあればそれを更新し、重複登録はしません。
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
SetupOutput\BrowserGuardSetup.exe /VERYSILENT /TASKS="forcelist"
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
