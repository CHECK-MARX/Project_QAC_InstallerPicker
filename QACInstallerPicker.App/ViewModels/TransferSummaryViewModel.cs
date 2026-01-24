using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.ViewModels;

public partial class TransferSummaryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _overallProgressText = "0%";

    [ObservableProperty]
    private string _remainingCountText = "0";

    [ObservableProperty]
    private string _totalSpeedText = "0.00";

    [ObservableProperty]
    private string _concurrentText = "0";

    public void Update(IReadOnlyCollection<TransferItemViewModel> items, int maxConcurrent)
    {
        if (items.Count == 0)
        {
            OverallProgressText = "0%";
            RemainingCountText = "0";
            TotalSpeedText = "0.00";
            ConcurrentText = "0 / " + maxConcurrent;
            return;
        }

        var totalBytes = items.Sum(i => i.Record.Size);
        var copiedBytes = items.Sum(i => i.Record.BytesCopied);
        var percent = totalBytes == 0 ? 0 : copiedBytes * 100d / totalBytes;
        OverallProgressText = percent.ToString("F1", CultureInfo.InvariantCulture) + "%";

        var remaining = items.Count(i => i.Status is not (TransferStatus.Completed or TransferStatus.Canceled));
        RemainingCountText = remaining.ToString(CultureInfo.InvariantCulture);

        var totalSpeed = items.Sum(i => i.SpeedMbpsValue);
        TotalSpeedText = totalSpeed.ToString("F2", CultureInfo.InvariantCulture);

        var concurrent = items.Count(i => i.Status is TransferStatus.HashingSource or TransferStatus.Downloading or TransferStatus.Verifying);
        ConcurrentText = $"{concurrent} / {maxConcurrent}";
    }
}
