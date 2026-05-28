using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public sealed class LocalLlmDecisionService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly TimeSpan LlmRequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ModelNameCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SuccessDecisionCacheTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan FailureDecisionCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, ModelCacheEntry> ModelNameCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DecisionCacheEntry> _decisionCache =
        new(StringComparer.Ordinal);

    public async Task<LocalLlmDecisionResult> AnalyzeMemoAsync(
        string endpoint,
        string memo,
        IReadOnlyCollection<string> knownCodes,
        CancellationToken cancellationToken = default)
    {
        var result = new LocalLlmDecisionResult();
        var normalizedEndpoint = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedEndpoint))
        {
            result.ErrorMessage = "ローカルLLMのエンドポイントが未設定です。";
            return result;
        }

        var normalizedMemo = (memo ?? string.Empty).Trim();
        if (normalizedMemo.Length == 0)
        {
            result.ErrorMessage = "メール/メモが空のため、LLM判定を実行できません。";
            return result;
        }

        var cacheKey = BuildDecisionCacheKey(normalizedEndpoint, normalizedMemo, knownCodes);
        if (TryGetCachedDecision(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LlmRequestTimeout);
            var requestToken = timeoutCts.Token;

            var modelName = await ResolveModelNameAsync(SharedHttpClient, normalizedEndpoint, requestToken);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                result.ErrorMessage = "利用可能なローカルLLMモデルが見つかりません。";
                CacheDecision(cacheKey, result);
                return CloneDecision(result);
            }

            result.ModelName = modelName;
            var requestBody = new
            {
                model = modelName,
                stream = false,
                format = "json",
                options = new { temperature = 0, num_predict = 320 },
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt()
                    },
                    new
                    {
                        role = "user",
                        content = BuildUserPrompt(normalizedMemo, knownCodes)
                    }
                }
            };

            var payload = JsonSerializer.Serialize(requestBody);
            using var response = await SharedHttpClient.PostAsync(
                $"{normalizedEndpoint}/api/chat",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                requestToken);
            var responseText = await response.Content.ReadAsStringAsync(requestToken);
            result.RawResponse = responseText;
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"ローカルLLM応答エラー: {(int)response.StatusCode} {response.ReasonPhrase}";
                CacheDecision(cacheKey, result);
                return CloneDecision(result);
            }

            var messageContent = ExtractAssistantContent(responseText);
            if (string.IsNullOrWhiteSpace(messageContent))
            {
                result.ErrorMessage = "ローカルLLMの応答本文が空です。";
                CacheDecision(cacheKey, result);
                return CloneDecision(result);
            }

            var decisionJson = ExtractJsonPayload(messageContent);
            if (string.IsNullOrWhiteSpace(decisionJson))
            {
                result.ErrorMessage = "ローカルLLM応答からJSONを抽出できませんでした。";
                CacheDecision(cacheKey, result);
                return CloneDecision(result);
            }

            MergeDecision(result, decisionJson);
            CacheDecision(cacheKey, result);
            return CloneDecision(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.ErrorMessage = "ローカルLLM判定がタイムアウトしました。";
            CacheDecision(cacheKey, result);
            return CloneDecision(result);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"ローカルLLM判定に失敗しました: {ex.Message}";
            CacheDecision(cacheKey, result);
            return CloneDecision(result);
        }
    }

    private static string BuildSystemPrompt()
    {
        return
            """
            You are an extractor for Japanese support emails.
            Output STRICT JSON only. Do not output markdown or explanations.
            Keep the JSON compact.

            Required top-level keys:
            {
              "company_name": "string",
              "default_os": "Windows|Linux|Both|Unspecified",
              "versioned_requests": [{"code":"string","version":"string","os":"Windows|Linux|Both|Unspecified"}],
              "matched_codes": ["string"]
            }

            Rules:
            - company_name must be requester company, not addressee company.
            - Prefer latest reply body and avoid quoted old threads.
            - versioned_requests include only explicit module+version requests.
            - matched_codes may include explicit module mentions without version.
            """;
    }

    private static string BuildUserPrompt(string memo, IReadOnlyCollection<string> knownCodes)
    {
        var codeList = string.Join(", ", knownCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase));

        return
            $"""
             known_module_codes:
             {codeList}

             mail_text:
             {memo}
             """;
    }

    private static async Task<string> ResolveModelNameAsync(
        HttpClient client,
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedModelName(endpoint, out var cachedModelName))
        {
            return cachedModelName;
        }

        using var response = await client.GetAsync($"{endpoint}/api/tags", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        if (names.Count == 0)
        {
            return string.Empty;
        }

        var preferred = names.FirstOrDefault(name =>
            name.StartsWith("qwen2.5", StringComparison.OrdinalIgnoreCase));
        preferred ??= names.FirstOrDefault(name =>
            name.StartsWith("qwen", StringComparison.OrdinalIgnoreCase));
        preferred ??= names[0];

        ModelNameCache[endpoint] = new ModelCacheEntry(
            preferred,
            DateTimeOffset.UtcNow.Add(ModelNameCacheTtl));
        return preferred;
    }

    private static bool TryGetCachedModelName(string endpoint, out string modelName)
    {
        modelName = string.Empty;
        if (!ModelNameCache.TryGetValue(endpoint, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            ModelNameCache.TryRemove(endpoint, out _);
            return false;
        }

        modelName = entry.ModelName;
        return !string.IsNullOrWhiteSpace(modelName);
    }

    private static string BuildDecisionCacheKey(
        string endpoint,
        string memo,
        IReadOnlyCollection<string> knownCodes)
    {
        var normalizedCodes = string.Join(
            ",",
            knownCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.Ordinal));
        var plain = $"{endpoint}\n{memo}\n{normalizedCodes}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(hashBytes);
    }

    private bool TryGetCachedDecision(string cacheKey, out LocalLlmDecisionResult result)
    {
        result = new LocalLlmDecisionResult();
        if (!_decisionCache.TryGetValue(cacheKey, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _decisionCache.TryRemove(cacheKey, out _);
            return false;
        }

        result = CloneDecision(entry.Result, isCached: true);
        return true;
    }

    private void CacheDecision(string cacheKey, LocalLlmDecisionResult result)
    {
        var ttl = result.IsSuccess ? SuccessDecisionCacheTtl : FailureDecisionCacheTtl;
        _decisionCache[cacheKey] = new DecisionCacheEntry(
            CloneDecision(result),
            DateTimeOffset.UtcNow.Add(ttl));
    }

    private static LocalLlmDecisionResult CloneDecision(LocalLlmDecisionResult source, bool isCached = false)
    {
        return new LocalLlmDecisionResult
        {
            ModelName = source.ModelName,
            CompanyName = source.CompanyName,
            DefaultOs = source.DefaultOs,
            VersionedRequests = source.VersionedRequests
                .Select(item => new LocalLlmVersionedRequest
                {
                    Code = item.Code,
                    Version = item.Version,
                    Os = item.Os
                })
                .ToList(),
            MatchedCodes = source.MatchedCodes.ToList(),
            RawResponse = source.RawResponse,
            ErrorMessage = source.ErrorMessage,
            IsCached = isCached
        };
    }

    private static string ExtractAssistantContent(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(responseText);
        if (doc.RootElement.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var contentElement))
        {
            return contentElement.GetString() ?? string.Empty;
        }

        if (doc.RootElement.TryGetProperty("response", out var responseElement))
        {
            return responseElement.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string ExtractJsonPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                .ToArray();
            trimmed = string.Join(Environment.NewLine, lines).Trim();
        }

        if (IsValidJsonObject(trimmed))
        {
            return trimmed;
        }

        var bestCandidate = string.Empty;
        var depth = 0;
        var objectStart = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch == '{')
            {
                if (depth == 0)
                {
                    objectStart = i;
                }

                depth++;
                continue;
            }

            if (ch != '}' || depth == 0)
            {
                continue;
            }

            depth--;
            if (depth != 0 || objectStart < 0)
            {
                continue;
            }

            var candidate = trimmed[objectStart..(i + 1)];
            if (IsValidJsonObject(candidate))
            {
                bestCandidate = candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(bestCandidate))
        {
            return bestCandidate;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var fallback = trimmed[start..(end + 1)];
            if (IsValidJsonObject(fallback))
            {
                return fallback;
            }
        }

        return string.Empty;
    }

    private static bool IsValidJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static void MergeDecision(LocalLlmDecisionResult result, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        result.CompanyName = GetString(root, "company_name", "company", "requester_company");
        result.DefaultOs = GetString(root, "default_os", "os", "requested_os");

        foreach (var request in EnumerateRequestArray(root, "versioned_requests", "requests"))
        {
            var code = GetString(request, "code", "module", "module_code");
            var version = GetString(request, "version", "module_version", "requested_version");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            result.VersionedRequests.Add(new LocalLlmVersionedRequest
            {
                Code = code.Trim(),
                Version = version.Trim(),
                Os = GetString(request, "os", "requested_os")
            });
        }

        foreach (var code in EnumerateStringArray(root, "matched_codes", "codes", "modules"))
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                result.MatchedCodes.Add(code.Trim());
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateRequestArray(JsonElement root, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }
    }

    private static IEnumerable<string> EnumerateStringArray(JsonElement root, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = item.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }

            yield break;
        }
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return string.Empty;
    }

    private sealed record ModelCacheEntry(string ModelName, DateTimeOffset ExpiresAt);

    private sealed record DecisionCacheEntry(LocalLlmDecisionResult Result, DateTimeOffset ExpiresAt);
}
