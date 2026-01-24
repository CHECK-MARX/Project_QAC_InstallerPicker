using CommunityToolkit.Mvvm.ComponentModel;

namespace QACInstallerPicker.App.ViewModels;

public partial class BasketItemViewModel : ObservableObject
{
    public BasketItemViewModel(
        string helixVersion,
        string code,
        string name,
        string moduleVersion,
        string installerVersion,
        string os,
        string assetFileName,
        string sourcePath,
        bool isMissing,
        string reason,
        bool isManualPick)
    {
        HelixVersion = helixVersion;
        Code = code;
        Name = name;
        ModuleVersion = moduleVersion;
        InstallerVersion = installerVersion;
        Os = os;
        AssetFileName = assetFileName;
        SourcePath = sourcePath;
        IsMissing = isMissing;
        Reason = reason;
        IsManualPick = isManualPick;
    }

    public string HelixVersion { get; }
    public string Code { get; }
    public string Name { get; }
    public string ModuleVersion { get; }
    public string InstallerVersion { get; }
    public string Os { get; }
    public string AssetFileName { get; }
    public string SourcePath { get; }
    public bool IsMissing { get; }
    public string Reason { get; }
    public bool IsManualPick { get; }
}
