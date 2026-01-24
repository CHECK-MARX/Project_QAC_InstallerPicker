namespace QACInstallerPicker.App.Models;

public class CopyProgress
{
    public long BytesCopied { get; init; }
    public long TotalBytes { get; init; }
}

public class CopyResult
{
    public long BytesCopied { get; init; }
    public string LocalHashSha256 { get; init; } = string.Empty;
    public string PartPath { get; init; } = string.Empty;
}
