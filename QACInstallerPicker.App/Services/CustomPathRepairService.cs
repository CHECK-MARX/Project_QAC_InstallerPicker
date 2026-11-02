using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QACInstallerPicker.App.Services;

public sealed record CustomPathRepairPreview(
    IReadOnlyDictionary<string, string> Replacements,
    IReadOnlyList<string> MissingTargets,
    IReadOnlyList<string> InvalidPaths);

public static class CustomPathRepairService
{
    public static CustomPathRepairPreview Preview(
        string oldRoot, string newRoot, IEnumerable<string> sourcePaths)
    {
        if (!Path.IsPathFullyQualified(oldRoot) || !Path.IsPathFullyQualified(newRoot))
        {
            throw new ArgumentException("旧ルートと新ルートには絶対パスを指定してください。");
        }
        var oldFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldRoot));
        var newFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newRoot));
        if (oldFull.Length == 0 || newFull.Length == 0 ||
            oldFull.Equals(newFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("旧ルートと新ルートには異なるフォルダを指定してください。");
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var invalid = new List<string>();
        foreach (var original in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string source;
            try
            {
                source = Path.GetFullPath(original);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                invalid.Add(original);
                continue;
            }

            var prefix = Path.EndsInDirectorySeparator(oldFull)
                ? oldFull : oldFull + Path.DirectorySeparatorChar;
            if (!source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(newFull, source[prefix.Length..]);
            if (File.Exists(destination))
            {
                replacements[original] = destination;
            }
            else
            {
                missing.Add(destination);
            }
        }
        return new CustomPathRepairPreview(replacements, missing, invalid);
    }
}
