# QAC インストーラ選定ツール (QACInstallerPicker)

Helix QAC の配布作業を効率化するための Windows デスクトップアプリです。  
Excel 対応表と共有フォルダスキャン結果を使って、必要インストーラの選定・転送・履歴管理を行います。

## 主な機能

- 対応表（Excel）読み込み
  - Helix バージョンをタブ表示
  - モジュール対応可否（対応 / 非対応）判定
  - Validate / Dashboard のルール判定
- 共有スキャン
  - `*.exe / *.msi / *.zip / *.sh / *.run` を解析
  - ZIP 内ヘッダ確認（Windows 配布物候補化）
  - 論理アイテムと実体ファイルを分離表示
  - 同一キーで zip 優先
- 選定
  - モジュール選択、OS 選択、プレビュー
  - メモ貼り付けから候補抽出（シノニム辞書）
  - カスタム選定（追加ファイル、圧縮登録）
- 転送
  - キュー追加、進捗表示
  - Pause / Resume / Cancel / Retry
  - `.part` によるレジューム
  - SHA-256 検証（ソース / ローカル）
- 履歴
  - SQLite にバッチ・アイテム履歴保存
  - CSV 出力
- 送付履歴（Excel 追記）
  - 選定内容を送付履歴タブに自動反映
  - 個人名 / 区分を入力して記入
  - 複数候補を一括記入
  - 記入対象チェック（デフォルト全 ON）
  - 記入後に送付日降順（新しい日付が先頭）で並び替え
  - フィルター条件を自動解除して全件表示で保存

## ソリューション構成

- `Project_QACInstallerPicker.sln`
- `QACInstallerPicker.App/`
  - WPF (.NET 8)
  - MVVM (CommunityToolkit.Mvvm)
  - SQLite (Microsoft.Data.Sqlite)
  - Excel 操作 (ClosedXML)

## 必須環境

- Windows 10 / 11
- .NET SDK 8.x

## NuGet

- `ClosedXML` 0.102.2
- `CommunityToolkit.Mvvm` 8.2.2
- `Microsoft.Data.Sqlite` 8.0.4

## ビルド / 実行

### ビルド

```powershell
dotnet build Project_QACInstallerPicker.sln -c Debug
```

### 実行

```powershell
dotnet run --project .\QACInstallerPicker.App\QACInstallerPicker.App.csproj
```

### Single File 発行（Release）

```powershell
dotnet publish .\QACInstallerPicker.App\QACInstallerPicker.App.csproj -c Release -r win-x64
```

出力例:

- `QACInstallerPicker.App\bin\Release\net8.0-windows\win-x64\publish\QACInstallerPicker.App.exe`

## 初期設定

アプリ起動後、`設定` 画面で以下を設定します。

- 対応表(Excel)パス
- UNC ルート
- 出力ベース
- 送付履歴 Excel パス
- 同時実行数

## 送付履歴 Excel 仕様

- ヘッダー（A〜J）は以下順で利用します。
  - A: 送付日
  - B: 会社名
  - C: 個人名
  - D: 区分
  - E: Helixバージョン
  - F: コード
  - G: 名称
  - H: 対応表版数
  - I: 選択OS
  - J: インストーラ名
- タイトル行が上にある形式でも、ヘッダー行を自動検出して追記します。
- 既存テーブル（例: `SendHistory`）がある場合はテーブル範囲を拡張して記入します。

## CI (GitHub Actions)

- Workflow: `.github/workflows/ci.yml`
- `main` への push で CI が動作

## 注意点

- Excel が開きっぱなしだと書き込みエラーになります。
- SmartScreen 警告を恒久的に減らすにはコード署名（Authenticode）が必要です。
