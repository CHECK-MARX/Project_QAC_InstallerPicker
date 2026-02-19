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

## CI Code Signing (Authenticode)
- Workflow: `.github/workflows/ci.yml`
- Main push requires signing secrets. If missing, CI fails intentionally.
- Pull requests run build/publish, and signing is skipped when secrets are unavailable.

Required GitHub repository secrets:
- `WINDOWS_SIGN_CERT_BASE64`: base64 text of your `.pfx` code-signing certificate
- `WINDOWS_SIGN_CERT_PASSWORD`: password for the `.pfx`

Optional GitHub repository variable:
- `WINDOWS_SIGN_TIMESTAMP_URL`: RFC3161 timestamp URL
  - Default: `http://timestamp.digicert.com`

PowerShell helper (create base64 from PFX):
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\codesign.pfx")) | Set-Clipboard
```
