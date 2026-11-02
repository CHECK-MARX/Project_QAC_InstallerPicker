using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QACInstallerPicker.App.Services;

public sealed record TransferPreflightTarget(string SourcePath, string DestinationPath);

public sealed record TransferPreflightResult(IReadOnlyList<string> Errors, long SourceBytes)
{
    public bool CanStart => Errors.Count == 0;
}

public static class TransferPreflightService
{
    public static TransferPreflightResult Check(
        IEnumerable<TransferPreflightTarget> targets,
        IEnumerable<string>? additionalSources = null,
        long? availableBytes = null)
    {
        var errors = new List<string>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceCopies = new List<string>();
        var targetList = targets.ToList();

        foreach (var target in targetList)
        {
            try
            {
                var destination = Path.GetFullPath(target.DestinationPath);
                if (!destinations.Add(destination))
                {
                    errors.Add($"出力先が重複: {destination}");
                }

                if (!string.IsNullOrWhiteSpace(target.SourcePath))
                {
                    var source = Path.GetFullPath(target.SourcePath);
                    sources.Add(source);
                    sourceCopies.Add(source);
                    if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"元ファイルと出力先が同じ: {source}");
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"無効なパス: {target.SourcePath} → {target.DestinationPath}");
            }
        }

        if (additionalSources != null)
        {
            foreach (var path in additionalSources)
            {
                try
                {
                    var source = Path.GetFullPath(path);
                    sources.Add(source);
                    sourceCopies.Add(source);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    errors.Add($"無効な元ファイルパス: {path}");
                }
            }
        }

        foreach (var source in sourceCopies)
        {
            if (destinations.Contains(source))
            {
                errors.Add($"元ファイルが別の出力先と重複: {source}");
            }
        }

        long totalBytes = 0;
        foreach (var source in sources)
        {
            try
            {
                var info = new FileInfo(source);
                if (!info.Exists)
                {
                    errors.Add($"元ファイルが見つかりません: {source}");
                    continue;
                }

                totalBytes = checked(totalBytes + info.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
            {
                errors.Add($"元ファイルを確認できません: {source} ({ex.Message})");
            }
        }

        if (availableBytes.HasValue && totalBytes > availableBytes.Value)
        {
            errors.Add($"出力先の空き容量が不足: 必要約 {totalBytes / 1024d / 1024d:N1} MB / 空き {availableBytes.Value / 1024d / 1024d:N1} MB");
        }

        return new TransferPreflightResult(errors, totalBytes);
    }
}
