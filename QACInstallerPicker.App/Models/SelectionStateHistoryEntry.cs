using System;
using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class SelectionStateHistoryEntry
{
    public DateTime SavedAtUtc { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string MemoText { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string SelectedVersion { get; set; } = string.Empty;
    public List<SelectionModuleState> SelectedModules { get; set; } = new();
    public List<SelectionScanState> SelectedScanItems { get; set; } = new();
    public List<SelectionCustomTabState> SelectedCustomTabs { get; set; } = new();
    public List<CustomZipPlan> CustomZipPlans { get; set; } = new();
}

public class SelectionModuleState
{
    public string HelixVersion { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string OsSelection { get; set; } = string.Empty;
    public string SelectedInstallerVersion { get; set; } = string.Empty;
}

public class SelectionScanState
{
    public string SourcePath { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
}

public class SelectionCustomTabState
{
    public string TabName { get; set; } = string.Empty;
    public List<string> SelectedSourcePaths { get; set; } = new();
}
