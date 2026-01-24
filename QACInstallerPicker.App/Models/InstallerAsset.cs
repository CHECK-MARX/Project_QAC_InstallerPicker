using System;
using System.IO;

namespace QACInstallerPicker.App.Models;

public class InstallerAsset
{
    public string SourcePath { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public OsType Os { get; init; }
    public bool IsZip { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    public string FileName => Path.GetFileName(SourcePath);
}
