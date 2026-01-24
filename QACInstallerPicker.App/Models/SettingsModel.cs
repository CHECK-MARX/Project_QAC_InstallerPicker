namespace QACInstallerPicker.App.Models;

public class SettingsModel
{
    public string ExcelPath { get; set; } = string.Empty;
    public string UncRoot { get; set; } = string.Empty;
    public string OutputBaseFolder { get; set; } = string.Empty;
    public int MaxConcurrentTransfers { get; set; } = 2;
}
