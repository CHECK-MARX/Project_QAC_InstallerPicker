using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace QACInstallerPicker.App.ViewModels;

public class HelixVersionViewModel
{
    private string _filterText = string.Empty;

    public HelixVersionViewModel(string version, IEnumerable<ModuleRowViewModel> modules)
    {
        Version = version;
        Modules = new ObservableCollection<ModuleRowViewModel>(modules);
        ModulesView = CollectionViewSource.GetDefaultView(Modules);
        ModulesView.Filter = FilterModule;

        foreach (var module in Modules)
        {
            module.SelectionChanged += (_, e) => SelectionChanged?.Invoke(this, e);
            module.OsSelectionChanged += (_, e) => OsSelectionChanged?.Invoke(this, e);
            module.InstallerVersionChanged += (_, e) => InstallerVersionChanged?.Invoke(this, e);
        }
    }

    public string Version { get; }
    public ObservableCollection<ModuleRowViewModel> Modules { get; }
    public ICollectionView ModulesView { get; }

    public event EventHandler<ModuleSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<ModuleOsSelectionChangedEventArgs>? OsSelectionChanged;
    public event EventHandler<ModuleInstallerVersionChangedEventArgs>? InstallerVersionChanged;

    public void AddModule(ModuleRowViewModel module)
    {
        Modules.Add(module);
        module.SelectionChanged += (_, e) => SelectionChanged?.Invoke(this, e);
        module.OsSelectionChanged += (_, e) => OsSelectionChanged?.Invoke(this, e);
        module.InstallerVersionChanged += (_, e) => InstallerVersionChanged?.Invoke(this, e);
    }

    public void ApplyFilter(string? filterText)
    {
        _filterText = filterText?.Trim() ?? string.Empty;
        ModulesView.Refresh();
    }

    private bool FilterModule(object obj)
    {
        if (obj is not ModuleRowViewModel module)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_filterText))
        {
            return true;
        }

        var text = _filterText.ToLowerInvariant();
        if (module.Code.ToLowerInvariant().Contains(text))
        {
            return true;
        }

        if (module.Name.ToLowerInvariant().Contains(text))
        {
            return true;
        }

        return module.Aliases.Any(alias => alias.ToLowerInvariant().Contains(text));
    }
}
