using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public sealed class LocalLlmDecisionService
{
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

        if (string.IsNullOrWhiteSpace(memo))
        {
            result.ErrorMessage = "メール/メモが空のため、LLM判定を実行できません。";
            return result;
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            var modelName = await ResolveModelNameAsync(client, normalizedEndpoint, cancellationToken);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                result.ErrorMessage = "利用可能なローカルLLMモデルが見つかりません。";
                return result;
            }

            result.ModelName = modelName;
            var requestBody = new
            {
                model = modelName,
                stream = false,
                format = "json",
                options = new { temperature = 0, num_predict = 220 },
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
                        content = BuildUserPrompt(memo, knownCodes)
                    }
                }
            };

            var payload = JsonSerializer.Serialize(requestBody);
            using var response = await client.PostAsync(
                $"{normalizedEndpoint}/api/chat",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            result.RawResponse = responseText;
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"ローカルLLM応答エラー: {(int)response.StatusCode} {response.ReasonPhrase}";
                return result;
            }

            var messageContent = ExtractAssistantContent(responseText);
            if (string.IsNullOrWhiteSpace(messageContent))
            {
                result.ErrorMessage = "ローカルLLMの応答本文が空です。";
                return result;
            }

            var decisionJson = ExtractJsonPayload(messageContent);
            if (string.IsNullOrWhiteSpace(decisionJson))
            {
                result.ErrorMessage = "ローカルLLM応答からJSONを抽出できませんでした。";
                return result;
            }

            MergeDecision(result, decisionJson);
            return result;
        }
        catch (TaskCanceledException)
        {
            result.ErrorMessage = "ローカルLLM判定がタイムアウトしました。";
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"ローカルLLM判定に失敗しました: {ex.Message}";
            return result;
        }
    }

    private static string BuildSystemPrompt()
    {
        return
            """
            You are an extractor for Japanese support emails.
            Output STRICT JSON only, no markdown.
            Required keys:
            {
              "company_name": "string",
              "default_os": "Windows|Linux|Both|Unspecified",
              "versioned_requests": [{"code":"string","version":"string","os":"Windows|Linux|Both|Unspecified"}],
              "matched_codes": ["string"]
            }
            Rules:
            - company_name must be requester company, not addressee company.
            - Prefer latest reply body, ignore quoted old messages when possible.
            - versioned_requests include only explicit module+version requests.
            - Use codes from known module list if possible.
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
        return preferred ?? names[0];
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

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return string.Empty;
        }

        return trimmed[start..(end + 1)];
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
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }

            yield break;
        }
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return string.Empty;
    }
}
