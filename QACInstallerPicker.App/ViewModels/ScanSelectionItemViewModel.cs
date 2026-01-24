using System;
using CommunityToolkit.Mvvm.ComponentModel;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.ViewModels;

public partial class ScanSelectionItemViewModel : ObservableObject
{
    public ScanSelectionItemViewModel(LogicalItem item)
    {
        Asset = item.PreferredAsset;
        Code = item.Code;
        Name = ModuleCatalog.GetDescription(item.Code);
        Version = item.Version;
        Os = item.Os;
        AssetFileName = item.PreferredAsset.FileName;
        SourcePath = item.PreferredAsset.SourcePath;
        IsZip = item.PreferredAsset.IsZip;

        IsEnabled = !item.Code.Equals("Unclassified", StringComparison.OrdinalIgnoreCase);
        DisabledReason = IsEnabled ? string.Empty : "Unclassified";
    }

    public InstallerAsset Asset { get; }
    public string Code { get; }
    public string Name { get; }
    public string Version { get; }
    public OsType Os { get; }
    public string AssetFileName { get; }
    public string SourcePath { get; }
    public bool IsZip { get; }
    public bool IsEnabled { get; }
    public string DisabledReason { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
