using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class HashService
{
    private readonly DatabaseService _database;

    public HashService(DatabaseService database)
    {
        _database = database;
    }

    public async Task<(string Hash, bool FromCache)> GetOrComputeSourceHashAsync(
        string sourcePath,
        long size,
        DateTime lastWriteUtc,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await _database.TryGetHashCacheAsync(sourcePath, size, lastWriteUtc);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            return (cached, true);
        }

        var hash = await ComputeSha256Async(sourcePath, size, progress, cancellationToken);
        await _database.UpsertHashCacheAsync(new HashCacheEntry
        {
            SourcePath = sourcePath,
            Size = size,
            LastWriteTimeUtc = lastWriteUtc,
            Sha256 = hash,
            CalculatedAtUtc = DateTime.UtcNow
        });

        return (hash, false);
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        long totalBytes,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        long processed = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            processed += read;
            progress?.Report(processed);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        if (totalBytes > 0)
        {
            progress?.Report(Math.Min(processed, totalBytes));
        }
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
    }
}
