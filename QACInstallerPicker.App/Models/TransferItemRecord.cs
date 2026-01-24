using System;

namespace QACInstallerPicker.App.Models;

public class TransferItemRecord
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public string Company { get; set; } = string.Empty;
    public string LogicalKey { get; set; } = string.Empty;
    public string AssetSourcePath { get; set; } = string.Empty;
    public string DestPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Queued;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long BytesCopied { get; set; }
    public DateTime SourceLastWriteTimeUtc { get; set; }
    public string? SourceHashSha256 { get; set; }
    public string? DestHashSha256 { get; set; }
    public VerifyResult VerifyResult { get; set; } = VerifyResult.NotChecked;
    public string? ErrorReason { get; set; }
}
