using System;

namespace QACInstallerPicker.App.Models;

public sealed class ShipmentHistoryRecord
{
    public DateTime ShipmentDate { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string HelixVersion { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CompatibilityVersion { get; set; } = string.Empty;
    public string SelectedOs { get; set; } = string.Empty;
    public string InstallerName { get; set; } = string.Empty;
}
