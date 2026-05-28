using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using Win32 = Microsoft.Win32;

namespace QACInstallerPicker.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string AutoDetectHelixLabel = "(選択中のHelixを使用)";
    private const string AiModeDisabled = "Disabled";
    private const string AiModeLocalLlm = "LocalLlm";
    private const string DefaultLocalLlmBasePath = @"C:\LLM";
    private const string DefaultLocalLlmEndpoint = "http://127.0.0.1:11434";

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

        _aiDecisionMode = NormalizeAiMode(settings.AiDecisionMode);
        _localLlmBasePath = string.IsNullOrWhiteSpace(settings.LocalLlmBasePath)
            ? DefaultLocalLlmBasePath
            : settings.LocalLlmBasePath;
        _localLlmEndpoint = string.IsNullOrWhiteSpace(settings.LocalLlmEndpoint)
            ? DefaultLocalLlmEndpoint
            : settings.LocalLlmEndpoint;
        _excelPath = settings.ExcelPath;
        _uncRoot = settings.UncRoot;
        _outputBaseFolder = settings.OutputBaseFolder;
        _shipmentHistoryExcelPath = settings.ShipmentHistoryExcelPath;
        _maxConcurrentTransfers = settings.MaxConcurrentTransfers;

        AiDecisionModeOptions = new ObservableCollection<SelectableOptionViewModel>
        {
            new("AI無効（既存ルールのみ）", AiModeDisabled),
            new("ローカルLLM", AiModeLocalLlm)
        };

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
                IsSelected = selectedCustomTabs.Count == 0 || selectedCustomTabs.Contains(tabName)
            });
        }
    }

    [ObservableProperty]
    private string _aiDecisionMode = AiModeDisabled;

    [ObservableProperty]
    private string _localLlmBasePath = DefaultLocalLlmBasePath;

    [ObservableProperty]
    private string _localLlmEndpoint = DefaultLocalLlmEndpoint;

    [ObservableProperty]
    private string _excelPath;

    [ObservableProperty]
    private string _uncRoot;

    [ObservableProperty]
    private string _outputBaseFolder;

    [ObservableProperty]
    private string _shipmentHistoryExcelPath;

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

    public ObservableCollection<SelectableOptionViewModel> AiDecisionModeOptions { get; }
    public ObservableCollection<string> BulkExcelHelixVersionOptions { get; }
    public ObservableCollection<SelectableOptionViewModel> BulkExcelCustomTabOptions { get; }

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private void Save()
    {
        var shipmentHistoryExcelPath = ShipmentHistoryExcelPath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(shipmentHistoryExcelPath) &&
            !shipmentHistoryExcelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            WpfMessageBox.Show(
                "送付履歴Excelは .xlsx ファイルを指定してください。",
                "設定エラー",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(shipmentHistoryExcelPath) && !File.Exists(shipmentHistoryExcelPath))
        {
            WpfMessageBox.Show(
                $"送付履歴Excelが見つかりません: {shipmentHistoryExcelPath}",
                "設定エラー",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning);
            return;
        }

        _settings.AiDecisionMode = NormalizeAiMode(AiDecisionMode);
        _settings.LocalLlmBasePath = (LocalLlmBasePath ?? string.Empty).Trim();
        _settings.LocalLlmEndpoint = (LocalLlmEndpoint ?? string.Empty).Trim();
        _settings.ExcelPath = ExcelPath?.Trim() ?? string.Empty;
        _settings.UncRoot = UncRoot?.Trim() ?? string.Empty;
        _settings.OutputBaseFolder = OutputBaseFolder?.Trim() ?? string.Empty;
        _settings.ShipmentHistoryExcelPath = shipmentHistoryExcelPath;
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
        AiDecisionMode = AiModeDisabled;
        LocalLlmBasePath = DefaultLocalLlmBasePath;
        LocalLlmEndpoint = DefaultLocalLlmEndpoint;
        ExcelPath = string.Empty;
        UncRoot = string.Empty;
        OutputBaseFolder = string.Empty;
        ShipmentHistoryExcelPath = string.Empty;
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

    [RelayCommand]
    private void BrowseShipmentHistoryExcel()
    {
        var dialog = new Win32.OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            FileName = ShipmentHistoryExcelPath
        };

        var directory = Path.GetDirectoryName(ShipmentHistoryExcelPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() == true)
        {
            ShipmentHistoryExcelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseLocalLlmBasePath()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "ローカルLLMフォルダ",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(LocalLlmBasePath) ? LocalLlmBasePath : DefaultLocalLlmBasePath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            LocalLlmBasePath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void CreateRecommendedLlmFolders()
    {
        var basePath = string.IsNullOrWhiteSpace(LocalLlmBasePath)
            ? DefaultLocalLlmBasePath
            : LocalLlmBasePath.Trim();
        var subDirs = new[] { "models", "runtime", "cache", "logs", "config" };

        try
        {
            Directory.CreateDirectory(basePath);
            foreach (var dir in subDirs)
            {
                Directory.CreateDirectory(Path.Combine(basePath, dir));
            }

            WpfMessageBox.Show(
                $"LLMフォルダを作成しました: {basePath}",
                "設定",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"LLMフォルダの作成に失敗しました: {ex.Message}",
                "エラー",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task TestLocalLlmConnectionAsync()
    {
        var endpoint = (LocalLlmEndpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            WpfMessageBox.Show(
                "ローカルLLMエンドポイントを入力してください。",
                "設定",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Warning);
            return;
        }

        var probeUrls = new[]
        {
            $"{endpoint}/api/tags",
            $"{endpoint}/v1/models",
            $"{endpoint}/health",
            endpoint
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        foreach (var url in probeUrls)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if ((int)response.StatusCode < 500)
                {
                    var detail = $"{url} ({(int)response.StatusCode} {response.ReasonPhrase})";
                    WpfMessageBox.Show(
                        $"ローカルLLMに到達できました。\n{detail}",
                        "接続確認",
                        WpfMessageBoxButton.OK,
                        WpfMessageBoxImage.Information);
                    return;
                }
            }
            catch
            {
                // 次の候補URLを試す
            }
        }

        WpfMessageBox.Show(
            $"ローカルLLMに接続できませんでした。\nエンドポイント: {endpoint}",
            "接続確認",
            WpfMessageBoxButton.OK,
            WpfMessageBoxImage.Error);
    }

    private static string NormalizeAiMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return AiModeDisabled;
        }

        return mode.Equals(AiModeLocalLlm, StringComparison.OrdinalIgnoreCase)
            ? AiModeLocalLlm
            : AiModeDisabled;
    }
}

public partial class SelectableOptionViewModel : ObservableObject
{
    public SelectableOptionViewModel(string name, string? value = null)
    {
        Name = name;
        Value = string.IsNullOrWhiteSpace(value) ? name : value;
    }

    public string Name { get; }
    public string Value { get; }

    [ObservableProperty]
    private bool _isSelected;
}
