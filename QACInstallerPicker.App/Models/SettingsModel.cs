using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class SettingsModel
{
    public string AiDecisionMode { get; set; } = "Disabled";
    public string LocalLlmBasePath { get; set; } = @"C:\LLM";
    public string LocalLlmEndpoint { get; set; } = "http://127.0.0.1:11434";
    public string ExcelPath { get; set; } = string.Empty;
    public string UncRoot { get; set; } = string.Empty;
    public string OutputBaseFolder { get; set; } = string.Empty;
    public string ShipmentHistoryExcelPath { get; set; } = string.Empty;
    public int MaxConcurrentTransfers { get; set; } = 2;
    public string SelectedCustomTabName { get; set; } = string.Empty;
    public List<CustomTabState> CustomTabStates { get; set; } = new();
    public List<CustomZipPlan> CustomZipPlans { get; set; } = new();
    public List<SelectionStateHistoryEntry> SelectionStateHistory { get; set; } = new();
    public Dictionary<string, List<string>> MemoLearnedSynonyms { get; set; } = new();
    public Dictionary<string, string> MemoLearnedCompanyAliases { get; set; } = new();
    public List<string> MemoLatestVersionHints { get; set; } = new();
    public List<string> MemoUnresolvedHistory { get; set; } = new();
    public BulkExcelTemplateOptions BulkExcelTemplateOptions { get; set; } = new();
}
