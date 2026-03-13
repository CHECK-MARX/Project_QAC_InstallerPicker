using System.Collections.Generic;

namespace QACInstallerPicker.App.Models;

public class BulkExcelTemplateOptions
{
    public bool IncludeBasicInfo { get; set; } = true;
    public bool IncludeModuleSelection { get; set; } = true;
    public bool IncludeScanSelection { get; set; } = false;
    public bool IncludeCustomTabs { get; set; } = true;
    public bool IncludeCustomZipPlans { get; set; } = true;
    public string ExportHelixVersion { get; set; } = string.Empty;
    public List<string> ExportCustomTabNames { get; set; } = new();
}
