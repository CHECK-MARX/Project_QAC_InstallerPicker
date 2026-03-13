using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using ClosedXML.Excel;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class BulkSelectionExcelService
{
    private const string GuideSheetName = "README";
    private const string UnifiedInputSheetName = "一括設定";
    private const string BasicInfoSheetName = "基本情報";
    private const string InstallerSelectionSheetName = "インストーラ選択";
    private const string ScanSheetName = "スキャン選択";
    private const string CustomSelectionSheetName = "カスタム選択";
    private const string CustomCandidatesSheetName = "カスタム候補";
    private const string LegacyHelixSelectionSheetName = "Helix選択";
    private const string LegacyModuleSheetName = "モジュール選択";
    private const string LegacyCustomTabSheetName = "カスタムタブ";
    private const string LegacyCustomTabRowsSheetName = "カスタム行";
    private const string LegacyCustomZipSheetName = "カスタム圧縮";
    private const int ValidationRowLimit = 1000;
    private static readonly Regex VersionTokenRegex = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);

    public void ExportTemplate(string excelPath, BulkSelectionWorkbookModel model, BulkExcelTemplateOptions options)
    {
        options ??= new BulkExcelTemplateOptions();
        var customCandidates = BuildCustomCandidates(model.CustomTabStates);
        var selectedHelixVersions = model.SelectedHelixVersions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(options.ExportHelixVersion))
        {
            selectedHelixVersions = new List<string> { options.ExportHelixVersion.Trim() };
        }

        if (selectedHelixVersions.Count == 0 && !string.IsNullOrWhiteSpace(model.SelectedHelixVersion))
        {
            selectedHelixVersions.Add(model.SelectedHelixVersion);
        }

        var includedCustomTabNames = model.IncludedCustomTabNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var configuredCustomTabNames = (options.ExportCustomTabNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (configuredCustomTabNames.Count > 0)
        {
            includedCustomTabNames = configuredCustomTabNames;
        }

        if (includedCustomTabNames.Count == 0 && !string.IsNullOrWhiteSpace(model.SelectedCustomTabName))
        {
            includedCustomTabNames.Add(model.SelectedCustomTabName);
        }

        using var workbook = new XLWorkbook();
        WriteGuideSheet(workbook, options);
        WriteCustomCandidatesSheet(workbook, customCandidates);
        WriteUnifiedInputSheet(
            workbook,
            model,
            options,
            selectedHelixVersions,
            includedCustomTabNames,
            customCandidates);

        workbook.SaveAs(excelPath);
    }

    public BulkSelectionWorkbookModel ImportTemplate(string excelPath)
    {
        using var stream = new FileStream(
            excelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var workbook = new XLWorkbook(stream);

        var result = new BulkSelectionWorkbookModel();

        if (workbook.TryGetWorksheet(BasicInfoSheetName, out var basicInfoSheet))
        {
            result.HasBasicInfoSection = true;
            ReadBasicInfoSheet(basicInfoSheet, result);
        }

        if (workbook.TryGetWorksheet(UnifiedInputSheetName, out var unifiedInputSheet))
        {
            workbook.TryGetWorksheet(CustomCandidatesSheetName, out var customCandidatesForUnified);
            ReadUnifiedInputSheet(unifiedInputSheet, customCandidatesForUnified, result);
            return result;
        }

        var selectedHelixVersions = new List<string>();
        if (workbook.TryGetWorksheet(InstallerSelectionSheetName, out var installerSheet))
        {
            result.HasModuleSelectionSection = true;
            result.ModuleSelections = ReadModuleSheet(installerSheet, selectedHelixVersions);
            selectedHelixVersions = result.ModuleSelections
                .Select(row => row.HelixVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (selectedHelixVersions.Count > 0)
            {
                result.SelectedHelixVersions = selectedHelixVersions;
                result.SelectedHelixVersion = selectedHelixVersions[0];
            }
        }
        else
        {
            // Backward compatibility for old templates.
            if (workbook.TryGetWorksheet(LegacyHelixSelectionSheetName, out var legacyHelixSelectionSheet))
            {
                selectedHelixVersions = ReadHelixSelectionSheet(legacyHelixSelectionSheet);
                if (selectedHelixVersions.Count > 0)
                {
                    result.SelectedHelixVersions = selectedHelixVersions;
                    result.SelectedHelixVersion = selectedHelixVersions[0];
                }
            }

            if (workbook.TryGetWorksheet(LegacyModuleSheetName, out var legacyModuleSheet))
            {
                result.HasModuleSelectionSection = true;
                result.ModuleSelections = ReadModuleSheet(legacyModuleSheet, selectedHelixVersions);
            }
        }

        if (workbook.TryGetWorksheet(ScanSheetName, out var scanSheet))
        {
            result.HasScanSelectionSection = true;
            result.ScanSelections = ReadScanSheet(scanSheet);
        }

        var hasCustomTabDefinition = workbook.TryGetWorksheet(LegacyCustomTabSheetName, out var customTabSheet);
        var hasCustomCandidates = workbook.TryGetWorksheet(CustomCandidatesSheetName, out var customCandidatesSheet);
        var hasCustomSelection = workbook.TryGetWorksheet(CustomSelectionSheetName, out var customSelectionSheet);
        var hasLegacyCustomRows = workbook.TryGetWorksheet(LegacyCustomTabRowsSheetName, out var legacyCustomRowsSheet);
        var customRowsSheet = hasCustomSelection ? customSelectionSheet : legacyCustomRowsSheet;
        if (hasCustomTabDefinition || hasCustomSelection || hasLegacyCustomRows || hasCustomCandidates)
        {
            result.HasCustomTabsSection = true;
            var readResult = ReadCustomTabs(customTabSheet, customCandidatesSheet, customRowsSheet, result);
            result.CustomTabStates = readResult.CustomTabStates;
            result.IncludedCustomTabNames = readResult.IncludedCustomTabNames;
            if (readResult.CustomZipPlans.Count > 0)
            {
                result.CustomZipPlans = readResult.CustomZipPlans;
                result.HasCustomZipPlansSection = true;
            }

            // Backward compatibility only: old workbook may still have a dedicated custom-zip sheet.
            if (workbook.TryGetWorksheet(LegacyCustomZipSheetName, out var customZipSheet))
            {
                var fromZipSheet = ReadCustomZipSheet(customZipSheet, readResult.CandidatesByDisplay);
                if (fromZipSheet.Count > 0)
                {
                    result.CustomZipPlans = MergeCustomZipPlans(result.CustomZipPlans, fromZipSheet);
                    result.HasCustomZipPlansSection = true;
                }
            }
        }
        else if (workbook.TryGetWorksheet(LegacyCustomZipSheetName, out var onlyCustomZipSheet))
        {
            var zipOnly = ReadCustomZipSheet(onlyCustomZipSheet, new Dictionary<string, CustomCandidateEntry>(StringComparer.OrdinalIgnoreCase));
            if (zipOnly.Count > 0)
            {
                result.CustomZipPlans = zipOnly;
                result.HasCustomZipPlansSection = true;
            }
        }

        return result;
    }

    private static void WriteGuideSheet(XLWorkbook workbook, BulkExcelTemplateOptions options)
    {
        var sheet = workbook.Worksheets.Add(GuideSheetName);
        var lines = new[]
        {
            "QACインストーラ選定ツール",
            "設定Excel 取込手順書",
            "",
            "1. 目的",
            "",
            "本Excelは、QACインストーラ選定ツールに取り込むための設定ファイルです。",
            "ツール上で個別に設定する代わりに、必要な内容をExcelへ入力し、「設定Excel取込」から一括で反映できます。",
            "",
            "このExcelでは、主に以下の内容を設定できます。",
            "",
            "・基本情報",
            "・インストーラ選択",
            "・対象OS",
            "・実ファイル版数",
            "・カスタム選択",
            "・圧縮設定",
            "",
            "2. 作業の流れ",
            "",
            "2.1 全体の流れ",
            "",
            "作業は、次の順で行います。",
            "",
            "① ツールで設定Excelを出力する",
            "② Excelに必要事項を入力する",
            "③ Excelを保存する",
            "④ ツールで設定Excelを取り込む",
            "",
            "2.2 使用するシート",
            "",
            "通常、編集するのは「一括設定」シートです。",
            "「カスタム候補」シートは候補値管理用のため、通常は編集しません。",
            "",
            "3. 設定Excelの作成と入力",
            "",
            "3.1 設定Excelを出力する",
            "",
            "(1) QACインストーラ選定ツールを起動します。",
            "(2) 「設定Excel出力」を押します。",
            "(3) Excelファイルを出力します。",
            "",
            "3.2 基本情報を入力する",
            "",
            "「■基本情報」で会社名を入力します。",
            "入力必須は会社名のみです。",
            "",
            "3.3 インストーラ選択を入力する",
            "",
            "「■インストーラ選択」で、対象にしたいモジュールを選択します。",
            "必要なものだけを選んでください。",
            "",
            "3.4 必要に応じて詳細項目を指定する",
            "",
            "必要に応じて、以下の項目を指定します。",
            "",
            "・選択OS",
            "・実ファイル版数",
            "",
            "OSを限定したい場合や、版数を明示したい場合に入力してください。",
            "",
            "3.5 カスタム選択を入力する",
            "",
            "「■カスタム選択」で必要なファイルを選択し、必要に応じて以下を指定します。",
            "",
            "・圧縮有無",
            "・圧縮名",
            "・フォルダ維持",
            "",
            "4. 取込手順",
            "",
            "4.1 保存する",
            "",
            "入力が完了したら、Excelを保存します。",
            "",
            "4.2 ツールに取り込む",
            "",
            "(1) ツールに戻ります。",
            "(2) 「設定Excel取込」を押します。",
            "(3) 保存したExcelを選択します。",
            "(4) 取込後、内容が正しく反映されていることを確認します。",
            "",
            "4.3 確認ポイント",
            "",
            "取込後は、少なくとも以下を確認してください。",
            "",
            "・会社名",
            "・インストーラ選択内容",
            "・OS選択内容",
            "・カスタム選択内容",
            "",
            "5. 入力ルール",
            "",
            "5.1 プルダウン項目は候補から選択する",
            "",
            "赤字太字のセルはプルダウン選択項目です。",
            "直接入力せず、必ず候補から選択してください。",
            "",
            "5.2 列名と見出しは変更しない",
            "",
            "以下は変更しないでください。",
            "",
            "・1行目の列名",
            "・セクション見出し（「■○○」）",
            "",
            "これらを変更すると、取込に失敗する場合があります。",
            "",
            "5.3 候補列は直接入力しない",
            "",
            "候補がある列は、見た目が同じでも直接入力すると取込できない場合があります。",
            "必ずプルダウンを使用してください。",
            "",
            "5.4 内部用シートは編集しない",
            "",
            "「カスタム候補」シートは内部用のため、通常は編集不要です。",
            "",
            "6. 注意事項",
            "",
            "6.1 行削除・列変更をしない",
            "",
            "以下の操作は行わないでください。",
            "",
            "・既存行の削除",
            "・列の並び変更",
            "・列の追加",
            "・列の削除",
            "",
            "Excelの構造が変わると、取込に失敗することがあります。",
            "",
            "6.2 迷った場合は再出力する",
            "",
            "入力内容や候補値に不安がある場合は、既存ファイルを修正し続けず、",
            "ツールから新しく設定Excelを出力して入力し直してください。",
            "",
            "6.3 古いExcelの流用に注意する",
            "",
            "過去に出力したExcelを流用すると、現在のツールの項目や候補と合わず、",
            "正常に取り込めない場合があります。"
        };

        for (var i = 0; i < lines.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = lines[i];
        }
        sheet.Column(1).Style.Alignment.WrapText = true;
        sheet.Column(1).Width = 120;
        sheet.Columns().AdjustToContents();
    }

    private static void WriteUnifiedInputSheet(
        XLWorkbook workbook,
        BulkSelectionWorkbookModel model,
        BulkExcelTemplateOptions options,
        IReadOnlyCollection<string> selectedHelixVersions,
        IReadOnlyCollection<string> includedCustomTabNames,
        IReadOnlyList<CustomCandidateEntry> candidates)
    {
        var sheet = workbook.Worksheets.Add(UnifiedInputSheetName);
        var row = 1;

        if (options.IncludeBasicInfo)
        {
            sheet.Cell(row, 1).Value = "■基本情報";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            sheet.Cell(row, 1).Value = "項目";
            sheet.Cell(row, 2).Value = "値";
            var basicHeader = sheet.Range(row, 1, row, 2);
            basicHeader.Style.Font.Bold = true;
            basicHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
            row++;

            sheet.Cell(row, 1).Value = "会社名";
            sheet.Cell(row, 2).Value = model.CompanyName;
            sheet.Cell(row, 2).Style.Font.FontColor = XLColor.Red;
            sheet.Cell(row, 2).Style.Font.Bold = true;
            row += 2;
        }

        if (options.IncludeModuleSelection)
        {
            sheet.Cell(row, 1).Value = "■インストーラ選択";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            var headerRow = row;
            sheet.Cell(headerRow, 1).Value = "Helixバージョン";
            sheet.Cell(headerRow, 2).Value = "名称";
            sheet.Cell(headerRow, 3).Value = "コード";
            sheet.Cell(headerRow, 4).Value = "対応表版数";
            sheet.Cell(headerRow, 5).Value = "対応OS";
            sheet.Cell(headerRow, 6).Value = "選択OS";
            sheet.Cell(headerRow, 7).Value = "選択";
            sheet.Cell(headerRow, 8).Value = "対応";
            var installerHeader = sheet.Range(headerRow, 1, headerRow, 8);
            installerHeader.Style.Font.Bold = true;
            installerHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
            row++;

            var selectedSet = selectedHelixVersions
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var installerRows = model.ModuleSelections
                .Where(module => selectedSet.Count == 0 || selectedSet.Contains(module.HelixVersion))
                .ToList();

            var installerStart = row;
            foreach (var module in installerRows)
            {
                sheet.Cell(row, 1).Value = module.HelixVersion;
                sheet.Cell(row, 2).Value = module.Name;
                sheet.Cell(row, 3).Value = module.Code;
                sheet.Cell(row, 4).Value = module.CompatibilityVersion;
                sheet.Cell(row, 5).Value = module.SupportedOsDisplay;
                sheet.Cell(row, 6).Value = string.IsNullOrWhiteSpace(module.OsSelection) ? "両方" : module.OsSelection;
                sheet.Cell(row, 7).Value = module.IsSelected ? "選択する" : "選択しない";
                sheet.Cell(row, 8).Value = string.IsNullOrWhiteSpace(module.SupportStatus) ? "対応" : module.SupportStatus;

                row++;
            }

            var installerEnd = Math.Max(installerStart, row - 1);
            var installerValidationEnd = Math.Max(installerEnd + 20, installerStart + 20);
            ApplyListValidation(sheet, installerStart, installerValidationEnd, 7, "\"選択する,選択しない\"");
            ApplyListValidation(sheet, installerStart, installerValidationEnd, 6, "\"両方,Windows,Linux\"");

            if (installerEnd >= installerStart)
            {
                var dropdownRange = sheet.Range(installerStart, 7, installerEnd, 7);
                dropdownRange.Style.Font.Bold = true;
                dropdownRange.Style.Font.FontColor = XLColor.Red;

                var osDropdownRange = sheet.Range(installerStart, 6, installerEnd, 6);
                osDropdownRange.Style.Font.Bold = true;
                osDropdownRange.Style.Font.FontColor = XLColor.Red;
            }
            row += 1;
        }

        if (options.IncludeCustomTabs)
        {
            sheet.Cell(row, 1).Value = "■カスタム選択";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            var headerRow = row;
            sheet.Cell(headerRow, 1).Value = "タブ名";
            sheet.Cell(headerRow, 2).Value = "候補";
            sheet.Cell(headerRow, 3).Value = "選択";
            sheet.Cell(headerRow, 4).Value = "圧縮";
            sheet.Cell(headerRow, 5).Value = "圧縮名";
            sheet.Cell(headerRow, 6).Value = "フォルダ維持";
            sheet.Cell(headerRow, 7).Value = "列情報(JSON)";
            var customHeader = sheet.Range(headerRow, 1, headerRow, 7);
            customHeader.Style.Font.Bold = true;
            customHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
            row++;

            var includedSet = includedCustomTabNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidateByPath = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourcePath))
                .GroupBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var zipByPath = BuildZipInfoByPath(model.CustomZipPlans ?? new List<CustomZipPlan>());

            var customStart = row;
            foreach (var state in model.CustomTabStates ?? new List<CustomTabState>())
            {
                if (includedSet.Count > 0 && !includedSet.Contains(state.Name))
                {
                    continue;
                }

                foreach (var customRow in state.Rows ?? new List<CustomTabRowState>())
                {
                    if (string.IsNullOrWhiteSpace(customRow.SourcePath))
                    {
                        continue;
                    }

                    var hasCandidate = candidateByPath.TryGetValue(customRow.SourcePath, out var candidate);
                    var candidateDisplay = hasCandidate
                        ? candidate!.DisplayKey
                        : BuildCandidateDisplay(state.Name, customRow.Folder, customRow.FileName);

                    zipByPath.TryGetValue(customRow.SourcePath, out var zipInfo);
                    var archiveName = zipInfo?.ArchiveBaseName ?? string.Empty;
                    var keepFolder = zipInfo?.IncludeFolderInArchive ?? true;

                    sheet.Cell(row, 1).Value = state.Name;
                    sheet.Cell(row, 2).Value = candidateDisplay;
                    sheet.Cell(row, 3).Value = customRow.IsSelected ? "選択する" : "選択しない";
                    sheet.Cell(row, 4).Value = zipInfo == null ? "圧縮しない" : "圧縮する";
                    sheet.Cell(row, 5).Value = archiveName;
                    sheet.Cell(row, 6).Value = keepFolder ? "維持" : "平坦";
                    sheet.Cell(row, 7).Value = JsonSerializer.Serialize(customRow.ColumnValues ?? new Dictionary<string, string>());
                    row++;
                }
            }

            var customEnd = Math.Max(customStart, row - 1);
            var customValidationEnd = Math.Max(customEnd + 20, customStart + 20);
            var tabNames = (model.CustomTabStates ?? new List<CustomTabState>())
                .Select(state => state.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tabFormula = $"\"{string.Join(",", tabNames)}\"";
            if (tabNames.Count > 0 && tabFormula.Length <= 250)
            {
                ApplyListValidation(sheet, customStart, customValidationEnd, 1, tabFormula);
            }

            ApplyListValidation(sheet, customStart, customValidationEnd, 3, "\"選択する,選択しない\"");
            if (candidates.Count > 0)
            {
                ApplyListValidation(sheet, customStart, customValidationEnd, 2, BuildSheetListReference(CustomCandidatesSheetName, 2, 1, candidates.Count + 1, 1));
            }

            ApplyListValidation(sheet, customStart, customValidationEnd, 4, "\"圧縮する,圧縮しない\"");
            ApplyListValidation(sheet, customStart, customValidationEnd, 6, "\"維持,平坦\"");

            if (customEnd >= customStart)
            {
                var selectRange = sheet.Range(customStart, 3, customEnd, 3);
                selectRange.Style.Font.Bold = true;
                selectRange.Style.Font.FontColor = XLColor.Red;

                var compressRange = sheet.Range(customStart, 4, customEnd, 4);
                compressRange.Style.Font.Bold = true;
                compressRange.Style.Font.FontColor = XLColor.Red;

                var keepFolderRange = sheet.Range(customStart, 6, customEnd, 6);
                keepFolderRange.Style.Font.Bold = true;
                keepFolderRange.Style.Font.FontColor = XLColor.Red;
            }
        }

        var lastRow = Math.Max(1, sheet.LastRowUsed()?.RowNumber() ?? 1);
        var lastCol = Math.Max(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 1);
        var usedRange = sheet.Range(1, 1, lastRow, lastCol);
        usedRange.Style.NumberFormat.Format = "@";
        usedRange.Style.Alignment.WrapText = false;
        usedRange.Style.Alignment.ShrinkToFit = false;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
    }

    private static void WriteBasicInfoSheet(XLWorkbook workbook, BulkSelectionWorkbookModel model)
    {
        var sheet = workbook.Worksheets.Add(BasicInfoSheetName);
        sheet.Cell(1, 1).Value = "項目";
        sheet.Cell(1, 2).Value = "値";

        var rows = new List<(string Key, object? Value)>
        {
            ("テンプレート版", model.TemplateVersion),
            ("会社名", model.CompanyName),
            ("Helixバージョン", model.SelectedHelixVersion),
            ("検索文字列", model.SearchText),
            ("メモ", model.MemoText),
            ("出力ベース", model.OutputBaseFolder),
            ("同時実行数", model.MaxConcurrentTransfers),
            ("選択中カスタムタブ", model.SelectedCustomTabName)
        };

        for (var i = 0; i < rows.Count; i++)
        {
            var row = i + 2;
            sheet.Cell(row, 1).Value = rows[i].Key;
            sheet.Cell(row, 2).Value = rows[i].Value?.ToString() ?? string.Empty;
        }

        StyleHeaderRow(sheet, 2);
    }

    private static void WriteInstallerSelectionSheet(
        XLWorkbook workbook,
        IReadOnlyList<BulkModuleSelectionRow> rows,
        IReadOnlyCollection<string> selectedHelixVersions)
    {
        var selectedSet = selectedHelixVersions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = rows
            .Where(row => selectedSet.Count == 0 ||
                          selectedSet.Contains(row.HelixVersion))
            .OrderByDescending(row => ExtractVersionToken(row.HelixVersion), Comparer<string>.Create(VersionUtil.CompareVersionLike))
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sheet = workbook.Worksheets.Add(InstallerSelectionSheetName);
        sheet.Cell(1, 1).Value = "選択";
        sheet.Cell(1, 2).Value = "Helixバージョン";
        sheet.Cell(1, 3).Value = "対応OS/リビジョン情報";
        sheet.Cell(1, 4).Value = "コード";
        sheet.Cell(1, 5).Value = "名称";
        sheet.Cell(1, 6).Value = "対応表版数";
        sheet.Cell(1, 7).Value = "選択OS";
        sheet.Cell(1, 8).Value = "実ファイル版数";

        var rowIndex = 2;
        foreach (var row in filtered)
        {
            sheet.Cell(rowIndex, 1).Value = row.IsSelected ? "選択する" : "選択しない";
            sheet.Cell(rowIndex, 2).Value = row.HelixVersion;
            sheet.Cell(rowIndex, 3).Value = ResolveHelixOsInfo(row.HelixVersion);
            sheet.Cell(rowIndex, 4).Value = row.Code;
            sheet.Cell(rowIndex, 5).Value = row.Name;
            sheet.Cell(rowIndex, 6).Value = row.CompatibilityVersion;
            sheet.Cell(rowIndex, 7).Value = string.IsNullOrWhiteSpace(row.OsSelection) ? "両方" : row.OsSelection;
            sheet.Cell(rowIndex, 8).Value = row.SelectedInstallerVersion;

            var versionOptions = (row.InstallerVersionOptions ?? new List<string>())
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (versionOptions.Count > 0)
            {
                // ClosedXML/Excel list formula has practical length limits.
                var formula = $"\"{string.Join(",", versionOptions)}\"";
                if (formula.Length <= 250)
                {
                    ApplyListValidation(sheet, rowIndex, rowIndex, 8, formula);
                }
            }

            rowIndex++;
        }

        StyleHeaderRow(sheet, 8);

        var validationLastRow = Math.Max(rowIndex + 20, ValidationRowLimit);
        ApplyListValidation(sheet, 2, validationLastRow, 1, "\"選択する,選択しない\"");
        ApplyListValidation(sheet, 2, validationLastRow, 7, "\"両方,Windows,Linux\"");
    }

    private static void WriteScanSheet(XLWorkbook workbook, IEnumerable<BulkScanSelectionRow> rows)
    {
        var sheet = workbook.Worksheets.Add(ScanSheetName);
        sheet.Cell(1, 1).Value = "選択";
        sheet.Cell(1, 2).Value = "コード";
        sheet.Cell(1, 3).Value = "版数";
        sheet.Cell(1, 4).Value = "OS";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.IsSelected ? "選択する" : "選択しない";
            sheet.Cell(rowIndex, 2).Value = row.Code;
            sheet.Cell(rowIndex, 3).Value = row.Version;
            sheet.Cell(rowIndex, 4).Value = row.Os;
            rowIndex++;
        }

        StyleHeaderRow(sheet, 4);
        var validationLastRow = Math.Max(rowIndex + 20, ValidationRowLimit);
        ApplyListValidation(sheet, 2, validationLastRow, 1, "\"選択する,選択しない\"");
    }
    private static void WriteCustomCandidatesSheet(XLWorkbook workbook, IReadOnlyList<CustomCandidateEntry> candidates)
    {
        var sheet = workbook.Worksheets.Add(CustomCandidatesSheetName);
        sheet.Cell(1, 1).Value = "候補";
        sheet.Cell(1, 2).Value = "タブ名";
        sheet.Cell(1, 3).Value = "フォルダ";
        sheet.Cell(1, 4).Value = "ファイル名";
        sheet.Cell(1, 5).Value = "ソースパス";
        sheet.Cell(1, 6).Value = "列情報(JSON)";

        var rowIndex = 2;
        foreach (var candidate in candidates)
        {
            sheet.Cell(rowIndex, 1).Value = candidate.DisplayKey;
            sheet.Cell(rowIndex, 2).Value = candidate.TabName;
            sheet.Cell(rowIndex, 3).Value = candidate.Folder;
            sheet.Cell(rowIndex, 4).Value = candidate.FileName;
            sheet.Cell(rowIndex, 5).Value = candidate.SourcePath;
            sheet.Cell(rowIndex, 6).Value = JsonSerializer.Serialize(candidate.ColumnValues);
            rowIndex++;
        }

        StyleHeaderRow(sheet, 6);
        sheet.Column(5).Hide();
        sheet.Visibility = XLWorksheetVisibility.Hidden;
    }

    private static void WriteCustomSelectionSheet(
        XLWorkbook workbook,
        IReadOnlyList<CustomTabState> customTabStates,
        IReadOnlyList<CustomZipPlan> customZipPlans,
        IReadOnlyList<CustomCandidateEntry> candidates,
        IReadOnlyCollection<string> includedCustomTabNames)
    {
        var includedSet = includedCustomTabNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sheet = workbook.Worksheets.Add(CustomSelectionSheetName);
        sheet.Cell(1, 1).Value = "タブ名";
        sheet.Cell(1, 2).Value = "選択";
        sheet.Cell(1, 3).Value = "候補";
        sheet.Cell(1, 4).Value = "圧縮";
        sheet.Cell(1, 5).Value = "圧縮名";
        sheet.Cell(1, 6).Value = "フォルダ維持";
        sheet.Cell(1, 7).Value = "列情報(JSON)";

        var candidateByPath = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourcePath))
            .GroupBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var zipByPath = BuildZipInfoByPath(customZipPlans);

        var rowIndex = 2;
        foreach (var state in customTabStates)
        {
            if (includedSet.Count > 0 && !includedSet.Contains(state.Name))
            {
                continue;
            }

            foreach (var row in state.Rows ?? new List<CustomTabRowState>())
            {
                if (string.IsNullOrWhiteSpace(row.SourcePath))
                {
                    continue;
                }

                var hasCandidate = candidateByPath.TryGetValue(row.SourcePath, out var candidate);
                var candidateDisplay = hasCandidate
                    ? candidate!.DisplayKey
                    : BuildCandidateDisplay(state.Name, row.Folder, row.FileName);

                zipByPath.TryGetValue(row.SourcePath, out var zipInfo);
                var archiveName = zipInfo?.ArchiveBaseName ?? string.Empty;
                var keepFolder = zipInfo?.IncludeFolderInArchive ?? true;

                sheet.Cell(rowIndex, 1).Value = state.Name;
                sheet.Cell(rowIndex, 2).Value = row.IsSelected ? "選択する" : "選択しない";
                sheet.Cell(rowIndex, 3).Value = candidateDisplay;
                sheet.Cell(rowIndex, 4).Value = zipInfo == null ? "圧縮しない" : "圧縮する";
                sheet.Cell(rowIndex, 5).Value = archiveName;
                sheet.Cell(rowIndex, 6).Value = keepFolder ? "維持" : "平坦";
                sheet.Cell(rowIndex, 7).Value = JsonSerializer.Serialize(row.ColumnValues ?? new Dictionary<string, string>());
                rowIndex++;
            }
        }

        StyleHeaderRow(sheet, 7);

        var validationLastRow = Math.Max(rowIndex + 20, ValidationRowLimit);
        var tabNames = customTabStates
            .Select(state => state.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tabFormula = $"\"{string.Join(",", tabNames)}\"";
        if (tabNames.Count > 0 && tabFormula.Length <= 250)
        {
            ApplyListValidation(sheet, 2, validationLastRow, 1, tabFormula);
        }

        ApplyListValidation(sheet, 2, validationLastRow, 2, "\"選択する,選択しない\"");
        if (candidates.Count > 0)
        {
            ApplyListValidation(sheet, 2, validationLastRow, 3, BuildSheetListReference(CustomCandidatesSheetName, 2, 1, candidates.Count + 1, 1));
        }

        ApplyListValidation(sheet, 2, validationLastRow, 4, "\"圧縮する,圧縮しない\"");
        ApplyListValidation(sheet, 2, validationLastRow, 6, "\"維持,平坦\"");
    }

    private static void StyleHeaderRow(IXLWorksheet sheet, int columns)
    {
        var headerRange = sheet.Range(1, 1, 1, columns);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDED");
        sheet.SheetView.FreezeRows(1);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (columns > 0 && lastRow > 0)
        {
            var usedRange = sheet.Range(1, 1, lastRow, columns);
            usedRange.Style.NumberFormat.Format = "@";
            usedRange.Style.Alignment.WrapText = false;
            usedRange.Style.Alignment.ShrinkToFit = false;
        }

        sheet.Columns(1, Math.Max(1, columns)).AdjustToContents();
    }

    private static void ApplyListValidation(IXLWorksheet sheet, int fromRow, int toRow, int column, string listFormula)
    {
        if (toRow < fromRow || string.IsNullOrWhiteSpace(listFormula))
        {
            return;
        }

        var validation = sheet.Range(fromRow, column, toRow, column).CreateDataValidation();
        validation.IgnoreBlanks = true;
        validation.InCellDropdown = true;
        validation.List(listFormula, true);
    }

    private static string BuildSheetListReference(string sheetName, int fromRow, int fromCol, int toRow, int toCol)
    {
        var fromAddress = XLHelper.GetColumnLetterFromNumber(fromCol) + fromRow;
        var toAddress = XLHelper.GetColumnLetterFromNumber(toCol) + toRow;
        return $"='{sheetName}'!${fromAddress}:${toAddress}";
    }

    private static List<string> ReadHelixSelectionSheet(IXLWorksheet sheet)
    {
        var headers = BuildHeaderMap(sheet);
        var hasListFormat = headers.ContainsKey("Helixバージョン");
        if (!hasListFormat)
        {
            var value = GetString(sheet.Cell(2, 2));
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : new List<string> { value };
        }

        var versionColumn = GetColumnIndex(headers, "Helixバージョン", 1);
        var selectColumn = GetColumnIndex(headers, "選択", 2);

        var selected = new List<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var version = GetString(sheet.Cell(row, versionColumn));
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            var selectValue = GetString(sheet.Cell(row, selectColumn));
            if (!ParseSelectState(selectValue, true))
            {
                continue;
            }

            if (!selected.Any(item => item.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(version);
            }
        }

        return selected;
    }
    private static void ReadBasicInfoSheet(IXLWorksheet sheet, BulkSelectionWorkbookModel model)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var row = 2; row <= lastRow; row++)
        {
            var key = GetString(sheet.Cell(row, 1));
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            map[key] = GetString(sheet.Cell(row, 2));
        }

        model.TemplateVersion = GetValue(map, "テンプレート版", model.TemplateVersion);
        model.CompanyName = GetValue(map, "会社名", model.CompanyName);
        model.SelectedHelixVersion = GetValue(map, "Helixバージョン", model.SelectedHelixVersion);
        model.SearchText = GetValue(map, "検索文字列", model.SearchText);
        model.MemoText = GetValue(map, "メモ", model.MemoText);
        model.OutputBaseFolder = GetValue(map, "出力ベース", model.OutputBaseFolder);
        model.SelectedCustomTabName = GetValue(map, "選択中カスタムタブ", model.SelectedCustomTabName);
        model.MaxConcurrentTransfers = ParseInt(GetValue(map, "同時実行数", model.MaxConcurrentTransfers.ToString()), 2);
    }

    private static void ReadUnifiedInputSheet(
        IXLWorksheet sheet,
        IXLWorksheet? customCandidatesSheet,
        BulkSelectionWorkbookModel model)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var moduleRows = new List<BulkModuleSelectionRow>();
        var states = new Dictionary<string, CustomTabState>(StringComparer.OrdinalIgnoreCase);
        var candidatesByDisplay = ReadCustomCandidates(customCandidatesSheet, states);
        var candidatesByNormalizedDisplay = candidatesByDisplay
            .Values
            .GroupBy(candidate => NormalizeCandidateDisplay(candidate.DisplayKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var zipAccumulator = new Dictionary<string, List<CustomZipPlanItem>>(StringComparer.OrdinalIgnoreCase);
        var includedCustomTabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 1; row <= lastRow; row++)
        {
            var marker = GetString(sheet.Cell(row, 1));
            if (!IsUnifiedSectionTitle(marker))
            {
                continue;
            }

            if (marker.StartsWith("■基本情報", StringComparison.Ordinal))
            {
                model.HasBasicInfoSection = true;
                for (var r = row + 1; r <= lastRow; r++)
                {
                    var key = GetString(sheet.Cell(r, 1));
                    if (IsUnifiedSectionTitle(key))
                    {
                        break;
                    }

                    if (key.Equals("会社名", StringComparison.OrdinalIgnoreCase))
                    {
                        model.CompanyName = GetString(sheet.Cell(r, 2));
                    }
                }
            }
            else if (marker.StartsWith("■インストーラ選択", StringComparison.Ordinal))
            {
                model.HasModuleSelectionSection = true;
                var headerRow = row + 1;
                var headers = BuildHeaderMap(sheet, headerRow);
                var helixColumn = GetColumnIndex(headers, "Helixバージョン", 1);
                var nameColumn = GetColumnIndex(headers, "名称", 2);
                var codeColumn = GetColumnIndex(headers, "コード", 3);
                var compatibilityVersionColumn = GetColumnIndex(headers, "対応表版数", 4);
                var supportedOsColumn = GetColumnIndex(headers, "対応OS", 5);
                var osSelectionColumn = GetColumnIndex(headers, "選択OS", 6);
                var selectColumn = GetColumnIndex(headers, "選択", 7);
                var supportColumn = GetColumnIndex(headers, "対応", 8);
                var installerVersionColumn = GetColumnIndex(headers, "実ファイル版数", 0);

                var dataRow = row + 2;
                for (; dataRow <= lastRow; dataRow++)
                {
                    var first = GetString(sheet.Cell(dataRow, 1));
                    if (IsUnifiedSectionTitle(first))
                    {
                        break;
                    }

                    var code = GetString(sheet.Cell(dataRow, codeColumn));
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    var module = new BulkModuleSelectionRow
                    {
                        IsSelected = ParseSelectState(
                            GetString(sheet.Cell(dataRow, selectColumn)),
                            ParseBool(sheet.Cell(dataRow, selectColumn))),
                        Code = code,
                        Name = GetString(sheet.Cell(dataRow, nameColumn)),
                        CompatibilityVersion = GetString(sheet.Cell(dataRow, compatibilityVersionColumn)),
                        SupportedOsDisplay = GetString(sheet.Cell(dataRow, supportedOsColumn)),
                        OsSelection = GetString(sheet.Cell(dataRow, osSelectionColumn)),
                        SupportStatus = GetString(sheet.Cell(dataRow, supportColumn)),
                        SelectedInstallerVersion = installerVersionColumn > 0
                            ? GetString(sheet.Cell(dataRow, installerVersionColumn))
                            : string.Empty,
                        HelixVersion = GetString(sheet.Cell(dataRow, helixColumn))
                    };

                    if (!string.IsNullOrWhiteSpace(module.HelixVersion))
                    {
                        moduleRows.Add(module);
                    }
                }

                row = dataRow - 1;
            }
            else if (marker.StartsWith("■カスタム選択", StringComparison.Ordinal))
            {
                model.HasCustomTabsSection = true;
                var headerRow = row + 1;
                var headers = BuildHeaderMap(sheet, headerRow);
                var tabColumn = GetColumnIndex(headers, "タブ名", 1);
                var candidateColumn = GetColumnIndex(headers, "候補", 2);
                var selectColumn = GetColumnIndex(headers, "選択", 3);
                var compressColumn = GetColumnIndex(headers, "圧縮", 4);
                var archiveNameColumn = GetColumnIndex(headers, "圧縮名", 5);
                var keepFolderColumn = GetColumnIndex(headers, "フォルダ維持", 6);
                var metadataColumn = GetColumnIndex(headers, "列情報(JSON)", 7);

                var dataRow = row + 2;
                for (; dataRow <= lastRow; dataRow++)
                {
                    var first = GetString(sheet.Cell(dataRow, 1));
                    if (IsUnifiedSectionTitle(first))
                    {
                        break;
                    }

                    var tabName = GetString(sheet.Cell(dataRow, tabColumn));
                    var selected = ParseSelectState(
                        GetString(sheet.Cell(dataRow, selectColumn)),
                        ParseBool(sheet.Cell(dataRow, selectColumn)));
                    var candidateText = GetString(sheet.Cell(dataRow, candidateColumn));
                    if (string.IsNullOrWhiteSpace(candidateText))
                    {
                        continue;
                    }

                    if (!candidatesByDisplay.TryGetValue(candidateText, out var candidate))
                    {
                        candidatesByNormalizedDisplay.TryGetValue(NormalizeCandidateDisplay(candidateText), out candidate);
                    }

                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(tabName))
                    {
                        tabName = candidate.TabName;
                    }
                    if (string.IsNullOrWhiteSpace(tabName))
                    {
                        continue;
                    }

                    includedCustomTabNames.Add(tabName);
                    var state = GetOrCreateCustomTabState(states, tabName);
                    var rowState = FindRowBySourcePath(state.Rows, candidate.SourcePath);
                    if (rowState == null)
                    {
                        rowState = new CustomTabRowState
                        {
                            SourcePath = candidate.SourcePath,
                            Folder = candidate.Folder,
                            FileName = candidate.FileName,
                            ColumnValues = new Dictionary<string, string>(candidate.ColumnValues, StringComparer.OrdinalIgnoreCase)
                        };
                        state.Rows.Add(rowState);
                    }

                    rowState.IsSelected = selected;
                    rowState.Folder = candidate.Folder;
                    rowState.FileName = candidate.FileName;
                    var metadataJson = GetString(sheet.Cell(dataRow, metadataColumn));
                    if (!string.IsNullOrWhiteSpace(metadataJson))
                    {
                        rowState.ColumnValues = ParseJsonDictionary(metadataJson);
                    }

                    var shouldCompress = ParseCompressionValue(GetString(sheet.Cell(dataRow, compressColumn)));
                    if (!rowState.IsSelected || !shouldCompress)
                    {
                        continue;
                    }

                    var archiveName = GetString(sheet.Cell(dataRow, archiveNameColumn));
                    if (string.IsNullOrWhiteSpace(archiveName))
                    {
                        archiveName = tabName;
                    }

                    var keepFolder = ParseFolderKeepValue(GetString(sheet.Cell(dataRow, keepFolderColumn)), defaultValue: true);
                    AddZipItem(zipAccumulator, tabName, archiveName, candidate, keepFolder);
                }

                row = dataRow - 1;
            }
        }

        if (moduleRows.Count > 0)
        {
            model.ModuleSelections = moduleRows;
            var helixVersions = moduleRows
                .Select(module => module.HelixVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            model.SelectedHelixVersions = helixVersions;
            model.SelectedHelixVersion = helixVersions.FirstOrDefault() ?? string.Empty;
        }

        if (states.Count > 0)
        {
            if (includedCustomTabNames.Count == 0)
            {
                foreach (var stateName in states.Keys)
                {
                    includedCustomTabNames.Add(stateName);
                }
            }

            model.CustomTabStates = states.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            model.IncludedCustomTabNames = includedCustomTabNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            model.SelectedCustomTabName = model.IncludedCustomTabNames.FirstOrDefault() ?? string.Empty;
            model.HasCustomTabsSection = true;
        }

        var zipPlans = BuildZipPlans(zipAccumulator);
        if (zipPlans.Count > 0)
        {
            model.CustomZipPlans = zipPlans;
            model.HasCustomZipPlansSection = true;
        }
    }

    private static List<BulkModuleSelectionRow> ReadModuleSheet(IXLWorksheet sheet, IReadOnlyList<string> defaultHelixVersions)
    {
        var headers = BuildHeaderMap(sheet);
        var hasHelixColumn = headers.TryGetValue("Helixバージョン", out var helixColumn);
        var selectColumn = GetColumnIndex(headers, "選択", 1);
        var codeColumn = GetColumnIndex(headers, "コード", hasHelixColumn ? 3 : 2);
        var nameColumn = GetColumnIndex(headers, "名称", hasHelixColumn ? 4 : 3);
        var compatVersionColumn = GetColumnIndex(headers, "対応表版数", hasHelixColumn ? 5 : 4);
        var supportedOsColumn = GetColumnIndex(headers, "対応OS", 0);
        var osColumn = GetColumnIndex(headers, "選択OS", hasHelixColumn ? 6 : 5);
        var supportStatusColumn = GetColumnIndex(headers, "対応", 0);
        var installerVersionColumn = GetColumnIndex(headers, "実ファイル版数", hasHelixColumn ? 7 : 6);

        var result = new List<BulkModuleSelectionRow>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var code = GetString(sheet.Cell(row, codeColumn));
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var helixValues = hasHelixColumn
                ? new[] { GetString(sheet.Cell(row, helixColumn)) }
                : defaultHelixVersions.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (helixValues.Length == 0)
            {
                continue;
            }

            var isSelected = ParseSelectState(
                GetString(sheet.Cell(row, selectColumn)),
                ParseBool(sheet.Cell(row, selectColumn)));
            var name = nameColumn > 0 ? GetString(sheet.Cell(row, nameColumn)) : string.Empty;
            var compatibilityVersion = compatVersionColumn > 0 ? GetString(sheet.Cell(row, compatVersionColumn)) : string.Empty;
            var supportedOsDisplay = supportedOsColumn > 0 ? GetString(sheet.Cell(row, supportedOsColumn)) : string.Empty;
            var osSelection = osColumn > 0 ? GetString(sheet.Cell(row, osColumn)) : string.Empty;
            var supportStatus = supportStatusColumn > 0 ? GetString(sheet.Cell(row, supportStatusColumn)) : string.Empty;
            var installerVersion = installerVersionColumn > 0 ? GetString(sheet.Cell(row, installerVersionColumn)) : string.Empty;

            foreach (var helix in helixValues)
            {
                if (string.IsNullOrWhiteSpace(helix))
                {
                    continue;
                }

                result.Add(new BulkModuleSelectionRow
                {
                    IsSelected = isSelected,
                    HelixVersion = helix,
                    Code = code,
                    Name = name,
                    CompatibilityVersion = compatibilityVersion,
                    SupportedOsDisplay = supportedOsDisplay,
                    OsSelection = osSelection,
                    SupportStatus = supportStatus,
                    SelectedInstallerVersion = installerVersion
                });
            }
        }

        return result;
    }

    private static List<BulkScanSelectionRow> ReadScanSheet(IXLWorksheet sheet)
    {
        var headers = BuildHeaderMap(sheet);
        var selectColumn = GetColumnIndex(headers, "選択", 1);
        var sourcePathColumn = GetColumnIndex(headers, "ソースパス", 0);
        var codeColumn = GetColumnIndex(headers, "コード", sourcePathColumn > 0 ? 3 : 2);
        var versionColumn = GetColumnIndex(headers, "版数", sourcePathColumn > 0 ? 4 : 3);
        var osColumn = GetColumnIndex(headers, "OS", sourcePathColumn > 0 ? 5 : 4);

        var result = new List<BulkScanSelectionRow>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var sourcePath = sourcePathColumn > 0 ? GetString(sheet.Cell(row, sourcePathColumn)) : string.Empty;
            var code = GetString(sheet.Cell(row, codeColumn));
            if (string.IsNullOrWhiteSpace(sourcePath) && string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            result.Add(new BulkScanSelectionRow
            {
                IsSelected = ParseSelectState(
                    GetString(sheet.Cell(row, selectColumn)),
                    ParseBool(sheet.Cell(row, selectColumn))),
                SourcePath = sourcePath,
                Code = code,
                Version = GetString(sheet.Cell(row, versionColumn)),
                Os = GetString(sheet.Cell(row, osColumn))
            });
        }

        return result;
    }

    private static CustomTabReadResult ReadCustomTabs(
        IXLWorksheet? customTabSheet,
        IXLWorksheet? customCandidatesSheet,
        IXLWorksheet? customTabRowsSheet,
        BulkSelectionWorkbookModel model)
    {
        var states = new Dictionary<string, CustomTabState>(StringComparer.OrdinalIgnoreCase);
        var includedCustomTabNames = new List<string>();
        if (customTabSheet != null)
        {
            var customTabHeaders = BuildHeaderMap(customTabSheet);
            var tabNameColumn = GetColumnIndex(customTabHeaders, "タブ名", 1);
            var initialColumnsColumn = GetColumnIndex(customTabHeaders, "初期カラム", 2);
            var newDirectoryColumn = GetColumnIndex(customTabHeaders, "追加フォルダ", 0);
            var selectColumn = GetColumnIndex(customTabHeaders, "選択", newDirectoryColumn > 0 ? 4 : 3);

            var lastRow = customTabSheet.LastRowUsed()?.RowNumber() ?? 1;
            for (var row = 2; row <= lastRow; row++)
            {
                var tabName = GetString(customTabSheet.Cell(row, tabNameColumn));
                if (string.IsNullOrWhiteSpace(tabName))
                {
                    continue;
                }

                var state = new CustomTabState
                {
                    Name = tabName,
                    ColumnsInput = GetString(customTabSheet.Cell(row, initialColumnsColumn)),
                    NewDirectoryPath = newDirectoryColumn > 0
                        ? GetString(customTabSheet.Cell(row, newDirectoryColumn))
                        : string.Empty
                };

                states[tabName] = state;
                if (ParseSelectState(
                        GetString(customTabSheet.Cell(row, selectColumn)),
                        ParseBool(customTabSheet.Cell(row, selectColumn))))
                {
                    includedCustomTabNames.Add(tabName);
                }
            }
        }

        if (includedCustomTabNames.Count == 0 && states.Count > 0)
        {
            includedCustomTabNames.AddRange(states.Keys);
        }

        model.IncludedCustomTabNames = includedCustomTabNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        model.SelectedCustomTabName = model.IncludedCustomTabNames.FirstOrDefault() ?? string.Empty;
        var includedSet = model.IncludedCustomTabNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidatesByDisplay = ReadCustomCandidates(customCandidatesSheet, states);
        var zipAccumulator = new Dictionary<string, List<CustomZipPlanItem>>(StringComparer.OrdinalIgnoreCase);

        if (customTabRowsSheet != null)
        {
            var headers = BuildHeaderMap(customTabRowsSheet);
            if (headers.ContainsKey("候補"))
            {
                ReadCustomRowsNewFormat(customTabRowsSheet, headers, states, candidatesByDisplay, zipAccumulator, includedSet);
            }
            else
            {
                ReadCustomRowsLegacyFormat(customTabRowsSheet, headers, states, zipAccumulator, includedSet);
            }
        }

        var zipPlans = BuildZipPlans(zipAccumulator);
        var tabStates = states.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CustomTabReadResult(tabStates, candidatesByDisplay, zipPlans, model.IncludedCustomTabNames);
    }

    private static Dictionary<string, CustomCandidateEntry> ReadCustomCandidates(
        IXLWorksheet? sheet,
        IDictionary<string, CustomTabState> states)
    {
        var candidates = new Dictionary<string, CustomCandidateEntry>(StringComparer.OrdinalIgnoreCase);
        if (sheet == null)
        {
            return candidates;
        }

        var headers = BuildHeaderMap(sheet);
        var displayColumn = GetColumnIndex(headers, "候補", 1);
        var tabColumn = GetColumnIndex(headers, "タブ名", 2);
        var folderColumn = GetColumnIndex(headers, "フォルダ", 3);
        var fileNameColumn = GetColumnIndex(headers, "ファイル名", 4);
        var sourcePathColumn = GetColumnIndex(headers, "ソースパス", 5);
        var metadataColumn = GetColumnIndex(headers, "列情報(JSON)", 6);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var display = GetString(sheet.Cell(row, displayColumn));
            var tabName = GetString(sheet.Cell(row, tabColumn));
            var sourcePath = GetString(sheet.Cell(row, sourcePathColumn));
            if (string.IsNullOrWhiteSpace(display) ||
                string.IsNullOrWhiteSpace(tabName) ||
                string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            var folder = GetString(sheet.Cell(row, folderColumn));
            var fileName = GetString(sheet.Cell(row, fileNameColumn));
            var columnValues = ParseJsonDictionary(GetString(sheet.Cell(row, metadataColumn)));

            var candidate = new CustomCandidateEntry(
                display,
                tabName,
                folder,
                fileName,
                sourcePath,
                columnValues);

            candidates.TryAdd(display, candidate);

            var state = GetOrCreateCustomTabState(states, tabName);
            var existing = FindRowBySourcePath(state.Rows, sourcePath);
            if (existing != null)
            {
                continue;
            }

            state.Rows.Add(new CustomTabRowState
            {
                IsSelected = false,
                Folder = folder,
                FileName = fileName,
                SourcePath = sourcePath,
                ColumnValues = new Dictionary<string, string>(columnValues, StringComparer.OrdinalIgnoreCase)
            });
        }

        return candidates;
    }
    private static void ReadCustomRowsNewFormat(
        IXLWorksheet sheet,
        IReadOnlyDictionary<string, int> headers,
        IDictionary<string, CustomTabState> states,
        IReadOnlyDictionary<string, CustomCandidateEntry> candidatesByDisplay,
        IDictionary<string, List<CustomZipPlanItem>> zipAccumulator,
        IReadOnlySet<string> includedCustomTabNames)
    {
        var tabColumn = GetColumnIndex(headers, "タブ名", 1);
        var selectColumn = GetColumnIndex(headers, "選択", 2);
        var candidateColumn = GetColumnIndex(headers, "候補", 3);
        var compressColumn = GetColumnIndex(headers, "圧縮", 4);
        var archiveNameColumn = GetColumnIndex(headers, "圧縮名", 5);
        var keepFolderColumn = GetColumnIndex(headers, "フォルダ維持", 6);
        var metadataColumn = GetColumnIndex(headers, "列情報(JSON)", 7);
        var candidatesByNormalizedDisplay = candidatesByDisplay
            .Values
            .GroupBy(candidate => NormalizeCandidateDisplay(candidate.DisplayKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var tabName = GetString(sheet.Cell(row, tabColumn));
            var selected = ParseSelectState(
                GetString(sheet.Cell(row, selectColumn)),
                ParseBool(sheet.Cell(row, selectColumn)));
            var candidateText = GetString(sheet.Cell(row, candidateColumn));
            if (string.IsNullOrWhiteSpace(candidateText))
            {
                continue;
            }

            if (!candidatesByDisplay.TryGetValue(candidateText, out var candidate))
            {
                candidatesByNormalizedDisplay.TryGetValue(NormalizeCandidateDisplay(candidateText), out candidate);
            }

            if (candidate == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(tabName))
            {
                tabName = candidate.TabName;
            }
            if (includedCustomTabNames.Count > 0 && !includedCustomTabNames.Contains(tabName))
            {
                continue;
            }

            var state = GetOrCreateCustomTabState(states, tabName);
            var rowState = FindRowBySourcePath(state.Rows, candidate.SourcePath);
            if (rowState == null)
            {
                rowState = new CustomTabRowState
                {
                    SourcePath = candidate.SourcePath,
                    Folder = candidate.Folder,
                    FileName = candidate.FileName,
                    ColumnValues = new Dictionary<string, string>(candidate.ColumnValues, StringComparer.OrdinalIgnoreCase)
                };
                state.Rows.Add(rowState);
            }

            rowState.IsSelected = selected;
            rowState.Folder = candidate.Folder;
            rowState.FileName = candidate.FileName;
            var metadataJson = GetString(sheet.Cell(row, metadataColumn));
            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                rowState.ColumnValues = ParseJsonDictionary(metadataJson);
            }

            var shouldCompress = ParseCompressionValue(GetString(sheet.Cell(row, compressColumn)));
            if (!rowState.IsSelected || !shouldCompress)
            {
                continue;
            }

            var archiveBaseName = GetString(sheet.Cell(row, archiveNameColumn));
            if (string.IsNullOrWhiteSpace(archiveBaseName))
            {
                archiveBaseName = tabName;
            }

            var keepFolder = ParseFolderKeepValue(GetString(sheet.Cell(row, keepFolderColumn)), defaultValue: true);
            AddZipItem(zipAccumulator, tabName, archiveBaseName, candidate, keepFolder);
        }
    }

    private static void ReadCustomRowsLegacyFormat(
        IXLWorksheet sheet,
        IReadOnlyDictionary<string, int> headers,
        IDictionary<string, CustomTabState> states,
        IDictionary<string, List<CustomZipPlanItem>> zipAccumulator,
        IReadOnlySet<string> includedCustomTabNames)
    {
        var tabColumn = GetColumnIndex(headers, "タブ名", 1);
        var selectColumn = GetColumnIndex(headers, "選択", 2);
        var folderColumn = GetColumnIndex(headers, "フォルダ", 3);
        var fileNameColumn = GetColumnIndex(headers, "ファイル名", 4);
        var sourcePathColumn = GetColumnIndex(headers, "ソースパス", 5);
        var metadataColumn = GetColumnIndex(headers, "列情報(JSON)", 6);
        var compressColumn = GetColumnIndex(headers, "圧縮", 0);
        var archiveNameColumn = GetColumnIndex(headers, "圧縮名", 0);
        var keepFolderColumn = GetColumnIndex(headers, "フォルダ維持", 0);

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var tabName = GetString(sheet.Cell(row, tabColumn));
            var sourcePath = sourcePathColumn > 0 ? GetString(sheet.Cell(row, sourcePathColumn)) : string.Empty;
            if (string.IsNullOrWhiteSpace(tabName) || string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }
            if (includedCustomTabNames.Count > 0 && !includedCustomTabNames.Contains(tabName))
            {
                continue;
            }

            var selected = ParseSelectState(
                GetString(sheet.Cell(row, selectColumn)),
                ParseBool(sheet.Cell(row, selectColumn)));
            var folder = folderColumn > 0 ? GetString(sheet.Cell(row, folderColumn)) : string.Empty;
            var fileName = fileNameColumn > 0 ? GetString(sheet.Cell(row, fileNameColumn)) : Path.GetFileName(sourcePath);

            var state = GetOrCreateCustomTabState(states, tabName);
            var rowState = FindRowBySourcePath(state.Rows, sourcePath);
            if (rowState == null)
            {
                rowState = new CustomTabRowState
                {
                    SourcePath = sourcePath,
                    Folder = folder,
                    FileName = fileName,
                    ColumnValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
                state.Rows.Add(rowState);
            }

            rowState.IsSelected = selected;
            rowState.Folder = folder;
            rowState.FileName = fileName;
            if (metadataColumn > 0)
            {
                var metadataJson = GetString(sheet.Cell(row, metadataColumn));
                if (!string.IsNullOrWhiteSpace(metadataJson))
                {
                    rowState.ColumnValues = ParseJsonDictionary(metadataJson);
                }
            }

            if (compressColumn <= 0)
            {
                continue;
            }

            var shouldCompress = ParseCompressionValue(GetString(sheet.Cell(row, compressColumn)));
            if (!rowState.IsSelected || !shouldCompress)
            {
                continue;
            }

            var archiveBaseName = archiveNameColumn > 0
                ? GetString(sheet.Cell(row, archiveNameColumn))
                : string.Empty;
            if (string.IsNullOrWhiteSpace(archiveBaseName))
            {
                archiveBaseName = tabName;
            }

            var keepFolder = keepFolderColumn > 0
                ? ParseFolderKeepValue(GetString(sheet.Cell(row, keepFolderColumn)), defaultValue: true)
                : true;

            var candidate = new CustomCandidateEntry(
                BuildCandidateDisplay(tabName, folder, fileName),
                tabName,
                folder,
                fileName,
                sourcePath,
                rowState.ColumnValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            AddZipItem(zipAccumulator, tabName, archiveBaseName, candidate, keepFolder);
        }
    }

    private static List<CustomZipPlan> ReadCustomZipSheet(
        IXLWorksheet sheet,
        IReadOnlyDictionary<string, CustomCandidateEntry> candidatesByDisplay)
    {
        var headers = BuildHeaderMap(sheet);
        var tabColumn = GetColumnIndex(headers, "タブ名", 1);
        var zipNameColumn = GetColumnIndex(headers, "ZIP名", 2);
        var candidateColumn = GetColumnIndex(headers, "候補", 3);
        var keepFolderColumn = GetColumnIndex(headers, "フォルダ維持", 4);
        var sourcePathColumn = GetColumnIndex(headers, "ソースパス", 0);
        var folderColumn = GetColumnIndex(headers, "フォルダ", 0);
        var fileNameColumn = GetColumnIndex(headers, "ファイル名", 0);

        var zipAccumulator = new Dictionary<string, List<CustomZipPlanItem>>(StringComparer.OrdinalIgnoreCase);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var tabName = GetString(sheet.Cell(row, tabColumn));
            var zipName = GetString(sheet.Cell(row, zipNameColumn));
            if (string.IsNullOrWhiteSpace(tabName) || string.IsNullOrWhiteSpace(zipName))
            {
                continue;
            }

            var keepFolder = ParseFolderKeepValue(GetString(sheet.Cell(row, keepFolderColumn)), defaultValue: true);

            CustomCandidateEntry? candidate = null;
            if (candidateColumn > 0)
            {
                var candidateText = GetString(sheet.Cell(row, candidateColumn));
                if (!string.IsNullOrWhiteSpace(candidateText))
                {
                    candidatesByDisplay.TryGetValue(candidateText, out candidate);
                }
            }

            if (candidate == null && sourcePathColumn > 0)
            {
                var sourcePath = GetString(sheet.Cell(row, sourcePathColumn));
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    candidate = new CustomCandidateEntry(
                        BuildCandidateDisplay(tabName,
                            folderColumn > 0 ? GetString(sheet.Cell(row, folderColumn)) : string.Empty,
                            fileNameColumn > 0 ? GetString(sheet.Cell(row, fileNameColumn)) : Path.GetFileName(sourcePath)),
                        tabName,
                        folderColumn > 0 ? GetString(sheet.Cell(row, folderColumn)) : string.Empty,
                        fileNameColumn > 0 ? GetString(sheet.Cell(row, fileNameColumn)) : Path.GetFileName(sourcePath),
                        sourcePath,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                }
            }

            if (candidate == null || string.IsNullOrWhiteSpace(candidate.SourcePath))
            {
                continue;
            }

            AddZipItem(zipAccumulator, tabName, zipName, candidate, keepFolder);
        }

        return BuildZipPlans(zipAccumulator);
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet)
    {
        return BuildHeaderMap(sheet, 1);
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (var col = 1; col <= lastCol; col++)
        {
            var header = GetString(sheet.Cell(headerRow, col));
            if (string.IsNullOrWhiteSpace(header) || map.ContainsKey(header))
            {
                continue;
            }

            map[header] = col;
        }

        return map;
    }

    private static int GetColumnIndex(IReadOnlyDictionary<string, int> headers, string header, int fallback)
    {
        return headers.TryGetValue(header, out var index)
            ? index
            : fallback;
    }
    private static string GetString(IXLCell cell)
    {
        return cell.GetString().Trim();
    }

    private static string GetValue(IReadOnlyDictionary<string, string> map, string key, string fallback)
    {
        return map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static bool ParseBool(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Boolean)
        {
            return cell.GetBoolean();
        }

        return ParseBoolText(cell.GetString());
    }

    private static bool ParseBoolText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (bool.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        if (trimmed == "1")
        {
            return true;
        }

        if (trimmed == "0")
        {
            return false;
        }

        var normalized = trimmed.ToUpperInvariant();
        return normalized is "YES" or "Y" or "ON" or "TRUE" or "はい" or "○" or "有" or "あり";
    }

    private static bool ParseSelectState(string? text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var trimmed = text.Trim();
        if (ParseBoolText(trimmed))
        {
            return true;
        }

        if (trimmed.Equals("選択しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("選択しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("未選択", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("無効", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("いいえ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("なし", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Equals("選択する", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("選択する", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("する", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("有効", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fallback;
    }

    private static bool IsUnifiedSectionTitle(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith("■", StringComparison.Ordinal);
    }

    private static bool ParseCompressionValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("圧縮しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("圧縮しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("しない", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("無効", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("無", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("なし", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ParseBoolText(trimmed))
        {
            return true;
        }

        return trimmed.Equals("圧縮する", StringComparison.OrdinalIgnoreCase)
               || trimmed.Contains("圧縮する", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("する", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("有", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("あり", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseFolderKeepValue(string? text, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (ParseBoolText(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (trimmed.Equals("平坦", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("なし", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("無", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.Equals("維持", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("あり", StringComparison.OrdinalIgnoreCase)
               || defaultValue;
    }

    private static Dictionary<string, string> ParseJsonDictionary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<CustomCandidateEntry> BuildCustomCandidates(IReadOnlyList<CustomTabState> customTabStates)
    {
        var candidates = new List<CustomCandidateEntry>();
        var displayCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in customTabStates)
        {
            foreach (var row in tab.Rows ?? new List<CustomTabRowState>())
            {
                if (string.IsNullOrWhiteSpace(tab.Name) || string.IsNullOrWhiteSpace(row.SourcePath))
                {
                    continue;
                }

                var folder = string.IsNullOrWhiteSpace(row.Folder)
                    ? "-"
                    : row.Folder;
                var fileName = string.IsNullOrWhiteSpace(row.FileName)
                    ? Path.GetFileName(row.SourcePath)
                    : row.FileName;
                var baseDisplay = BuildCandidateDisplay(tab.Name, folder, fileName);
                if (!displayCounts.TryGetValue(baseDisplay, out var count))
                {
                    count = 0;
                }

                count++;
                displayCounts[baseDisplay] = count;
                var display = count == 1 ? baseDisplay : $"{baseDisplay} ({count})";

                candidates.Add(new CustomCandidateEntry(
                    display,
                    tab.Name,
                    folder,
                    fileName,
                    row.SourcePath,
                    new Dictionary<string, string>(
                        row.ColumnValues ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase)));
            }
        }

        return candidates;
    }

    private static string BuildCandidateDisplay(string tabName, string folder, string fileName)
    {
        return $"{tabName} | {folder} | {fileName}";
    }

    private static string NormalizeCandidateDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join("|", value
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim()));
    }

    private static string ExtractVersionToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = VersionTokenRegex.Match(value);
        return match.Success ? match.Value : value;
    }

    private static string ResolveHelixOsInfo(string helixVersion)
    {
        var token = ExtractVersionToken(helixVersion);
        if (string.IsNullOrWhiteSpace(token))
        {
            return "要確認";
        }

        if (VersionUtil.CompareVersionLike(token, "2025.4") >= 0)
        {
            return "Win11-64bit(22H2-24H2), Ubuntu-64bit(22.04/24.04 LTS), Rocky-64bit(9-9.3)";
        }

        if (VersionUtil.CompareVersionLike(token, "2025.2") >= 0)
        {
            return "Win11-64bit(22H2-24H2), Win10-64bit(2004-22H2), Ubuntu-64bit(22.04/24.04 LTS), Rocky-64bit(9-9.3)";
        }

        if (VersionUtil.CompareVersionLike(token, "2024.3") >= 0)
        {
            return "Win11-64bit(22H2), Win10-64bit(2004-22H2), Ubuntu-64bit(22.04 LTS), Rocky-64bit(9-9.3)";
        }

        if (VersionUtil.CompareVersionLike(token, "2024.2") >= 0)
        {
            return "Win11-64bit(22H2), Win10-64bit(2004-22H2), Redhat-64bit(EL7+), CentOS-64bit(7+), Ubuntu-64bit(22.04 LTS), Rocky-64bit(9-9.3)";
        }

        if (VersionUtil.CompareVersionLike(token, "2023.3") >= 0)
        {
            return "Win11-64bit(22H2), Win10-64bit(2004-22H2), Redhat-64bit(EL7+), CentOS-64bit(7+)";
        }

        if (VersionUtil.CompareVersionLike(token, "2021.2") >= 0)
        {
            return "Win10-64bit(指定なし), Redhat-64bit(EL7+), CentOS-64bit(7+)";
        }

        if (VersionUtil.CompareVersionLike(token, "2020.1") >= 0)
        {
            return "Win10-64bit(指定なし), Redhat-64bit(EL6+)";
        }

        if (VersionUtil.CompareVersionLike(token, "2019.1") >= 0)
        {
            return "Win10/Win7(x86/x64), Redhat(x86/x64, EL5+)";
        }

        return "要確認";
    }

    private static Dictionary<string, CustomZipInfo> BuildZipInfoByPath(IReadOnlyList<CustomZipPlan> plans)
    {
        var map = new Dictionary<string, CustomZipInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            foreach (var item in plan.Items)
            {
                if (string.IsNullOrWhiteSpace(item.SourcePath) || map.ContainsKey(item.SourcePath))
                {
                    continue;
                }

                map[item.SourcePath] = new CustomZipInfo(plan.TabName, plan.ArchiveBaseName, item.IncludeFolderInArchive);
            }
        }

        return map;
    }

    private static CustomTabState GetOrCreateCustomTabState(IDictionary<string, CustomTabState> states, string tabName)
    {
        if (!states.TryGetValue(tabName, out var state))
        {
            state = new CustomTabState
            {
                Name = tabName
            };
            states[tabName] = state;
        }

        return state;
    }

    private static CustomTabRowState? FindRowBySourcePath(IEnumerable<CustomTabRowState> rows, string sourcePath)
    {
        return rows.FirstOrDefault(row =>
            row.SourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddZipItem(
        IDictionary<string, List<CustomZipPlanItem>> zipAccumulator,
        string tabName,
        string archiveBaseName,
        CustomCandidateEntry candidate,
        bool includeFolderInArchive)
    {
        if (string.IsNullOrWhiteSpace(tabName) ||
            string.IsNullOrWhiteSpace(archiveBaseName) ||
            string.IsNullOrWhiteSpace(candidate.SourcePath) ||
            string.IsNullOrWhiteSpace(candidate.FileName))
        {
            return;
        }

        var key = $"{tabName}\t{archiveBaseName}";
        if (!zipAccumulator.TryGetValue(key, out var list))
        {
            list = new List<CustomZipPlanItem>();
            zipAccumulator[key] = list;
        }

        list.Add(new CustomZipPlanItem(
            candidate.SourcePath,
            candidate.Folder,
            candidate.FileName,
            includeFolderInArchive));
    }

    private static List<CustomZipPlan> BuildZipPlans(IDictionary<string, List<CustomZipPlanItem>> zipAccumulator)
    {
        var result = new List<CustomZipPlan>();
        foreach (var pair in zipAccumulator)
        {
            var split = pair.Key.Split('\t');
            if (split.Length != 2)
            {
                continue;
            }

            var tabName = split[0];
            var archiveBaseName = split[1];
            var items = pair.Value
                .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath) && !string.IsNullOrWhiteSpace(item.FileName))
                .GroupBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            result.Add(new CustomZipPlan(tabName, archiveBaseName, items));
        }

        return result;
    }

    private static List<CustomZipPlan> MergeCustomZipPlans(
        IReadOnlyList<CustomZipPlan> first,
        IReadOnlyList<CustomZipPlan> second)
    {
        return BuildZipPlans(first
            .Concat(second)
            .SelectMany(plan => plan.Items.Select(item => new
            {
                plan.TabName,
                plan.ArchiveBaseName,
                Item = item
            }))
            .GroupBy(item => $"{item.TabName}\t{item.ArchiveBaseName}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Item)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase));
    }

    private sealed record CustomCandidateEntry(
        string DisplayKey,
        string TabName,
        string Folder,
        string FileName,
        string SourcePath,
        IReadOnlyDictionary<string, string> ColumnValues);

    private sealed record CustomZipInfo(
        string TabName,
        string ArchiveBaseName,
        bool IncludeFolderInArchive);

    private sealed record CustomTabReadResult(
        List<CustomTabState> CustomTabStates,
        Dictionary<string, CustomCandidateEntry> CandidatesByDisplay,
        List<CustomZipPlan> CustomZipPlans,
        List<string> IncludedCustomTabNames);
}
