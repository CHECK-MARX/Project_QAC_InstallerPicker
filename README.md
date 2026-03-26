# QAC インストーラ選定ツール (QACInstallerPicker)

Helix QAC の配布作業を効率化するための Windows デスクトップアプリです。  
対応表 (Excel)・共有ディレクトリスキャン結果・カスタムファイル選定を組み合わせて、転送キュー作成、実ファイル転送、履歴管理、送付履歴記録までを一括で行えます。

## 主な機能

- 対応表 (Excel) 読み込み
  - Helix バージョンごとのモジュール対応判定
  - モジュール名/版数/OS 選択の支援
  - Validate / Dashboard のルール判定を適用
- 共有スキャン
  - `*.exe / *.msi / *.zip / *.sh / *.run` を走査
  - ZIP 内の実体確認 (Windows 配布物推定)
  - 論理アイテムと実体ファイルの分離表示
- 選定
  - モジュール選択 (全選択/全解除)
  - カスタム選定タブ (タブ追加・編集・削除、ファイル/フォルダ追加)
  - プレビュー (選択一覧)、メール貼り付け用リスト作成
  - 選定履歴 (最新5件) の呼び出し
- 転送
  - キュー実行、進捗表示、Pause / Resume / Cancel / Retry
  - `.part` によるレジューム
  - SHA-256 ハッシュ検証 (コピー整合性確認)
  - 同時実行数の制御
- 履歴
  - 転送バッチ履歴の表示/削除/CSV 出力
- 送付入力
  - 選定結果を自動転記し、個人名/区分を入力して送付履歴 Excel に記録
  - 記入対象はチェックボックスで除外可能 (初期は全 ON)
- 送付履歴
  - 送付履歴 Excel を読み込み表示
  - 期間フィルター (全期間/1週間/1か月/1年、開始日/終了日)
  - 列ごとの複数選択フィルター (プルダウン + チェックボックス)
  - 新しい日付順で表示
  - 長い文字列は省略せず表示し、画面外は横スクロールで確認可能
- 設定 Excel 出力/取込
  - `一括設定` シートへ現在の選定内容を書き出し
  - 編集済み `一括設定` シートの取り込み

## 画面構成

- `選定` タブ
  - 日常運用の中心。選定→キュー追加までここで実施。
- `スキャン` タブ
  - 共有スキャン結果の詳細確認用。
- `転送` タブ
  - 実際のコピー実行・進捗監視。
- `履歴` タブ
  - 転送バッチ履歴の管理。
- `送付入力` タブ
  - 送付履歴 Excel への記入。
- `送付履歴` タブ
  - 記入済み送付履歴の閲覧とフィルタリング。

## 動作環境

- Windows 10 / 11
- .NET SDK 8.x（開発時）

## 使用ライブラリ (NuGet)

- `ClosedXML` 0.102.2
- `CommunityToolkit.Mvvm` 8.2.2
- `Microsoft.Data.Sqlite` 8.0.4

## ビルド / 実行

### ビルド (Debug)

```powershell
dotnet build Project_QACInstallerPicker.sln -c Debug
```

### 実行

```powershell
dotnet run --project .\QACInstallerPicker.App\QACInstallerPicker.App.csproj
```

### 発行 (Release, 単体 EXE)

```powershell
dotnet publish .\QACInstallerPicker.App\QACInstallerPicker.App.csproj -c Release -r win-x64
```

出力例:

```text
QACInstallerPicker.App\bin\Release\net8.0-windows\win-x64\publish\QACInstallerPicker.App.exe
```

## 初回設定

`設定` 画面で以下を入力します。

- 対応表 (Excel) パス
- UNC ルート
- 出力ベース
- 送付履歴 Excel
- 同時実行数

送付履歴 Excel は `.xlsx` のみ対応です。ファイル未存在や拡張子不一致は警告表示します。

## 送付履歴 Excel 仕様

送付履歴記録先 Excel は、A〜J 列に次のヘッダーを持つこと:

1. 送付日
2. 会社名
3. 個人名
4. 区分
5. Helixバージョン
6. コード
7. 名称
8. 対応表版数
9. 選択OS
10. インストーラ名

補足:

- 記入時は末尾に追加後、`送付日` 降順に並び替えます。
- AutoFilter を再設定します（既存テーブルにも対応）。
- ヘッダー位置は先頭 50 行以内を探索します。

## 設定・DB 保存先

アプリ設定/DB は既定で以下に保存されます。

```text
%LOCALAPPDATA%\QACInstallerPicker\
```

主なファイル:

- `Settings.json`（設定）
- `qacinstaller.db`（転送履歴/ハッシュキャッシュ）
- `Data\synonyms.json`（シノニム辞書）

## 更新時の推奨手順（設定引継ぎ）

1. アプリを終了
2. 新しいビルド一式を展開（旧ファイルを上書き）
3. 再起動して動作確認

`%LOCALAPPDATA%\QACInstallerPicker\` 配下を維持すれば、設定と DB は引き継がれます。  
通常は `qacinstaller.db` を差し替える必要はありません。

## CI (GitHub Actions)

- Workflow: `.github/workflows/ci.yml`
- `push / pull_request / manual` で実行
- Windows ビルド + publish + artifact 出力
- 署名用シークレット設定時は Authenticode 署名を実施

## 注意事項

- 実行中に EXE を上書きするとビルド/更新に失敗します。更新前に必ずアプリを終了してください。
- SmartScreen 警告の恒久対策はコード署名証明書による署名です。
- Excel が他プロセスで排他ロック中の場合、書き込みは失敗します。
