using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class MemoParserService
{
    private sealed record SynonymEntry(string Term, List<string> Codes, string Normalized);

    public Dictionary<string, List<string>> LoadSynonyms(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                  ?? new Dictionary<string, JsonElement>();

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in raw)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                result[key] = new List<string> { element.GetString() ?? string.Empty };
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            list.Add(value);
                        }
                    }
                }

                if (list.Count > 0)
                {
                    result[key] = list;
                }
            }
        }

        return result;
    }

    public MemoParseResult ParseMemo(
        string text,
        IReadOnlyCollection<string> knownCodes,
        IReadOnlyDictionary<string, List<string>> synonyms)
    {
        var result = new MemoParseResult();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var matchedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedText = NormalizeForMatch(text);
        var synonymEntries = BuildSynonymEntries(synonyms);

        foreach (var code in knownCodes)
        {
            var normalizedCode = NormalizeForMatch(code);
            if (!string.IsNullOrEmpty(normalizedCode) && normalizedText.Contains(normalizedCode, StringComparison.OrdinalIgnoreCase))
            {
                matchedCodes.Add(code);
            }
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 2)
            {
                continue;
            }

            var normalizedLine = NormalizeForMatch(trimmed);
            if (normalizedLine.Length == 0)
            {
                continue;
            }

            var matches = new List<SynonymEntry>();
            foreach (var entry in synonymEntries)
            {
                if (normalizedLine.Contains(entry.Normalized, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(entry);
                }
            }

            if (matches.Count == 0)
            {
                continue;
            }

            // Prefer longer matches to avoid treating "C" as a hit for "C++".
            var filteredMatches = FilterOverlappingSynonyms(matches);
            foreach (var entry in filteredMatches)
            {
                if (entry.Codes.Count == 1)
                {
                    matchedCodes.Add(entry.Codes[0]);
                }
                else
                {
                    result.AmbiguousMatches.Add(new AmbiguousMatch
                    {
                        Term = entry.Term,
                        Candidates = entry.Codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
            }
        }

        result.MatchedCodes.AddRange(matchedCodes);

        var normalizedCodes = knownCodes
            .Select(code => NormalizeForMatch(code))
            .Where(code => !string.IsNullOrEmpty(code))
            .ToList();
        var normalizedSynonymTerms = synonymEntries
            .Select(entry => entry.Normalized)
            .ToList();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 2)
            {
                continue;
            }

            var normalizedLine = NormalizeForMatch(trimmed);
            var hasKnown = normalizedCodes.Any(code => normalizedLine.Contains(code, StringComparison.OrdinalIgnoreCase));
            var hasSynonym = normalizedSynonymTerms.Any(term => normalizedLine.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (!hasKnown && !hasSynonym)
            {
                result.UnresolvedTerms.Add(trimmed);
            }
        }

        return result;
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || ch == '+')
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static List<SynonymEntry> BuildSynonymEntries(IReadOnlyDictionary<string, List<string>> synonyms)
    {
        return synonyms
            .Select(pair => new SynonymEntry(pair.Key, pair.Value, NormalizeForMatch(pair.Key)))
            .Where(entry => !string.IsNullOrEmpty(entry.Normalized))
            .GroupBy(entry => entry.Normalized, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var term = group.OrderByDescending(entry => entry.Term.Length).First().Term;
                var codes = group
                    .SelectMany(entry => entry.Codes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new SynonymEntry(term, codes, group.Key);
            })
            .OrderByDescending(entry => entry.Normalized.Length)
            .ToList();
    }

    private static List<SynonymEntry> FilterOverlappingSynonyms(List<SynonymEntry> matches)
    {
        var ordered = matches
            .OrderByDescending(entry => entry.Normalized.Length)
            .ToList();
        var filtered = new List<SynonymEntry>();
        foreach (var entry in ordered)
        {
            if (filtered.Any(existing =>
                    existing.Normalized.Contains(entry.Normalized, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            filtered.Add(entry);
        }

        return filtered;
    }
}
