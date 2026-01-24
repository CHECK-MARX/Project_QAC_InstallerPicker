# QACInstallerPicker

[![CI Build](https://github.com/CHECK-MARX/Project_QAC_InstallerPicker/actions/workflows/ci.yml/badge.svg)](https://github.com/CHECK-MARX/Project_QAC_InstallerPicker/actions/workflows/ci.yml)

.NET 8（WPF + WinForms）で作成した Windows デスクトップアプリです。

## 前提
- Windows
- .NET SDK 8.x

## ビルド
```bash
dotnet build
```

## 実行
```bash
dotnet run --project QACInstallerPicker.App
```

## 公開（Publish）
```bash
dotnet publish QACInstallerPicker.App -c Release -r win-x64
```

## 構成
- QACInstallerPicker.App/（アプリ本体）
- Project_QACInstallerPicker.sln（ソリューション）

## 補足
- 実行時に参照するデータファイル: `Settings.json`、`Data/synonyms.json`
