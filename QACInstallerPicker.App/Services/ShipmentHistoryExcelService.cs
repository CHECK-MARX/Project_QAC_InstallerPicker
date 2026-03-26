using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public sealed class ShipmentHistoryExcelService
{
    private sealed record ShipmentHistoryColumnDefinition(
        int ColumnIndex,
        string HeaderName,
        Func<ShipmentHistoryRecord, string> ValueSelector);

    private static readonly IReadOnlyList<ShipmentHistoryColumnDefinition> Columns =
    [
        new(1, "\u9001\u4ED8\u65E5", row => row.ShipmentDate.ToString("yyyy/MM/dd")),
        new(2, "\u4F1A\u793E\u540D", row => row.CompanyName),
        new(3, "\u500B\u4EBA\u540D", row => row.PersonName),
        new(4, "\u533A\u5206", row => row.Category),
        new(5, "Helix\u30D0\u30FC\u30B8\u30E7\u30F3", row => row.HelixVersion),
        new(6, "\u30B3\u30FC\u30C9", row => row.Code),
        new(7, "\u540D\u79F0", row => row.Name),
        new(8, "\u5BFE\u5FDC\u8868\u7248\u6570", row => row.CompatibilityVersion),
        new(9, "\u9078\u629EOS", row => row.SelectedOs),
        new(10, "\u30A4\u30F3\u30B9\u30C8\u30FC\u30E9\u540D", row => row.InstallerName)
    ];

    private const int HeaderSearchMaxRows = 50;

    public void Append(string excelPath, ShipmentHistoryRecord row)
    {
        AppendMany(excelPath, [row]);
    }

    public void AppendMany(string excelPath, IEnumerable<ShipmentHistoryRecord> rows)
    {
        if (string.IsNullOrWhiteSpace(excelPath))
        {
            throw new ArgumentException("\u9001\u4ED8\u5C65\u6B74Excel\u30D1\u30B9\u304C\u672A\u8A2D\u5B9A\u3067\u3059\u3002", nameof(excelPath));
        }

        if (!File.Exists(excelPath))
        {
            throw new FileNotFoundException("\u9001\u4ED8\u5C65\u6B74Excel\u304C\u898B\u3064\u304B\u308A\u307E\u305B\u3093\u3002", excelPath);
        }

        if (!excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("\u9001\u4ED8\u5C65\u6B74Excel\u306F .xlsx \u30D5\u30A1\u30A4\u30EB\u3092\u6307\u5B9A\u3057\u3066\u304F\u3060\u3055\u3044\u3002");
        }

        var rowList = rows?.ToList() ?? new List<ShipmentHistoryRecord>();
        if (rowList.Count == 0)
        {
            throw new InvalidOperationException("\u8FFD\u8A18\u5BFE\u8C61\u306E\u9001\u4ED8\u5C65\u6B74\u30C7\u30FC\u30BF\u304C\u3042\u308A\u307E\u305B\u3093\u3002");
        }

        using var stream = new FileStream(excelPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet == null)
        {
            throw new InvalidOperationException("\u9001\u4ED8\u5C65\u6B74Excel\u306B\u30B7\u30FC\u30C8\u304C\u5B58\u5728\u3057\u307E\u305B\u3093\u3002");
        }

        var headerRow = FindHeaderRow(sheet);
        ValidateHeaders(sheet, headerRow);

        var nextRow = FindNextDataRow(sheet, headerRow);
        foreach (var row in rowList)
        {
            foreach (var column in Columns)
            {
                sheet.Cell(nextRow, column.ColumnIndex).Value = column.ValueSelector(row);
            }

            nextRow++;
        }

        SortDataByShipmentDateDescending(sheet, headerRow);
        EnsureAutoFilter(sheet, headerRow);
        workbook.Save();
    }

    public IReadOnlyList<ShipmentHistoryRecord> ReadAll(string excelPath)
    {
        if (string.IsNullOrWhiteSpace(excelPath))
        {
            throw new ArgumentException("\u9001\u4ED8\u5C65\u6B74Excel\u30D1\u30B9\u304C\u672A\u8A2D\u5B9A\u3067\u3059\u3002", nameof(excelPath));
        }

        if (!File.Exists(excelPath))
        {
            throw new FileNotFoundException("\u9001\u4ED8\u5C65\u6B74Excel\u304C\u898B\u3064\u304B\u308A\u307E\u305B\u3093\u3002", excelPath);
        }

        if (!excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("\u9001\u4ED8\u5C65\u6B74Excel\u306F .xlsx \u30D5\u30A1\u30A4\u30EB\u3092\u6307\u5B9A\u3057\u3066\u304F\u3060\u3055\u3044\u3002");
        }

        using var stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet == null)
        {
            throw new InvalidOperationException("\u9001\u4ED8\u5C65\u6B74Excel\u306B\u30B7\u30FC\u30C8\u304C\u5B58\u5728\u3057\u307E\u305B\u3093\u3002");
        }

        var headerRow = FindHeaderRow(sheet);
        ValidateHeaders(sheet, headerRow);

        var lastDataRow = FindLastDataRow(sheet, headerRow);
        var result = new List<ShipmentHistoryRecord>();
        for (var row = headerRow + 1; row <= lastDataRow; row++)
        {
            if (!HasDataRow(sheet, row))
            {
                continue;
            }

            result.Add(new ShipmentHistoryRecord
            {
                ShipmentDate = ParseShipmentDate(sheet.Cell(row, 1)),
                CompanyName = sheet.Cell(row, 2).GetString().Trim(),
                PersonName = sheet.Cell(row, 3).GetString().Trim(),
                Category = sheet.Cell(row, 4).GetString().Trim(),
                HelixVersion = sheet.Cell(row, 5).GetString().Trim(),
                Code = sheet.Cell(row, 6).GetString().Trim(),
                Name = sheet.Cell(row, 7).GetString().Trim(),
                CompatibilityVersion = sheet.Cell(row, 8).GetString().Trim(),
                SelectedOs = sheet.Cell(row, 9).GetString().Trim(),
                InstallerName = sheet.Cell(row, 10).GetString().Trim()
            });
        }

        return result;
    }

    private static int FindHeaderRow(IXLWorksheet sheet)
    {
        var lastUsedRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var maxRow = Math.Max(1, Math.Min(lastUsedRow, HeaderSearchMaxRows));

        for (var row = 1; row <= maxRow; row++)
        {
            if (IsHeaderRow(sheet, row))
            {
                return row;
            }
        }

        var expected = string.Join(" | ", Columns.Select(column => column.HeaderName));
        throw new InvalidOperationException(
            "\u9001\u4ED8\u5C65\u6B74Excel\u306E\u30D8\u30C3\u30C0\u30FC\u884C\u3092\u691C\u51FA\u3067\u304D\u307E\u305B\u3093\u3067\u3057\u305F\u3002"
            + Environment.NewLine
            + "A\u5217\uFF5EJ\u5217\u306B\u4EE5\u4E0B\u306E\u30D8\u30C3\u30C0\u30FC\u3092\u3053\u306E\u9806\u3067\u914D\u7F6E\u3057\u3066\u304F\u3060\u3055\u3044\u3002"
            + Environment.NewLine
            + expected
            + Environment.NewLine
            + $"\u63A2\u7D22\u7BC4\u56F2: 1\u884C\u76EE\uFF5E{maxRow}\u884C\u76EE");
    }

    private static bool IsHeaderRow(IXLWorksheet sheet, int rowNumber)
    {
        foreach (var column in Columns)
        {
            var actual = NormalizeHeader(sheet.Cell(rowNumber, column.ColumnIndex).GetString());
            var expected = NormalizeHeader(column.HeaderName);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateHeaders(IXLWorksheet sheet, int headerRow)
    {
        var mismatches = new List<string>();
        foreach (var column in Columns)
        {
            var actualRaw = sheet.Cell(headerRow, column.ColumnIndex).GetString().Trim();
            var actual = NormalizeHeader(actualRaw);
            var expected = NormalizeHeader(column.HeaderName);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                mismatches.Add($"{ToExcelColumnName(column.ColumnIndex)}\u5217: \u671F\u5F85='{column.HeaderName}' \u5B9F\u969B='{actualRaw}'");
            }
        }

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                $"\u9001\u4ED8\u5C65\u6B74Excel\u306E\u30D8\u30C3\u30C0\u30FC\u304C\u60F3\u5B9A\u3068\u4E00\u81F4\u3057\u307E\u305B\u3093\u3002(\u30D8\u30C3\u30C0\u30FC\u884C: {headerRow})"
                + Environment.NewLine
                + string.Join(Environment.NewLine, mismatches));
        }
    }

    private static int FindNextDataRow(IXLWorksheet sheet, int headerRow)
    {
        var lastUsedRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        var lastDataRow = headerRow;

        for (var row = headerRow + 1; row <= lastUsedRow; row++)
        {
            var hasData = Columns.Any(column =>
                !string.IsNullOrWhiteSpace(sheet.Cell(row, column.ColumnIndex).GetString()));
            if (hasData)
            {
                lastDataRow = row;
            }
        }

        return Math.Max(headerRow + 1, lastDataRow + 1);
    }

    private static void SortDataByShipmentDateDescending(IXLWorksheet sheet, int headerRow)
    {
        var lastDataRow = FindLastDataRow(sheet, headerRow);
        if (lastDataRow <= headerRow + 1)
        {
            return;
        }

        var dataRange = sheet.Range(headerRow + 1, 1, lastDataRow, Columns.Count);
        dataRange.Sort(1, XLSortOrder.Descending);
    }

    private static void EnsureAutoFilter(IXLWorksheet sheet, int headerRow)
    {
        var lastDataRow = FindLastDataRow(sheet, headerRow);
        var endRow = Math.Max(headerRow + 1, lastDataRow);
        var targetRange = sheet.Range(headerRow, 1, endRow, Columns.Count);

        var existingTable = sheet.Tables
            .FirstOrDefault(table =>
                table.RangeAddress.FirstAddress.RowNumber == headerRow
                && table.RangeAddress.FirstAddress.ColumnNumber == 1);

        if (existingTable != null)
        {
            existingTable.AutoFilter.Clear();
            existingTable.Resize(targetRange);
            existingTable.ShowAutoFilter = true;
            existingTable.AutoFilter.Clear();
            return;
        }

        sheet.AutoFilter.Clear();
        targetRange.SetAutoFilter();
        sheet.AutoFilter.Clear();
    }

    private static int FindLastDataRow(IXLWorksheet sheet, int headerRow)
    {
        var lastUsedRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        var lastDataRow = headerRow;

        for (var row = headerRow + 1; row <= lastUsedRow; row++)
        {
            var hasData = Columns.Any(column =>
                !string.IsNullOrWhiteSpace(sheet.Cell(row, column.ColumnIndex).GetString()));
            if (hasData)
            {
                lastDataRow = row;
            }
        }

        return lastDataRow;
    }

    private static bool HasDataRow(IXLWorksheet sheet, int row)
    {
        return Columns.Any(column => !string.IsNullOrWhiteSpace(sheet.Cell(row, column.ColumnIndex).GetString()));
    }

    private static DateTime ParseShipmentDate(IXLCell cell)
    {
        if (cell.TryGetValue<DateTime>(out var date))
        {
            return date.Date;
        }

        var text = cell.GetString().Trim();
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date))
        {
            return date.Date;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
        {
            return date.Date;
        }

        return DateTime.MinValue;
    }

    private static string NormalizeHeader(string value)
    {
        return (value ?? string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string ToExcelColumnName(int columnIndex)
    {
        var dividend = columnIndex;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }
}
