using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.ViewModels;

namespace QACInstallerPicker.App.Services;

public enum TransferActionRequest
{
    None,
    Pause,
    Cancel
}

public class TransferManager
{
    private readonly DatabaseService _database;
    private readonly HashService _hashService;
    private readonly CopyService _copyService;
    private readonly SemaphoreSlim _semaphore;
    private readonly object _concurrencyLock = new();
    private int _maxConcurrent;
    private int _pendingReduction;
    private readonly ConcurrentDictionary<long, Task> _running = new();
    private readonly object _queueLock = new();
    private readonly LinkedList<TransferItemViewModel> _pendingQueue = new();
    private readonly Dictionary<long, LinkedListNode<TransferItemViewModel>> _pendingIndex = new();

    public TransferManager(DatabaseService database, HashService hashService, CopyService copyService, int maxConcurrent)
    {
        _database = database;
        _hashService = hashService;
        _copyService = copyService;
        _maxConcurrent = Math.Max(1, maxConcurrent);
        _semaphore = new SemaphoreSlim(_maxConcurrent);
    }

    public void UpdateMaxConcurrent(int maxConcurrent)
    {
        var normalized = Math.Max(1, maxConcurrent);
        int delta;
        lock (_concurrencyLock)
        {
            delta = normalized - _maxConcurrent;
            _maxConcurrent = normalized;
            if (delta > 0)
            {
                if (_pendingReduction > 0)
                {
                    var offset = Math.Min(delta, _pendingReduction);
                    _pendingReduction -= offset;
                    delta -= offset;
                }
            }
        }

        if (delta > 0)
        {
            _semaphore.Release(delta);
            TryStartNext();
            return;
        }

        if (delta == 0)
        {
            return;
        }

        var reduction = -delta;
        var consumed = 0;
        while (consumed < reduction && _semaphore.Wait(0))
        {
            consumed++;
        }

        var remaining = reduction - consumed;
        if (remaining > 0)
        {
            lock (_concurrencyLock)
            {
                _pendingReduction += remaining;
            }
        }
    }

    public Task StartAsync(TransferItemViewModel item, bool prioritize = false)
    {
        if (_running.ContainsKey(item.Record.Id))
        {
            return Task.CompletedTask;
        }

        item.PrepareForStart();
        EnqueuePending(item, prioritize || item.Status == TransferStatus.Paused);
        TryStartNext();
        return Task.CompletedTask;
    }

    public void RequestPause(TransferItemViewModel item)
    {
        item.RequestedAction = TransferActionRequest.Pause;
        item.CancelCurrent();
        if (RemovePending(item))
        {
            _ = HandleCancellationAsync(item);
        }
    }

    public void RequestCancel(TransferItemViewModel item)
    {
        item.RequestedAction = TransferActionRequest.Cancel;
        item.CancelCurrent();
        if (RemovePending(item))
        {
            _ = HandleCancellationAsync(item);
        }
    }

    public Task RetryAsync(TransferItemViewModel item)
    {
        item.ResetForRetry();
        return StartAsync(item);
    }

    private async Task RunTransferAsync(TransferItemViewModel item)
    {
        try
        {
            await ExecuteTransferAsync(item);
        }
        catch (OperationCanceledException)
        {
            await HandleCancellationAsync(item);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(item, ex);
        }
        finally
        {
            _running.TryRemove(item.Record.Id, out _);
            var release = true;
            lock (_concurrencyLock)
            {
                if (_pendingReduction > 0)
                {
                    _pendingReduction--;
                    release = false;
                }
            }

            if (release)
            {
                _semaphore.Release();
            }
            TryStartNext();
        }
    }

    private void EnqueuePending(TransferItemViewModel item, bool prioritize)
    {
        lock (_queueLock)
        {
            if (_pendingIndex.TryGetValue(item.Record.Id, out var existing))
            {
                if (prioritize)
                {
                    _pendingQueue.Remove(existing);
                    _pendingQueue.AddFirst(existing);
                }
                return;
            }

            var node = new LinkedListNode<TransferItemViewModel>(item);
            if (prioritize)
            {
                _pendingQueue.AddFirst(node);
            }
            else
            {
                _pendingQueue.AddLast(node);
            }

            _pendingIndex[item.Record.Id] = node;
        }
    }

    private bool RemovePending(TransferItemViewModel item)
    {
        lock (_queueLock)
        {
            if (_pendingIndex.TryGetValue(item.Record.Id, out var node))
            {
                _pendingQueue.Remove(node);
                _pendingIndex.Remove(item.Record.Id);
                return true;
            }
        }

        return false;
    }

    private void TryStartNext()
    {
        while (true)
        {
            TransferItemViewModel? next = null;
            lock (_queueLock)
            {
                if (!_semaphore.Wait(0))
                {
                    return;
                }

                if (_pendingQueue.Count == 0)
                {
                    _semaphore.Release();
                    return;
                }

                var node = _pendingQueue.First;
                if (node != null)
                {
                    next = node.Value;
                    _pendingQueue.RemoveFirst();
                    _pendingIndex.Remove(next.Record.Id);
                }
            }

            if (next == null)
            {
                _semaphore.Release();
                return;
            }

            if (next.RequestedAction == TransferActionRequest.Cancel)
            {
                _semaphore.Release();
                _ = HandleCancellationAsync(next);
                continue;
            }

            var task = Task.Run(() => RunTransferAsync(next));
            _running[next.Record.Id] = task;
        }
    }

    private async Task ExecuteTransferAsync(TransferItemViewModel item)
    {
        var record = item.Record;
        var sourcePath = record.AssetSourcePath;
        if (!File.Exists(sourcePath))
        {
            item.MarkFailed("SourceNotFound");
            await _database.UpdateTransferItemAsync(record);
            return;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var partPath = record.DestPath + ".part";
        var sourceChanged = record.Size != 0 &&
                            (sourceInfo.Length != record.Size ||
                             sourceInfo.LastWriteTimeUtc != record.SourceLastWriteTimeUtc);
        if (sourceChanged)
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            record.BytesCopied = 0;
            record.DestHashSha256 = null;
            record.SourceHashSha256 = null;
            record.VerifyResult = VerifyResult.NotChecked;
        }

        record.Size = sourceInfo.Length;
        record.SourceLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;

        if (record.StartedAtUtc == null)
        {
            record.StartedAtUtc = DateTime.UtcNow;
        }

        var canSkipHash = !sourceChanged && !string.IsNullOrWhiteSpace(record.SourceHashSha256);
        if (!canSkipHash)
        {
            item.SetStatus(TransferStatus.HashingSource);
            item.UpdateSourceHashStatus("Calculating");
            await _database.UpdateTransferItemAsync(record);

            var hashTracker = new TransferSpeedTracker();
            var hashProgress = new Progress<long>(bytesHashed =>
            {
                var now = DateTime.UtcNow;
                hashTracker.AddSample(now, bytesHashed);
                var (bytesPerSecond, secondsWindow) = hashTracker.GetSpeed();
                item.ReportHashingProgress(bytesHashed, record.Size, bytesPerSecond, secondsWindow);
            });

            var (sourceHash, fromCache) = await _hashService.GetOrComputeSourceHashAsync(
                sourcePath,
                record.Size,
                record.SourceLastWriteTimeUtc,
                hashProgress,
                item.CancellationToken);

            record.SourceHashSha256 = sourceHash;
            item.UpdateSourceHashStatus(fromCache ? "Cached" : "Calculated");
            await _database.UpdateTransferItemAsync(record);
        }
        else
        {
            item.UpdateSourceHashStatus("Cached");
        }

        item.SetStatus(TransferStatus.Downloading);
        item.UpdateLocalHashStatus("Calculating");
        await _database.UpdateTransferItemAsync(record);

        var progressTracker = new TransferSpeedTracker();
        var progress = new Progress<CopyProgress>(p =>
        {
            var now = DateTime.UtcNow;
            progressTracker.AddSample(now, p.BytesCopied);
            var (bytesPerSecond, secondsWindow) = progressTracker.GetSpeed();
            item.ReportProgress(p.BytesCopied, p.TotalBytes, bytesPerSecond, secondsWindow);
        });

        CopyResult copyResult;
        try
        {
        copyResult = await _copyService.CopyWithResumeAsync(
            sourcePath,
            record.DestPath,
            record.Size,
            progress,
            item.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            await HandleCancellationAsync(item);
            return;
        }

        record.BytesCopied = copyResult.BytesCopied;
        record.DestHashSha256 = copyResult.LocalHashSha256;
        item.UpdateLocalHashStatus("Calculated");
        await _database.UpdateTransferItemAsync(record);

        var afterInfo = new FileInfo(sourcePath);
        if (afterInfo.Length != record.Size || afterInfo.LastWriteTimeUtc != record.SourceLastWriteTimeUtc)
        {
            item.MarkFailed("SourceChanged");
            await _database.UpdateTransferItemAsync(record);
            return;
        }

        item.SetStatus(TransferStatus.Verifying);
        await _database.UpdateTransferItemAsync(record);

        if (!string.Equals(record.SourceHashSha256, record.DestHashSha256, StringComparison.OrdinalIgnoreCase))
        {
            item.MarkFailed("HashMismatch");
            record.VerifyResult = VerifyResult.Ng;
            await _database.UpdateTransferItemAsync(record);
            return;
        }

        record.VerifyResult = VerifyResult.Ok;

        try
        {
            var finalPath = record.DestPath;
            var finalPartPath = copyResult.PartPath;
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(finalPartPath, finalPath);
        }
        catch (Exception ex)
        {
            item.MarkFailed($"FinalizeFailed:{ex.Message}");
            await _database.UpdateTransferItemAsync(record);
            return;
        }

        item.MarkCompleted();
        await _database.UpdateTransferItemAsync(record);
    }

    private async Task HandleCancellationAsync(TransferItemViewModel item)
    {
        if (item.RequestedAction == TransferActionRequest.Cancel)
        {
            item.MarkCanceled();
            var partPath = item.Record.DestPath + ".part";
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }
        }
        else
        {
            item.MarkPaused();
        }

        await _database.UpdateTransferItemAsync(item.Record);
    }

    private async Task HandleFailureAsync(TransferItemViewModel item, Exception ex)
    {
        AppLogger.LogError("TransferFailed", ex);
        try
        {
            item.MarkFailed($"TransferFailed:{ex.Message}");
        }
        catch (Exception markEx)
        {
            AppLogger.LogError("TransferFailedMark", markEx);
        }

        try
        {
            await _database.UpdateTransferItemAsync(item.Record);
        }
        catch (Exception dbEx)
        {
            AppLogger.LogError("TransferFailedUpdate", dbEx);
        }
    }

    private sealed class TransferSpeedTracker
    {
        private readonly object _lock = new();
        private readonly System.Collections.Generic.List<(DateTime Time, long Bytes)> _samples = new();

        public void AddSample(DateTime time, long bytes)
        {
            lock (_lock)
            {
                _samples.Add((time, bytes));
                var cutoff = time.AddSeconds(-10);
                _samples.RemoveAll(s => s.Time < cutoff);
            }
        }

        public (double BytesPerSecond, double SecondsWindow) GetSpeed()
        {
            lock (_lock)
            {
                if (_samples.Count < 2)
                {
                    return (0, 0);
                }

                var first = _samples[0];
                var last = _samples[^1];
                var seconds = (last.Time - first.Time).TotalSeconds;
                if (seconds <= 0)
                {
                    return (0, 0);
                }

                var bytes = last.Bytes - first.Bytes;
                return (bytes / seconds, seconds);
            }
        }
    }
}
