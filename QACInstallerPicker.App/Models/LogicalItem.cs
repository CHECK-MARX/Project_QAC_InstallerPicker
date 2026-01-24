using System.Collections.Generic;
using System.Linq;

namespace QACInstallerPicker.App.Models;

public class LogicalItem
{
    public string Code { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public OsType Os { get; init; }
    public List<InstallerAsset> Assets { get; } = new();

    public InstallerAsset PreferredAsset => Assets
        .OrderByDescending(asset => asset.IsZip)
        .First();

    public string Key => $"{Code}|{Version}|{Os}";
}
