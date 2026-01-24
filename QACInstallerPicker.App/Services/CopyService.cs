using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class CopyService
{
    public async Task<CopyResult> CopyWithResumeAsync(
        string sourcePath,
        string destPath,
        long sourceSize,
        IProgress<CopyProgress> progress,
        CancellationToken cancellationToken)
    {
        var partPath = destPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? string.Empty);

        long offset = 0;
        if (File.Exists(partPath))
        {
            offset = new FileInfo(partPath).Length;
            if (offset > sourceSize)
            {
                File.Delete(partPath);
                offset = 0;
            }
        }

        if (offset > 0)
        {
            progress.Report(new CopyProgress { BytesCopied = offset, TotalBytes = sourceSize });
        }

        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (offset > 0)
        {
            // Resume時は既存.partを読み込んでハッシュ状態を再構築する
            using var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var partBuffer = new byte[1024 * 1024];
            int partRead;
            while ((partRead = await partStream.ReadAsync(partBuffer, cancellationToken)) > 0)
            {
                incrementalHash.AppendData(partBuffer, 0, partRead);
            }
        }

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        sourceStream.Seek(offset, SeekOrigin.Begin);

        using var destStream = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        destStream.Seek(offset, SeekOrigin.Begin);

        var buffer = new byte[1024 * 1024];
        long copied = offset;
        int read;

        while ((read = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            incrementalHash.AppendData(buffer, 0, read);
            copied += read;
            progress.Report(new CopyProgress { BytesCopied = copied, TotalBytes = sourceSize });
            cancellationToken.ThrowIfCancellationRequested();
        }

        var localHash = Convert.ToHexString(incrementalHash.GetHashAndReset());
        return new CopyResult
        {
            BytesCopied = copied,
            LocalHashSha256 = localHash,
            PartPath = partPath
        };
    }
}
