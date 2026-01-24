using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QACInstallerPicker.App.Helpers;

public static class VersionUtil
{
    private static readonly Regex NumberRegex = new(@"\d+", RegexOptions.Compiled);

    public static int CompareVersionLike(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var partsA = ExtractNumbers(a);
        var partsB = ExtractNumbers(b);
        var length = Math.Max(partsA.Count, partsB.Count);
        for (var i = 0; i < length; i++)
        {
            var valueA = i < partsA.Count ? partsA[i] : 0;
            var valueB = i < partsB.Count ? partsB[i] : 0;
            if (valueA != valueB)
            {
                return valueA.CompareTo(valueB);
            }
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAtLeast(string? value, string? min)
    {
        return CompareVersionLike(value, min) >= 0;
    }

    private static List<int> ExtractNumbers(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new List<int>();
        }

        return NumberRegex.Matches(input)
            .Select(m => int.TryParse(m.Value, out var number) ? number : 0)
            .ToList();
    }
}
