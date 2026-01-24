using System;

namespace QACInstallerPicker.App.Models;

public class HistoryItem
{
    public long BatchId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Company { get; set; } = string.Empty;
    public string HelixVersion { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}
