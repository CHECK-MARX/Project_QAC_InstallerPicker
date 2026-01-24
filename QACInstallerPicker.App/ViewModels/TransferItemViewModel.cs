using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;

namespace QACInstallerPicker.App.ViewModels;

public partial class TransferItemViewModel : ObservableObject
{
    private readonly TransferManager _manager;
    private CancellationTokenSource? _cts;

    public TransferItemViewModel(TransferItemRecord record, TransferManager manager)
    {
        Record = record;
        _manager = manager;
        _status = record.Status;
        _progressPercent = record.Size == 0 ? 0 : record.BytesCopied * 100d / record.Size;
        _bytesText = FormatBytes(record.BytesCopied, record.Size);
        _speedText = "--";
        _etaText = "--";
        _remainingText = "--";
        _startedAtText = record.StartedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        _estimatedEndText = "--";
        _sourceHashStatus = string.IsNullOrWhiteSpace(record.SourceHashSha256) ? "NotChecked" : "Calculated";
        _localHashStatus = string.IsNullOrWhiteSpace(record.DestHashSha256) ? "NotChecked" : "Calculated";
        _verifyResultText = record.VerifyResult.ToString();
        _errorReason = record.ErrorReason ?? string.Empty;
    }

    public TransferItemRecord Record { get; }

    public string Company => Record.Company;
    public string FileName => Path.GetFileName(Record.DestPath);
    public string SourcePath => Record.AssetSourcePath;
    public string DestPath => Record.DestPath;

    public TransferActionRequest RequestedAction { get; set; }

    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    public event EventHandler? ProgressChanged;

    [ObservableProperty]
    private TransferStatus _status;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _bytesText = "0 / 0";

    [ObservableProperty]
    private string _speedText = "--";

    [ObservableProperty]
    private double _speedMbpsValue;

    [ObservableProperty]
    private string _etaText = "--";

    [ObservableProperty]
    private string _estimatedEndText = "--";

    [ObservableProperty]
    private string _remainingText = "--";

    [ObservableProperty]
    private string _startedAtText = "--";

    [ObservableProperty]
    private string _sourceHashStatus = "NotChecked";

    [ObservableProperty]
    private string _localHashStatus = "NotChecked";

    [ObservableProperty]
    private string _verifyResultText = "NotChecked";

    [ObservableProperty]
    private string _errorReason = string.Empty;

    [RelayCommand]
    private void Pause()
    {
        if (Status is TransferStatus.Downloading or TransferStatus.HashingSource or TransferStatus.Verifying)
        {
            _manager.RequestPause(this);
        }
        else if (Status == TransferStatus.Queued)
        {
            _manager.RequestPause(this);
        }
    }

    [RelayCommand]
    private async Task Resume()
    {
        if (Status is TransferStatus.Paused or TransferStatus.Failed or TransferStatus.Queued)
        {
            await _manager.StartAsync(this, Status == TransferStatus.Paused);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Status is TransferStatus.Completed or TransferStatus.Canceled)
        {
            return;
        }

        _manager.RequestCancel(this);
    }

    [RelayCommand]
    private async Task Retry()
    {
        if (Status == TransferStatus.Failed)
        {
            await _manager.RetryAsync(this);
        }
    }

    public void PrepareForStart()
    {
        RequestedAction = TransferActionRequest.None;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        if (Record.StartedAtUtc == null)
        {
            Record.StartedAtUtc = DateTime.UtcNow;
            StartedAtText = Record.StartedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public void CancelCurrent()
    {
        _cts?.Cancel();
    }

    public void ResetForRetry()
    {
        Record.BytesCopied = 0;
        Record.DestHashSha256 = null;
        Record.ErrorReason = null;
        Record.VerifyResult = VerifyResult.NotChecked;
        Record.StartedAtUtc = null;
        Record.CompletedAtUtc = null;
        ErrorReason = string.Empty;
        VerifyResultText = VerifyResult.NotChecked.ToString();
        BytesText = FormatBytes(0, Record.Size);
        ProgressPercent = 0;
        SpeedText = "--";
        SpeedMbpsValue = 0;
        EtaText = "--";
        RemainingText = "--";
        EstimatedEndText = "--";
        StartedAtText = "--";
        Record.Status = TransferStatus.Queued;
        Status = TransferStatus.Queued;
    }

    public void SetStatus(TransferStatus status)
    {
        Record.Status = status;
        RunOnUi(() =>
        {
            Status = status;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void ReportProgress(long bytesCopied, long totalBytes, double bytesPerSecond, double secondsWindow)
    {
        Record.BytesCopied = bytesCopied;
        Record.Size = totalBytes;
        RunOnUi(() =>
        {
            UpdateProgressDisplay(bytesCopied, totalBytes, bytesPerSecond, secondsWindow, true);
        });
    }

    public void ReportHashingProgress(long bytesProcessed, long totalBytes, double bytesPerSecond, double secondsWindow)
    {
        RunOnUi(() =>
        {
            UpdateProgressDisplay(bytesProcessed, totalBytes, bytesPerSecond, secondsWindow, false);
        });
    }

    public void MarkPaused()
    {
        Record.Status = TransferStatus.Paused;
        RunOnUi(() =>
        {
            Status = TransferStatus.Paused;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void MarkCanceled()
    {
        Record.Status = TransferStatus.Canceled;
        Record.CompletedAtUtc = DateTime.UtcNow;
        RunOnUi(() =>
        {
            Status = TransferStatus.Canceled;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void MarkFailed(string reason)
    {
        Record.Status = TransferStatus.Failed;
        Record.ErrorReason = reason;
        Record.CompletedAtUtc = DateTime.UtcNow;
        RunOnUi(() =>
        {
            Status = TransferStatus.Failed;
            ErrorReason = reason;
            VerifyResultText = Record.VerifyResult.ToString();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void MarkCompleted()
    {
        Record.Status = TransferStatus.Completed;
        Record.CompletedAtUtc = DateTime.UtcNow;
        RunOnUi(() =>
        {
            Status = TransferStatus.Completed;
            VerifyResultText = Record.VerifyResult.ToString();
            ErrorReason = string.Empty;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void UpdateSourceHashStatus(string status)
    {
        RunOnUi(() => SourceHashStatus = status);
    }

    public void UpdateLocalHashStatus(string status)
    {
        RunOnUi(() => LocalHashStatus = status);
    }

    private static string FormatBytes(long copied, long total)
    {
        return $"{copied:N0} / {total:N0}";
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private void UpdateProgressDisplay(
        long bytesProcessed,
        long totalBytes,
        double bytesPerSecond,
        double secondsWindow,
        bool updateSpeedValue)
    {
        ProgressPercent = totalBytes == 0 ? 0 : bytesProcessed * 100d / totalBytes;
        BytesText = FormatBytes(bytesProcessed, totalBytes);

        if (bytesPerSecond > 0 && secondsWindow >= 2)
        {
            var mbps = bytesPerSecond / (1024d * 1024d);
            SpeedText = mbps.ToString("F2", CultureInfo.InvariantCulture);
            SpeedMbpsValue = updateSpeedValue ? mbps : 0;
            var remainingBytes = Math.Max(0, totalBytes - bytesProcessed);
            var etaSeconds = remainingBytes / bytesPerSecond;
            var remaining = TimeSpan.FromSeconds(Math.Max(0, etaSeconds));
            EtaText = $"{remaining:hh\\:mm\\:ss}";
            RemainingText = EtaText;
            EstimatedEndText = DateTime.Now.Add(remaining).ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
            SpeedText = "--";
            SpeedMbpsValue = 0;
            EtaText = "--";
            RemainingText = "--";
            EstimatedEndText = "--";
        }

        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
}

