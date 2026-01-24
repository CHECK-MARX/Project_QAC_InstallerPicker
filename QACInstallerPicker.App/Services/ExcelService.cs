using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class ExcelService
{
    private static readonly Regex HelixVersionRegex = new(@"20\d{2}\.\d+", RegexOptions.Compiled);

    public CompatibilityData LoadCompatibility(string excelPath)
    {
        using var stream = new FileStream(
            excelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet("QAF", out var sheet))
        {
            throw new InvalidOperationException("QAFシートが見つかりません。");
        }

        var used = sheet.RangeUsed();
        if (used == null)
        {
            throw new InvalidOperationException("QAFシートが空です。");
        }

        var firstRow = used.FirstRowUsed().RowNumber();
        var firstCol = used.FirstColumnUsed().ColumnNumber();
        var lastRow = used.LastRowUsed().RowNumber();
        var lastCol = used.LastColumnUsed().ColumnNumber();

        var rowBased = TryLoadRowBased(sheet, firstRow, lastRow, firstCol, lastCol);
        var columnBased = TryLoadColumnBased(sheet, firstRow, lastRow, firstCol, lastCol);
        if (columnBased.Data != null && (rowBased.Data == null || columnBased.Score > rowBased.Score))
        {
            return columnBased.Data;
        }

        if (rowBased.Data != null)
        {
            return rowBased.Data;
        }

        throw new InvalidOperationException("モジュールとバージョンの行/列を検出できませんでした。対応表の見出し行を確認してください。");
    }

    private static (CompatibilityData? Data, int Score) TryLoadRowBased(
        IXLWorksheet sheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var headerRow = FindModuleHeaderRow(sheet, firstRow, lastRow, firstCol, lastCol);
        var versionCol = FindVersionColumn(sheet, headerRow + 1, lastRow, firstCol, lastCol);
        if (versionCol == null)
        {
            return (null, 0);
        }

        var moduleCols = new List<(int Col, string Code)>();
        var moduleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var col = firstCol; col <= lastCol; col++)
        {
            if (col == versionCol.Value)
            {
                continue;
            }

            var code = NormalizeToken(sheet.Cell(headerRow, col).GetString());
            if (!LooksLikeCode(code) || !moduleSet.Add(code))
            {
                continue;
            }

            moduleCols.Add((col, code));
        }

        if (moduleCols.Count == 0)
        {
            return (null, 0);
        }

        var versions = new List<HelixVersionData>();
        var versionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var helixVersion = NormalizeToken(sheet.Cell(row, versionCol.Value).GetString());
            if (string.IsNullOrWhiteSpace(helixVersion) || !LooksLikeHelixVersion(helixVersion))
            {
                continue;
            }

            if (!versionSet.Add(helixVersion))
            {
                continue;
            }

            var moduleSupport = new Dictionary<string, ModuleSupportInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, code) in moduleCols)
            {
                var cell = NormalizeToken(sheet.Cell(row, col).GetString());
                var supported = !string.IsNullOrWhiteSpace(cell) && cell != "-";
                moduleSupport[code] = new ModuleSupportInfo
                {
                    IsSupported = supported,
                    ModuleVersion = supported ? cell : null
                };
            }

            versions.Add(new HelixVersionData(helixVersion, moduleSupport));
        }

        if (versions.Count == 0)
        {
            return (null, 0);
        }

        var data = new CompatibilityData(moduleCols.Select(x => x.Code).ToList(), versions);
        return (data, moduleCols.Count * versions.Count);
    }

    private static (CompatibilityData? Data, int Score) TryLoadColumnBased(
        IXLWorksheet sheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var headerRow = FindVersionHeaderRow(sheet, firstRow, lastRow, firstCol, lastCol);
        var moduleCol = FindModuleColumn(sheet, headerRow + 1, lastRow, firstCol, lastCol);
        if (moduleCol == null)
        {
            return (null, 0);
        }

        var versionCols = new List<(int Col, string Version)>();
        var versionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var col = firstCol; col <= lastCol; col++)
        {
            if (col == moduleCol.Value)
            {
                continue;
            }

            var version = NormalizeToken(sheet.Cell(headerRow, col).GetString());
            if (!LooksLikeHelixVersion(version) || !versionSet.Add(version))
            {
                continue;
            }

            versionCols.Add((col, version));
        }

        if (versionCols.Count == 0)
        {
            return (null, 0);
        }

        var moduleRows = new List<(int Row, string Code)>();
        var moduleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var code = NormalizeToken(sheet.Cell(row, moduleCol.Value).GetString());
            if (!LooksLikeCode(code) || !moduleSet.Add(code))
            {
                continue;
            }

            moduleRows.Add((row, code));
        }

        if (moduleRows.Count == 0)
        {
            return (null, 0);
        }

        var versions = new List<HelixVersionData>();
        foreach (var (col, version) in versionCols)
        {
            var moduleSupport = new Dictionary<string, ModuleSupportInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (row, code) in moduleRows)
            {
                var cell = NormalizeToken(sheet.Cell(row, col).GetString());
                var supported = !string.IsNullOrWhiteSpace(cell) && cell != "-";
                moduleSupport[code] = new ModuleSupportInfo
                {
                    IsSupported = supported,
                    ModuleVersion = supported ? cell : null
                };
            }

            versions.Add(new HelixVersionData(version, moduleSupport));
        }

        var data = new CompatibilityData(moduleRows.Select(x => x.Code).ToList(), versions);
        return (data, moduleRows.Count * versionCols.Count);
    }

    private static int FindVersionHeaderRow(
        IXLWorksheet sheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var bestRow = firstRow;
        var bestMatches = 0;

        for (var row = firstRow; row <= lastRow; row++)
        {
            var matches = 0;
            for (var col = firstCol + 1; col <= lastCol; col++)
            {
                var value = NormalizeToken(sheet.Cell(row, col).GetString());
                if (LooksLikeHelixVersion(value))
                {
                    matches++;
                }
            }

            if (matches > bestMatches)
            {
                bestMatches = matches;
                bestRow = row;
            }
        }

        return bestMatches >= 2 ? bestRow : firstRow;
    }

    private static int? FindVersionColumn(
        IXLWorksheet sheet,
        int startRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var bestCol = 0;
        var bestMatches = 0;

        for (var col = firstCol; col <= lastCol; col++)
        {
            var matches = 0;
            for (var row = startRow; row <= lastRow; row++)
            {
                var value = NormalizeToken(sheet.Cell(row, col).GetString());
                if (LooksLikeHelixVersion(value))
                {
                    matches++;
                }
            }

            if (matches > bestMatches)
            {
                bestMatches = matches;
                bestCol = col;
            }
        }

        return bestMatches >= 2 ? bestCol : null;
    }

    private static int? FindModuleColumn(
        IXLWorksheet sheet,
        int startRow,
        int lastRow,
        int firstCol,
        int lastCol)
    {
        var bestCol = 0;
        var bestMatches = 0;

        for (var col = firstCol; col <= lastCol; col++)
        {
            var matches = 0;
            for (var row = startRow; row <= lastRow; row++)
            {
                var value = NormalizeToken(sheet.Cell(row, col).GetString());
                if (LooksLikeCode(value))
                {
                    matches++;
                }
            }

            if (matches > bestMatches)
            {
                bestMatches = matches;
                bestCol = col;
            }
        }

        return bestMatches >= 2 ? bestCol : null;
    }

    private static int FindModuleHeaderRow(IXLWorksheet sheet, int firstRow, int lastRow, int firstCol, int lastCol)
    {
        var knownCodes = new HashSet<string>(
            ModuleCatalog.ModuleDescriptions.Keys.Select(NormalizeToken),
            StringComparer.OrdinalIgnoreCase);
        var bestKnownRow = firstRow;
        var bestKnownMatches = 0;
        var bestLooksRow = firstRow;
        var bestLooksMatches = 0;

        for (var row = firstRow; row <= lastRow; row++)
        {
            var knownMatches = 0;
            var looksMatches = 0;
            for (var col = firstCol + 1; col <= lastCol; col++)
            {
                var value = NormalizeToken(sheet.Cell(row, col).GetString());
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (knownCodes.Contains(value))
                {
                    knownMatches++;
                }

                if (LooksLikeHeaderToken(value))
                {
                    looksMatches++;
                }
            }

            if (knownMatches > bestKnownMatches)
            {
                bestKnownMatches = knownMatches;
                bestKnownRow = row;
            }

            if (looksMatches > bestLooksMatches)
            {
                bestLooksMatches = looksMatches;
                bestLooksRow = row;
            }
        }

        if (bestKnownMatches >= 2)
        {
            return bestKnownRow;
        }

        return bestLooksMatches >= 2 ? bestLooksRow : firstRow;
    }

    private static bool LooksLikeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length > 16)
        {
            return false;
        }

        var hasLetter = false;
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '+' && ch != '-')
            {
                return false;
            }
            if (char.IsLetter(ch))
            {
                hasLetter = true;
            }
        }

        return hasLetter;
    }

    private static bool LooksLikeHeaderToken(string value)
    {
        return LooksLikeCode(value);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Normalize(NormalizationForm.FormKC);
    }

    private static bool LooksLikeHelixVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return HelixVersionRegex.IsMatch(value);
    }
}
