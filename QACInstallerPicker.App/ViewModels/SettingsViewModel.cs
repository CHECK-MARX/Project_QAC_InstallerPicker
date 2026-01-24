using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;
using Forms = System.Windows.Forms;
using Win32 = Microsoft.Win32;

namespace QACInstallerPicker.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly SettingsModel _settings;

    public SettingsViewModel(SettingsModel settings, SettingsService service)
    {
        _settings = settings;
        _service = service;
        _excelPath = settings.ExcelPath;
        _uncRoot = settings.UncRoot;
        _outputBaseFolder = settings.OutputBaseFolder;
        _maxConcurrentTransfers = settings.MaxConcurrentTransfers;
    }

    [ObservableProperty]
    private string _excelPath;

    [ObservableProperty]
    private string _uncRoot;

    [ObservableProperty]
    private string _outputBaseFolder;

    [ObservableProperty]
    private int _maxConcurrentTransfers;

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private void Save()
    {
        _settings.ExcelPath = ExcelPath?.Trim() ?? string.Empty;
        _settings.UncRoot = UncRoot?.Trim() ?? string.Empty;
        _settings.OutputBaseFolder = OutputBaseFolder?.Trim() ?? string.Empty;
        _settings.MaxConcurrentTransfers = Math.Max(1, MaxConcurrentTransfers);
        _service.Save(_settings);
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void Reset()
    {
        ExcelPath = string.Empty;
        UncRoot = string.Empty;
        OutputBaseFolder = string.Empty;
        MaxConcurrentTransfers = 2;
    }

    [RelayCommand]
    private void BrowseExcel()
    {
        var dialog = new Win32.OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            FileName = ExcelPath
        };

        var directory = Path.GetDirectoryName(ExcelPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() == true)
        {
            ExcelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseUncRoot()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "UNCルート",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(UncRoot) ? UncRoot : string.Empty,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            UncRoot = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void BrowseOutputBase()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "出力ベースフォルダ",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputBaseFolder) ? OutputBaseFolder : string.Empty,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputBaseFolder = dialog.SelectedPath;
        }
    }
}
