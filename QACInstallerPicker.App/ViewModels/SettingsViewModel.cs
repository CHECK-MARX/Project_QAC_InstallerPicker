using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;
using Forms = System.Windows.Forms;
using Win32 = Microsoft.Win32;

namespace QACInstallerPicker.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string AutoDetectHelixLabel = "（選択中のHelixを使用）";
    private readonly SettingsService _service;
    private readonly SettingsModel _settings;

    public SettingsViewModel(
        SettingsModel settings,
        SettingsService service,
        IEnumerable<string>? availableHelixVersions = null,
        IEnumerable<string>? availableCustomTabNames = null)
    {
        _settings = settings;
        _service = service;
        _excelPath = settings.ExcelPath;
        _uncRoot = settings.UncRoot;
        _outputBaseFolder = settings.OutputBaseFolder;
        _maxConcurrentTransfers = settings.MaxConcurrentTransfers;

        var bulkExcel = settings.BulkExcelTemplateOptions ?? new BulkExcelTemplateOptions();
        _includeBasicInfoInBulkExcel = bulkExcel.IncludeBasicInfo;
        _includeModuleSelectionInBulkExcel = bulkExcel.IncludeModuleSelection;
        _includeCustomTabsInBulkExcel = bulkExcel.IncludeCustomTabs;
        _includeCustomZipPlansInBulkExcel = bulkExcel.IncludeCustomZipPlans;

        BulkExcelHelixVersionOptions = new ObservableCollection<string>();
        BulkExcelCustomTabOptions = new ObservableCollection<SelectableOptionViewModel>();

        BulkExcelHelixVersionOptions.Add(AutoDetectHelixLabel);
        foreach (var version in (availableHelixVersions ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            BulkExcelHelixVersionOptions.Add(version);
        }

        if (!string.IsNullOrWhiteSpace(bulkExcel.ExportHelixVersion) &&
            !BulkExcelHelixVersionOptions.Any(value =>
                value.Equals(bulkExcel.ExportHelixVersion, StringComparison.OrdinalIgnoreCase)))
        {
            BulkExcelHelixVersionOptions.Add(bulkExcel.ExportHelixVersion);
        }

        _selectedBulkExcelHelixVersion = string.IsNullOrWhiteSpace(bulkExcel.ExportHelixVersion)
            ? AutoDetectHelixLabel
            : bulkExcel.ExportHelixVersion;

        var selectedCustomTabs = (bulkExcel.ExportCustomTabNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var customTabNames = (availableCustomTabNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var selected in selectedCustomTabs)
        {
            if (!customTabNames.Any(item => item.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            {
                customTabNames.Add(selected);
            }
        }

        foreach (var tabName in customTabNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            BulkExcelCustomTabOptions.Add(new SelectableOptionViewModel(tabName)
            {
                // Backward compatibility: if no explicit saved list exists, default to all selected.
                IsSelected = selectedCustomTabs.Count == 0 || selectedCustomTabs.Contains(tabName)
            });
        }
    }

    [ObservableProperty]
    private string _excelPath;

    [ObservableProperty]
    private string _uncRoot;

    [ObservableProperty]
    private string _outputBaseFolder;

    [ObservableProperty]
    private int _maxConcurrentTransfers;

    [ObservableProperty]
    private bool _includeBasicInfoInBulkExcel = true;

    [ObservableProperty]
    private bool _includeModuleSelectionInBulkExcel = true;

    [ObservableProperty]
    private bool _includeCustomTabsInBulkExcel = true;

    [ObservableProperty]
    private bool _includeCustomZipPlansInBulkExcel = true;

    [ObservableProperty]
    private string _selectedBulkExcelHelixVersion = AutoDetectHelixLabel;

    public ObservableCollection<string> BulkExcelHelixVersionOptions { get; }
    public ObservableCollection<SelectableOptionViewModel> BulkExcelCustomTabOptions { get; }

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private void Save()
    {
        _settings.ExcelPath = ExcelPath?.Trim() ?? string.Empty;
        _settings.UncRoot = UncRoot?.Trim() ?? string.Empty;
        _settings.OutputBaseFolder = OutputBaseFolder?.Trim() ?? string.Empty;
        _settings.MaxConcurrentTransfers = Math.Max(1, MaxConcurrentTransfers);
        _settings.BulkExcelTemplateOptions = new BulkExcelTemplateOptions
        {
            IncludeBasicInfo = IncludeBasicInfoInBulkExcel,
            IncludeModuleSelection = IncludeModuleSelectionInBulkExcel,
            IncludeScanSelection = false,
            IncludeCustomTabs = IncludeCustomTabsInBulkExcel,
            IncludeCustomZipPlans = IncludeCustomZipPlansInBulkExcel,
            ExportHelixVersion = SelectedBulkExcelHelixVersion == AutoDetectHelixLabel
                ? string.Empty
                : (SelectedBulkExcelHelixVersion?.Trim() ?? string.Empty),
            ExportCustomTabNames = BulkExcelCustomTabOptions
                .Where(item => item.IsSelected)
                .Select(item => item.Name)
                .ToList()
        };
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
        IncludeBasicInfoInBulkExcel = true;
        IncludeModuleSelectionInBulkExcel = true;
        IncludeCustomTabsInBulkExcel = true;
        IncludeCustomZipPlansInBulkExcel = true;
        SelectedBulkExcelHelixVersion = AutoDetectHelixLabel;
        foreach (var item in BulkExcelCustomTabOptions)
        {
            item.IsSelected = true;
        }
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

public partial class SelectableOptionViewModel : ObservableObject
{
    public SelectableOptionViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}
