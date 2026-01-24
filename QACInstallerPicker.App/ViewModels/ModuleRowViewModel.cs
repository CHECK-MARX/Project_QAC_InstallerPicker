using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QACInstallerPicker.App.ViewModels;

public sealed class ModuleSelectionChangedEventArgs : EventArgs
{
    public ModuleSelectionChangedEventArgs(ModuleRowViewModel module, bool isSelected)
    {
        Module = module;
        IsSelected = isSelected;
    }

    public ModuleRowViewModel Module { get; }
    public bool IsSelected { get; }
}

public sealed class ModuleOsSelectionChangedEventArgs : EventArgs
{
    public ModuleOsSelectionChangedEventArgs(ModuleRowViewModel module)
    {
        Module = module;
    }

    public ModuleRowViewModel Module { get; }
}

public sealed class ModuleInstallerVersionChangedEventArgs : EventArgs
{
    public ModuleInstallerVersionChangedEventArgs(ModuleRowViewModel module)
    {
        Module = module;
    }

    public ModuleRowViewModel Module { get; }
}

public partial class ModuleRowViewModel : ObservableObject
{
    private bool _suppressSelectionChange;
    private bool _suppressOsSelectionChange;
    private bool _suppressInstallerVersionChange;
    private bool _osSelectionExplicit;
    private bool _installerVersionExplicit;
    private readonly bool _baseIsEnabled;
    private readonly string _baseDisabledReason;
    public ModuleRowViewModel(
        string code,
        string name,
        string? moduleVersion,
        bool isSupported,
        bool isEnabled,
        string? disabledReason,
        List<string>? aliases = null,
        string? selectionGroupKey = null,
        bool isSelectionLeader = true)
    {
        Code = code;
        Name = name;
        ModuleVersion = moduleVersion ?? string.Empty;
        IsSupported = isSupported;
        _baseIsEnabled = isEnabled;
        _baseDisabledReason = disabledReason ?? string.Empty;
        _isEnabled = isEnabled;
        _disabledReason = _baseDisabledReason;
        Aliases = aliases ?? new List<string>();
        SupportText = isSupported && isEnabled ? "対応" : "非対応";
        SelectionGroupKey = selectionGroupKey ?? string.Empty;
        IsSelectionLeader = isSelectionLeader;
    }

    public const string OsSelectionWindows = "Windows";
    public const string OsSelectionLinux = "Linux";
    public const string OsSelectionBoth = "両方";
    private static readonly IReadOnlyList<string> OsSelectionOptionsList =
        new[] { OsSelectionWindows, OsSelectionLinux, OsSelectionBoth };

    public string Code { get; }
    public string Name { get; }
    public string ModuleVersion { get; }
    public bool IsSupported { get; }
    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _disabledReason = string.Empty;
    public string SupportText { get; }
    public List<string> Aliases { get; }
    public string SelectionGroupKey { get; }
    public bool IsSelectionLeader { get; }
    public bool IsSelectionVisible => string.IsNullOrEmpty(SelectionGroupKey) || IsSelectionLeader;
    public IReadOnlyList<string> OsSelectionOptions => OsSelectionOptionsList;

    [ObservableProperty]
    private string _osDisplay = "-";

    [ObservableProperty]
    private IReadOnlyList<string> _installerVersionOptions = Array.Empty<string>();

    [ObservableProperty]
    private string _selectedInstallerVersion = string.Empty;

    [ObservableProperty]
    private string _osSelection = OsSelectionBoth;

    [ObservableProperty]
    private bool _isSelected;

    public event EventHandler<ModuleSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<ModuleOsSelectionChangedEventArgs>? OsSelectionChanged;
    public event EventHandler<ModuleInstallerVersionChangedEventArgs>? InstallerVersionChanged;

    public bool HasInstallerVersionOptions => InstallerVersionOptions.Count > 0;

    partial void OnIsSelectedChanged(bool value)
    {
        if (_suppressSelectionChange)
        {
            return;
        }

        SelectionChanged?.Invoke(this, new ModuleSelectionChangedEventArgs(this, value));
    }

    partial void OnOsSelectionChanged(string value)
    {
        if (_suppressOsSelectionChange)
        {
            return;
        }

        _osSelectionExplicit = true;
        OsSelectionChanged?.Invoke(this, new ModuleOsSelectionChangedEventArgs(this));
    }

    partial void OnInstallerVersionOptionsChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(HasInstallerVersionOptions));
    }

    partial void OnSelectedInstallerVersionChanged(string value)
    {
        if (_suppressInstallerVersionChange)
        {
            return;
        }

        _installerVersionExplicit = true;
        InstallerVersionChanged?.Invoke(this, new ModuleInstallerVersionChangedEventArgs(this));
    }

    public void SetSelectedSilently(bool value)
    {
        _suppressSelectionChange = true;
        IsSelected = value;
        _suppressSelectionChange = false;
    }

    public void SetOsSelectionDefault(string value)
    {
        if (_osSelectionExplicit)
        {
            return;
        }

        SetOsSelectionSilently(value);
    }

    public void SetOsSelectionSilently(string value)
    {
        _suppressOsSelectionChange = true;
        OsSelection = value;
        _suppressOsSelectionChange = false;
    }

    public void SetOsSelectionFromGroup(string value)
    {
        _osSelectionExplicit = true;
        SetOsSelectionSilently(value);
    }

    public void SetInstallerVersionOptions(IReadOnlyList<string> options)
    {
        InstallerVersionOptions = options;
        if (options.Count == 0)
        {
            _installerVersionExplicit = false;
            SetSelectedInstallerVersionSilently(string.Empty);
            return;
        }

        if (_installerVersionExplicit &&
            options.Contains(SelectedInstallerVersion, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _installerVersionExplicit = false;
        SetSelectedInstallerVersionSilently(options[0]);
    }

    public void SetSelectedInstallerVersionSilently(string value)
    {
        _suppressInstallerVersionChange = true;
        SelectedInstallerVersion = value;
        _suppressInstallerVersionChange = false;
    }

    public void ApplyAvailability(bool? available, string? reason)
    {
        if (!_baseIsEnabled)
        {
            IsEnabled = false;
            DisabledReason = _baseDisabledReason;
            return;
        }

        if (available == null)
        {
            IsEnabled = true;
            DisabledReason = _baseDisabledReason;
            return;
        }

        if (available.Value)
        {
            IsEnabled = true;
            DisabledReason = _baseDisabledReason;
        }
        else
        {
            IsEnabled = false;
            DisabledReason = string.IsNullOrWhiteSpace(reason) ? "共有スキャン未検出" : reason;
            SetSelectedSilently(false);
        }
    }
}
