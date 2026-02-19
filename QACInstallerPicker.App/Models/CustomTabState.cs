using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class CustomTabState
{
    public string Name { get; set; } = string.Empty;
    public string ColumnsInput { get; set; } = string.Empty;
    public string NewDirectoryPath { get; set; } = string.Empty;
    public List<CustomTabRowState> Rows { get; set; } = new();
}

public class CustomTabRowState
{
    public bool IsSelected { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public Dictionary<string, string> ColumnValues { get; set; } = new();
}
