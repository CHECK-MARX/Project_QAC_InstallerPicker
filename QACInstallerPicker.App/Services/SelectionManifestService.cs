using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace QACInstallerPicker.App.Services;

public sealed record SelectionManifestEntry(
    string Kind, string Key, string SourcePath, string DestinationPath,
    long Size, DateTime LastWriteTimeUtc, bool SourceExists);

public sealed record SelectionManifestDocument(
    DateTime CreatedAtUtc, IReadOnlyList<SelectionManifestEntry> Items);

public sealed record SelectionManifestDifference(
    string Change, string Key, SelectionManifestEntry? Previous, SelectionManifestEntry? Current);

public static class SelectionManifestService
{
    public static SelectionManifestDocument Create(IEnumerable<SelectionManifestEntry> items)
    {
        var uniqueItems = items.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                .Select((item, index) => index == 0 ? item : item with { Key = $"{item.Key}|#{index + 1}" }))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SelectionManifestDocument(DateTime.UtcNow, uniqueItems);
    }

    public static SelectionManifestEntry CreateEntry(string kind, string key, string sourcePath, string destinationPath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (info.Exists)
            {
                return new SelectionManifestEntry(kind, key, sourcePath, destinationPath,
                    info.Length, info.LastWriteTimeUtc, true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // Record the missing or unreadable source in the manifest.
        }
        return new SelectionManifestEntry(kind, key, sourcePath, destinationPath, 0, default, false);
    }

    public static void Save(string path, SelectionManifestDocument document)
    {
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static SelectionManifestDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<SelectionManifestDocument>(File.ReadAllText(path));
        if (document?.Items == null)
        {
            throw new InvalidDataException("明細ファイルの形式が正しくありません。");
        }
        return document;
    }

    public static IReadOnlyList<SelectionManifestDifference> Compare(
        SelectionManifestDocument previous, SelectionManifestDocument current)
    {
        var oldItems = previous.Items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var newItems = current.Items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var differences = new List<SelectionManifestDifference>();
        foreach (var key in oldItems.Keys.Union(newItems.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            oldItems.TryGetValue(key, out var oldItem);
            newItems.TryGetValue(key, out var newItem);
            var change = oldItem == null ? "追加" : newItem == null ? "削除" :
                oldItem.SourcePath.Equals(newItem.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                oldItem.Size == newItem.Size &&
                oldItem.LastWriteTimeUtc == newItem.LastWriteTimeUtc &&
                oldItem.SourceExists == newItem.SourceExists ? "同一" : "変更";
            if (change != "同一")
            {
                differences.Add(new SelectionManifestDifference(change, key, oldItem, newItem));
            }
        }
        return differences;
    }

    public static void SaveDiffCsv(string path, IEnumerable<SelectionManifestDifference> differences)
    {
        var lines = new List<string>
        {
            "差分,種別,識別子,前回元パス,今回元パス,前回容量,今回容量,今回出力先"
        };
        foreach (var difference in differences)
        {
            var cells = new[]
            {
                difference.Change, difference.Current?.Kind ?? difference.Previous?.Kind ?? string.Empty,
                difference.Key, difference.Previous?.SourcePath ?? string.Empty,
                difference.Current?.SourcePath ?? string.Empty,
                difference.Previous?.Size.ToString() ?? string.Empty,
                difference.Current?.Size.ToString() ?? string.Empty,
                difference.Current?.DestinationPath ?? string.Empty
            };
            lines.Add(string.Join(",", cells.Select(EscapeCsv)));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static string EscapeCsv(string value)
    {
        var safe = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
