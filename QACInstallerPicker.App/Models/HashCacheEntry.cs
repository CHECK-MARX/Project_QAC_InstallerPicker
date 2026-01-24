using System;

namespace QACInstallerPicker.App.Models;

public class HashCacheEntry
{
    public string SourcePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CalculatedAtUtc { get; set; }
}
