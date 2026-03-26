using CommunityToolkit.Mvvm.ComponentModel;

namespace QACInstallerPicker.App.ViewModels;

public partial class FilterCheckItemViewModel : ObservableObject
{
    public FilterCheckItemViewModel(string value, bool isChecked = true)
    {
        Value = value;
        _isChecked = isChecked;
    }

    public string Value { get; }

    [ObservableProperty]
    private bool _isChecked;
}
