using System;
using CommunityToolkit.Mvvm.ComponentModel;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.ViewModels;

public partial class HistoryItemViewModel : ObservableObject
{
    public HistoryItemViewModel(HistoryItem item)
    {
        BatchId = item.BatchId;
        Timestamp = item.TimestampUtc.ToLocalTime();
        Company = item.Company;
        HelixVersion = item.HelixVersion;
        OutputRoot = item.OutputRoot;
        Memo = item.Memo;
        ItemCount = item.ItemCount;
    }

    public long BatchId { get; }
    public DateTime Timestamp { get; }
    public string Company { get; }
    public string HelixVersion { get; }
    public string OutputRoot { get; }
    public string Memo { get; }
    public int ItemCount { get; }

    [ObservableProperty]
    private bool _isSelected;
}
