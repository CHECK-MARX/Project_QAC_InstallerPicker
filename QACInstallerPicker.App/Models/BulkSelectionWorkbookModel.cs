using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class BulkSelectionWorkbookModel
{
    public string TemplateVersion { get; set; } = "1.0";
    public string CompanyName { get; set; } = string.Empty;
    public string SelectedHelixVersion { get; set; } = string.Empty;
    public List<string> SelectedHelixVersions { get; set; } = new();
    public string SearchText { get; set; } = string.Empty;
    public string MemoText { get; set; } = string.Empty;
    public string OutputBaseFolder { get; set; } = string.Empty;
    public int MaxConcurrentTransfers { get; set; } = 2;
    public string SelectedCustomTabName { get; set; } = string.Empty;
    public List<string> IncludedCustomTabNames { get; set; } = new();

    public bool HasBasicInfoSection { get; set; }
    public bool HasModuleSelectionSection { get; set; }
    public bool HasScanSelectionSection { get; set; }
    public bool HasCustomTabsSection { get; set; }
    public bool HasCustomZipPlansSection { get; set; }

    public List<BulkModuleSelectionRow> ModuleSelections { get; set; } = new();
    public List<BulkScanSelectionRow> ScanSelections { get; set; } = new();
    public List<CustomTabState> CustomTabStates { get; set; } = new();
    public List<CustomZipPlan> CustomZipPlans { get; set; } = new();
}

public class BulkModuleSelectionRow
{
    public bool IsSelected { get; set; }
    public string HelixVersion { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CompatibilityVersion { get; set; } = string.Empty;
    public string SupportedOsDisplay { get; set; } = string.Empty;
    public string OsSelection { get; set; } = string.Empty;
    public string SupportStatus { get; set; } = string.Empty;
    public string SelectedInstallerVersion { get; set; } = string.Empty;
    public List<string> InstallerVersionOptions { get; set; } = new();
}

public class BulkScanSelectionRow
{
    public bool IsSelected { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
}
