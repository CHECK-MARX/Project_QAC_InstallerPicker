using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsPath;
    }

    public SettingsModel Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = new SettingsModel();
            Save(defaults);
            return defaults;
        }

        SettingsModel settings;
        try
        {
            settings = ReadSettings(_settingsPath);
        }
        catch (JsonException ex) when (File.Exists(_settingsPath + ".bak"))
        {
            settings = ReadSettings(_settingsPath + ".bak");
            var damagedPath = _settingsPath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(_settingsPath, damagedPath);
            File.Copy(_settingsPath + ".bak", _settingsPath);
            AppLogger.LogError($"SettingsRecoveredFromBackup:{damagedPath}", ex);
        }
        if (settings.MaxConcurrentTransfers <= 0)
        {
            settings.MaxConcurrentTransfers = 2;
        }

        settings.AiDecisionMode = NormalizeAiDecisionMode(settings.AiDecisionMode);
        settings.LocalLlmBasePath = NormalizeLocalLlmBasePath(settings.LocalLlmBasePath);
        settings.LocalLlmEndpoint = NormalizeLocalLlmEndpoint(settings.LocalLlmEndpoint);
        settings.ShipmentHistoryExcelPath ??= string.Empty;
        settings.SelectedCustomTabName ??= string.Empty;
        settings.CustomTabStates ??= new();
        settings.CustomZipPlans ??= new();
        settings.SelectionStateHistory ??= new();
        settings.SavedSelectionSets ??= new();
        settings.MemoLearnedSynonyms = NormalizeMemoLearnedSynonyms(settings.MemoLearnedSynonyms);
        settings.MemoLearnedCompanyAliases = NormalizeMemoLearnedCompanyAliases(settings.MemoLearnedCompanyAliases);
        settings.MemoLatestVersionHints = NormalizeMemoLatestVersionHints(settings.MemoLatestVersionHints);
        settings.MemoUnresolvedHistory = NormalizeMemoUnresolvedHistory(settings.MemoUnresolvedHistory);
        settings.BulkExcelTemplateOptions ??= new();
        settings.BulkExcelTemplateOptions.ExportHelixVersion ??= string.Empty;
        settings.BulkExcelTemplateOptions.ExportCustomTabNames ??= new();
        settings.BulkExcelTemplateOptions.IncludeScanSelection = false;

        return settings;
    }

    public void Save(SettingsModel settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporaryPath = _settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, json);
            _ = ReadSettings(temporaryPath);
            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, _settingsPath + ".bak");
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static SettingsModel ReadSettings(string path)
    {
        return JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(path))
            ?? throw new JsonException($"設定ファイルが空です: {path}");
    }

    private static Dictionary<string, List<string>> NormalizeMemoLearnedSynonyms(
        Dictionary<string, List<string>>? source)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            var key = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var values = (pair.Value ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (values.Count == 0)
            {
                continue;
            }

            result[key] = values;
        }

        return result;
    }

    private static List<string> NormalizeMemoUnresolvedHistory(List<string>? source)
    {
        if (source == null)
        {
            return new List<string>();
        }

        return source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> NormalizeMemoLearnedCompanyAliases(
        Dictionary<string, string>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            var alias = (pair.Key ?? string.Empty).Trim();
            var company = (pair.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(company))
            {
                continue;
            }

            result[alias] = company;
        }

        return result;
    }

    private static List<string> NormalizeMemoLatestVersionHints(List<string>? source)
    {
        if (source == null)
        {
            return new List<string>();
        }

        return source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeAiDecisionMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Disabled";
        }

        return mode.Equals("LocalLlm", StringComparison.OrdinalIgnoreCase)
            ? "LocalLlm"
            : "Disabled";
    }

    private static string NormalizeLocalLlmBasePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? @"C:\LLM" : trimmed;
    }

    private static string NormalizeLocalLlmEndpoint(string? endpoint)
    {
        var trimmed = (endpoint ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "http://127.0.0.1:11434" : trimmed;
    }
}
