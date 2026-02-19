using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QACInstallerPicker.App.ViewModels;

public sealed record CustomSelectedFile(
    string TabName,
    string SourcePath,
    string FileName,
    IReadOnlyDictionary<string, string> ColumnValues);

public partial class CustomTabViewModel : ObservableObject
{
    public const string SelectColumnName = "\u9078\u629E";
    public const string FolderColumnName = "\u30D5\u30A9\u30EB\u30C0";
    public const string FileNameColumnName = "\u30D5\u30A1\u30A4\u30EB\u540D";
    public const string SourcePathColumnName = "__SourcePath";

    private DataTable _table;

    public CustomTabViewModel(string name, IEnumerable<string>? customColumns = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "\u30AB\u30B9\u30BF\u30E0" : name.Trim();
        ColumnsInput = customColumns == null
            ? string.Empty
            : string.Join(", ", NormalizeColumnNames(customColumns));

        _table = CreateTable(ParseColumnNames(ColumnsInput));
        AttachTableHandlers(_table);
    }

    [ObservableProperty]
    private string _name = "\u30AB\u30B9\u30BF\u30E0";

    [ObservableProperty]
    private string _columnsInput = string.Empty;

    [ObservableProperty]
    private string _newFilePath = string.Empty;

    [ObservableProperty]
    private string _newDirectoryPath = string.Empty;

    public DataView RowsView => _table.DefaultView;

    public event EventHandler? Changed;

    public void ApplyColumnsFromInput()
    {
        ApplyColumns(ParseColumnNames(ColumnsInput));
    }

    public void ApplyColumns(IEnumerable<string> columnNames)
    {
        var customColumns = NormalizeColumnNames(columnNames).ToList();
        ColumnsInput = string.Join(", ", customColumns);

        var oldRows = _table.Rows.Cast<DataRow>()
            .Select(row => new
            {
                Selected = row.Field<bool?>(SelectColumnName) ?? false,
                Folder = row.Field<string>(FolderColumnName) ?? string.Empty,
                SourcePath = row.Field<string>(SourcePathColumnName) ?? string.Empty,
                FileName = row.Field<string>(FileNameColumnName) ?? string.Empty,
                CustomValues = GetCustomValues(row)
            })
            .ToList();

        DetachTableHandlers(_table);
        _table = CreateTable(customColumns);
        AttachTableHandlers(_table);

        foreach (var old in oldRows)
        {
            var row = _table.NewRow();
            row[SelectColumnName] = old.Selected;
            row[FolderColumnName] = old.Folder;
            row[SourcePathColumnName] = old.SourcePath;
            row[FileNameColumnName] = old.FileName;
            foreach (var pair in old.CustomValues)
            {
                if (_table.Columns.Contains(pair.Key))
                {
                    row[pair.Key] = pair.Value;
                }
            }

            _table.Rows.Add(row);
        }

        OnPropertyChanged(nameof(RowsView));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool AddFile(string path, bool selectByDefault = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = path.Trim();
        var existing = _table.Rows.Cast<DataRow>()
            .FirstOrDefault(row => string.Equals(
                row.Field<string>(SourcePathColumnName),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (selectByDefault)
            {
                existing[SelectColumnName] = true;
            }
            return false;
        }

        var row = _table.NewRow();
        row[SelectColumnName] = selectByDefault;
        row[FolderColumnName] = GetNearestFolderName(normalizedPath);
        row[FileNameColumnName] = Path.GetFileName(normalizedPath);
        row[SourcePathColumnName] = normalizedPath;
        _table.Rows.Add(row);
        return true;
    }

    public bool UnselectByPath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        var row = _table.Rows.Cast<DataRow>()
            .FirstOrDefault(item => string.Equals(
                item.Field<string>(SourcePathColumnName),
                sourcePath,
                StringComparison.OrdinalIgnoreCase));
        if (row == null)
        {
            return false;
        }

        row[SelectColumnName] = false;
        return true;
    }

    public IReadOnlyList<CustomSelectedFile> GetSelectedFiles()
    {
        var selected = new List<CustomSelectedFile>();
        foreach (var row in _table.Rows.Cast<DataRow>())
        {
            var isSelected = row.Field<bool?>(SelectColumnName) ?? false;
            if (!isSelected)
            {
                continue;
            }

            var sourcePath = row.Field<string>(SourcePathColumnName) ?? string.Empty;
            var fileName = row.Field<string>(FileNameColumnName) ?? Path.GetFileName(sourcePath);
            selected.Add(new CustomSelectedFile(
                Name,
                sourcePath,
                fileName,
                GetCustomValues(row)));
        }

        return selected;
    }

    public bool HasFile(string sourcePath)
    {
        return _table.Rows.Cast<DataRow>().Any(row => string.Equals(
            row.Field<string>(SourcePathColumnName),
            sourcePath,
            StringComparison.OrdinalIgnoreCase));
    }

    private static DataTable CreateTable(IEnumerable<string> customColumns)
    {
        var table = new DataTable();
        table.Columns.Add(SelectColumnName, typeof(bool));
        table.Columns.Add(FolderColumnName, typeof(string));
        table.Columns.Add(FileNameColumnName, typeof(string));
        table.Columns.Add(SourcePathColumnName, typeof(string));
        var sourcePathColumn = table.Columns[SourcePathColumnName];
        if (sourcePathColumn != null)
        {
            sourcePathColumn.ColumnMapping = MappingType.Hidden;
        }

        foreach (var column in customColumns)
        {
            if (table.Columns.Contains(column))
            {
                continue;
            }

            table.Columns.Add(column, typeof(string));
        }

        return table;
    }

    private static IReadOnlyDictionary<string, string> GetCustomValues(DataRow row)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in row.Table.Columns)
        {
            if (!IsCustomColumn(column.ColumnName))
            {
                continue;
            }

            values[column.ColumnName] = row[column] switch
            {
                null => string.Empty,
                DBNull => string.Empty,
                var value => value.ToString() ?? string.Empty
            };
        }

        return values;
    }

    private static bool IsCustomColumn(string columnName)
    {
        return !columnName.Equals(SelectColumnName, StringComparison.OrdinalIgnoreCase)
               && !columnName.Equals(FolderColumnName, StringComparison.OrdinalIgnoreCase)
               && !columnName.Equals(FileNameColumnName, StringComparison.OrdinalIgnoreCase)
               && !columnName.Equals(SourcePathColumnName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseColumnNames(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<string>();
        }

        var tokens = input
            .Split(new[] { ',', '、', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim());

        return NormalizeColumnNames(tokens).ToList();
    }

    private static IReadOnlyList<string> NormalizeColumnNames(IEnumerable<string> columnNames)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in columnNames)
        {
            var trimmed = name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (!IsCustomColumn(trimmed))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string GetNearestFolderName(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "-";
        }

        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "-";
        }

        var folder = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(folder) ? "-" : folder;
    }

    private void AttachTableHandlers(DataTable table)
    {
        table.RowChanged += OnTableChanged;
        table.RowDeleted += OnTableChanged;
        table.ColumnChanged += OnTableColumnChanged;
    }

    private void DetachTableHandlers(DataTable table)
    {
        table.RowChanged -= OnTableChanged;
        table.RowDeleted -= OnTableChanged;
        table.ColumnChanged -= OnTableColumnChanged;
    }

    private void OnTableChanged(object? sender, DataRowChangeEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnTableColumnChanged(object? sender, DataColumnChangeEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
