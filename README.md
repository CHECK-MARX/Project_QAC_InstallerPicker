# QACInstallerPicker

Helix QAC インストーラ選定・転送を支援する Windows デスクトップアプリです。  
本リポジトリは `.NET 8 / WPF` で実装されています。

## 前提

- Windows 10/11
- .NET SDK 8.x

## ビルド

```powershell
dotnet build Project_QACInstallerPicker.sln
```

## 起動

```powershell
dotnet run --project QACInstallerPicker.App
```

## 公開ビルド

```powershell
dotnet publish QACInstallerPicker.App -c Release -r win-x64
```

## 設定Excelの運用

アプリの `設定Excel出力` / `設定Excel取込` は、`一括設定` シートを中心に運用します。  
`カスタム候補` シートは候補値の参照用で、通常は編集しません。

### 一括設定シート構成

1. `■基本情報`
   - `会社名` のみ必須
2. `■インストーラ選択`
   - 列順:
     - `Helixバージョン`
     - `名称`
     - `コード`
     - `対応表版数`
     - `対応OS`
     - `選択OS`
     - `選択`
     - `対応`
3. `■カスタム選択`
   - 列順:
     - `タブ名`
     - `候補`
     - `選択`
     - `圧縮`
     - `圧縮名`
     - `フォルダ維持`
     - `列情報(JSON)`

### 入力ルール

- 赤字太字のセルはプルダウンで選択してください
- セクション見出し（`■...`）と列名は変更しないでください
- 行削除/列追加/列削除/列順変更は避けてください

## 設定ファイル

アプリ設定は以下に保存されます。

- `%LOCALAPPDATA%\\QACInstallerPicker\\Settings.json`
- `%LOCALAPPDATA%\\QACInstallerPicker\\qacinstaller.db`

## CI と署名

- Workflow: `.github/workflows/ci.yml`
- Windows 実行ファイルの恒久対応には Authenticode 署名が必要です

必要な Secrets:

- `WINDOWS_SIGN_CERT_BASE64`
- `WINDOWS_SIGN_CERT_PASSWORD`

任意:

- `WINDOWS_SIGN_TIMESTAMP_URL`（既定: `http://timestamp.digicert.com`）

