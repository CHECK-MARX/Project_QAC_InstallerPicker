using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QACInstallerPicker.App.ViewModels;

public partial class AmbiguousMatchViewModel : ObservableObject
{
    public AmbiguousMatchViewModel(string term, List<string> candidates)
    {
        Term = term;
        Candidates = candidates;
    }

    public string Term { get; }
    public List<string> Candidates { get; }

    [ObservableProperty]
    private string? _selectedCode;

    public event EventHandler<string>? SelectedCodeChanged;

    partial void OnSelectedCodeChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            SelectedCodeChanged?.Invoke(this, value);
        }
    }
}
