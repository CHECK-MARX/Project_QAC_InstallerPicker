using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.ViewModels;
using WpfMessageBox = System.Windows.MessageBox;

namespace QACInstallerPicker.App.Views;

public partial class CustomTabPreviewWindow : Window, INotifyPropertyChanged
{
    private readonly HashSet<CustomTabViewModel> _attachedTabs = new();
    private readonly List<CustomTabPreviewTabViewModel> _attachedPreviewTabs = new();
    private string _previewSummary = string.Empty;
    private CustomTabPreviewTabViewModel? _selectedPreviewTab;
    private string _zipFileNameMode = ZipFileNameModeTabName;
    private string _customZipBaseName = string.Empty;

    public CustomTabPreviewWindow(MainViewModel mainVm)
    {
        MainVm = mainVm;
        InitializeComponent();
        DataContext = this;

        MainVm.CustomTabs.CollectionChanged += OnCustomTabsCollectionChanged;
        AttachHandlers(MainVm.CustomTabs);
        RefreshPreview();
        Closed += OnClosed;
    }

    public MainViewModel MainVm { get; }

    public ObservableCollection<CustomTabPreviewTabViewModel> PreviewTabs { get; } = new();
    public ObservableCollection<string> ZipFileNameModeOptions { get; } = new()
    {
        ZipFileNameModeTabName,
        ZipFileNameModeCustom
    };

    public ObservableCollection<FolderZipOptionViewModel> CurrentFolderOptions
    {
        get => SelectedPreviewTab?.FolderOptions ?? EmptyFolderOptions;
    }

    public string SelectedZipNamePreview
    {
        get
        {
            if (SelectedPreviewTab == null)
            {
                return "-";
            }

            var archiveBaseName = ResolveArchiveBaseName(SelectedPreviewTab.TabName, PreviewTabs.Count);
            return string.IsNullOrWhiteSpace(archiveBaseName) ? "(未設定)" : $"{archiveBaseName}.zip";
        }
    }

    public string ZipFileNameMode
    {
        get => _zipFileNameMode;
        set
        {
            if (string.Equals(_zipFileNameMode, value, StringComparison.Ordinal))
            {
                return;
            }

            _zipFileNameMode = value;
            if (IsCustomZipNameEnabled && string.IsNullOrWhiteSpace(CustomZipBaseName))
            {
                CustomZipBaseName = SelectedPreviewTab?.TabName ?? "custom";
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomZipNameEnabled));
            OnPropertyChanged(nameof(SelectedZipNamePreview));
            SyncZipPlansToMainViewModel();
        }
    }

    public bool IsCustomZipNameEnabled => string.Equals(ZipFileNameMode, ZipFileNameModeCustom, StringComparison.Ordinal);

    public string CustomZipBaseName
    {
        get => _customZipBaseName;
        set
        {
            if (string.Equals(_customZipBaseName, value, StringComparison.Ordinal))
            {
                return;
            }

            _customZipBaseName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedZipNamePreview));
            SyncZipPlansToMainViewModel();
        }
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        private set
        {
            if (string.Equals(_previewSummary, value, StringComparison.Ordinal))
            {
                return;
            }

            _previewSummary = value;
            OnPropertyChanged();
        }
    }

    public CustomTabPreviewTabViewModel? SelectedPreviewTab
    {
        get => _selectedPreviewTab;
        set
        {
            if (ReferenceEquals(_selectedPreviewTab, value))
            {
                return;
            }

            _selectedPreviewTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentFolderOptions));
            OnPropertyChanged(nameof(SelectedZipNamePreview));
            UpdatePreviewSummary();
            SyncZipPlansToMainViewModel();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static ObservableCollection<FolderZipOptionViewModel> EmptyFolderOptions { get; } = new();
    private const string ZipFileNameModeTabName = "\u30BF\u30D6\u540D";
    private const string ZipFileNameModeCustom = "\u4EFB\u610F";

    public void RefreshPreview()
    {
        var selectedByTab = SnapshotSelectedRows();
        var folderOptionByTab = SnapshotFolderOptions();

        var selectedName = MainVm.SelectedCustomTab?.Name;
        var previousName = SelectedPreviewTab?.TabName;

        var previewTabs = new List<CustomTabPreviewTabViewModel>();
        foreach (var customTab in MainVm.CustomTabs)
        {
            var hasSelectedState = selectedByTab.TryGetValue(customTab.Name, out var selectedPaths);
            var hasFolderState = folderOptionByTab.TryGetValue(customTab.Name, out var folderOptions);
            previewTabs.Add(CreatePreviewTab(
                customTab,
                hasSelectedState ? selectedPaths : null,
                hasFolderState ? folderOptions : null));
        }

        DetachPreviewTabHandlers();

        PreviewTabs.Clear();
        foreach (var previewTab in previewTabs)
        {
            previewTab.Changed += OnPreviewTabChanged;
            _attachedPreviewTabs.Add(previewTab);
            PreviewTabs.Add(previewTab);
        }

        SelectedPreviewTab = PreviewTabs.FirstOrDefault(tab =>
                               tab.TabName.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                           ?? PreviewTabs.FirstOrDefault(tab =>
                               tab.TabName.Equals(previousName, StringComparison.OrdinalIgnoreCase))
                           ?? PreviewTabs.FirstOrDefault();

        Title = $"\u30AB\u30B9\u30BF\u30E0\u30BF\u30D6\u7DE8\u96C6/\u30D7\u30EC\u30D3\u30E5\u30FC ({previewTabs.Count})";
        UpdatePreviewSummary();
        OnPropertyChanged(nameof(SelectedZipNamePreview));
        SyncZipPlansToMainViewModel();
    }

    private Dictionary<string, HashSet<string>> SnapshotSelectedRows()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in PreviewTabs)
        {
            map[tab.TabName] = tab.Rows
                .Where(row => row.IsSelected)
                .Select(row => row.SourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return map;
    }

    private Dictionary<string, Dictionary<string, bool>> SnapshotFolderOptions()
    {
        var map = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in PreviewTabs)
        {
            map[tab.TabName] = tab.FolderOptions
                .ToDictionary(
                    option => option.FolderName,
                    option => option.IncludeFolderInArchive,
                    StringComparer.OrdinalIgnoreCase);
        }

        return map;
    }

    private static CustomTabPreviewTabViewModel CreatePreviewTab(
        CustomTabViewModel tab,
        ISet<string>? selectedSourcePaths,
        IReadOnlyDictionary<string, bool>? folderOptions)
    {
        var rows = tab.GetSelectedFiles()
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => new CustomTabPreviewRow(
                selectedSourcePaths == null || selectedSourcePaths.Contains(file.SourcePath),
                GetNearestFolderName(file.SourcePath),
                file.FileName,
                file.SourcePath,
                BuildMetadata(file.ColumnValues)))
            .ToList();

        var options = rows
            .Select(row => row.Folder)
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && folder != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .Select(folder =>
            {
                var includeFolder = true;
                if (folderOptions != null &&
                    folderOptions.TryGetValue(folder, out var saved))
                {
                    includeFolder = saved;
                }

                return new FolderZipOptionViewModel(folder, includeFolder);
            })
            .ToList();

        return new CustomTabPreviewTabViewModel(tab.Name, rows, options);
    }

    private static string GetNearestFolderName(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "-";
        }

        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "-";
        }

        var folder = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(folder) ? "-" : folder;
    }

    private static string BuildMetadata(IReadOnlyDictionary<string, string> columnValues)
    {
        if (columnValues.Count == 0)
        {
            return "-";
        }

        var pairs = columnValues
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .ToArray();

        return pairs.Length == 0 ? "-" : string.Join(" / ", pairs);
    }

    private void AttachHandlers(IEnumerable<CustomTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            if (_attachedTabs.Add(tab))
            {
                tab.Changed += OnCustomTabChanged;
            }
        }
    }

    private void DetachHandlers(IEnumerable<CustomTabViewModel> tabs)
    {
        foreach (var tab in tabs)
        {
            if (_attachedTabs.Remove(tab))
            {
                tab.Changed -= OnCustomTabChanged;
            }
        }
    }

    private void DetachPreviewTabHandlers()
    {
        foreach (var previewTab in _attachedPreviewTabs)
        {
            previewTab.Changed -= OnPreviewTabChanged;
        }
        _attachedPreviewTabs.Clear();
    }

    private void OnCustomTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            DetachHandlers(e.OldItems.Cast<CustomTabViewModel>());
        }

        if (e.NewItems != null)
        {
            AttachHandlers(e.NewItems.Cast<CustomTabViewModel>());
        }

        RefreshPreview();
    }

    private void OnCustomTabChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RefreshPreview);
            return;
        }

        RefreshPreview();
    }

    private void OnPreviewTabChanged(object? sender, EventArgs e)
    {
        UpdatePreviewSummary();
        SyncZipPlansToMainViewModel();
    }

    private void UpdatePreviewSummary()
    {
        var tabCount = PreviewTabs.Count;
        var fileCount = PreviewTabs.Sum(tab => tab.Count);
        var selectedCount = PreviewTabs.Sum(tab => tab.SelectedCount);
        PreviewSummary = $"\u30BF\u30D6\u6570: {tabCount}  \u8868\u793A\u30D5\u30A1\u30A4\u30EB\u5408\u8A08: {fileCount}  \u5727\u7E2E\u5BFE\u8C61: {selectedCount}";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        MainVm.CustomTabs.CollectionChanged -= OnCustomTabsCollectionChanged;
        DetachHandlers(_attachedTabs.ToArray());
        DetachPreviewTabHandlers();
        Closed -= OnClosed;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
    }

    private void SelectAllPreviewRowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreviewTab == null)
        {
            return;
        }

        SelectedPreviewTab.SetRowSelection(true);
    }

    private void ClearPreviewRowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreviewTab == null)
        {
            return;
        }

        SelectedPreviewTab.SetRowSelection(false);
    }

    private void ApplyZipPlanButton_Click(object sender, RoutedEventArgs e)
    {
        SyncZipPlansToMainViewModel();
        var planCount = MainVm.GetCustomZipPlans().Count;
        WpfMessageBox.Show($"\u30AD\u30E5\u30FC\u8FFD\u52A0\u6642\u306E\u5727\u7E2E\u8A2D\u5B9A\u3092\u53CD\u6620\u3057\u307E\u3057\u305F\u3002\n\u30BF\u30D6\u6570: {planCount}", "\u53CD\u6620\u5B8C\u4E86", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SyncZipPlansToMainViewModel()
    {
        MainVm.SetCustomZipPlans(BuildZipPlans());
        MainVm.RefreshBasketForCustomZipPlans();
    }

    private IReadOnlyList<CustomZipPlan> BuildZipPlans()
    {
        var plans = new List<CustomZipPlan>();
        var tabCount = PreviewTabs.Count;
        foreach (var previewTab in PreviewTabs)
        {
            var archiveBaseName = ResolveArchiveBaseName(previewTab.TabName, tabCount);
            if (string.IsNullOrWhiteSpace(archiveBaseName))
            {
                continue;
            }

            var items = previewTab.Rows
                .Where(row => row.IsSelected)
                .Select(row => new CustomZipPlanItem(
                    row.SourcePath,
                    row.Folder,
                    row.FileName,
                    previewTab.GetIncludeFolderInArchive(row.Folder)))
                .ToList();

            if (items.Count == 0)
            {
                continue;
            }

            plans.Add(new CustomZipPlan(previewTab.TabName, archiveBaseName, items));
        }

        return plans;
    }

    private string ResolveArchiveBaseName(string tabName, int totalTabCount)
    {
        var baseName = IsCustomZipNameEnabled ? CustomZipBaseName : tabName;
        var sanitized = SanitizeFileName(baseName);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return string.Empty;
        }

        if (!IsCustomZipNameEnabled || totalTabCount <= 1)
        {
            return sanitized;
        }

        var tabSuffix = SanitizeFileName(tabName);
        if (string.IsNullOrWhiteSpace(tabSuffix))
        {
            return sanitized;
        }

        return $"{sanitized}_{tabSuffix}";
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var ch in fileName.Trim())
        {
            if (!invalidChars.Contains(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CustomTabDataGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, CustomTabViewModel.SourcePathColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            return;
        }

        if (string.Equals(e.PropertyName, CustomTabViewModel.SelectColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Column.Width = new DataGridLength(60);
            return;
        }

        if (string.Equals(e.PropertyName, CustomTabViewModel.FolderColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Column.Width = new DataGridLength(140);
            return;
        }

        if (string.Equals(e.PropertyName, CustomTabViewModel.FileNameColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Column.Width = new DataGridLength(220);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CustomTabPreviewTabViewModel
{
    public CustomTabPreviewTabViewModel(
        string tabName,
        IReadOnlyCollection<CustomTabPreviewRow> rows,
        IReadOnlyCollection<FolderZipOptionViewModel> folderOptions)
    {
        TabName = tabName;
        Rows = new ObservableCollection<CustomTabPreviewRow>(rows);
        FolderOptions = new ObservableCollection<FolderZipOptionViewModel>(folderOptions);

        foreach (var row in Rows)
        {
            row.PropertyChanged += OnChildPropertyChanged;
        }

        foreach (var option in FolderOptions)
        {
            option.PropertyChanged += OnChildPropertyChanged;
        }
    }

    public string TabName { get; }
    public int Count => Rows.Count;
    public int SelectedCount => Rows.Count(row => row.IsSelected);
    public ObservableCollection<CustomTabPreviewRow> Rows { get; }
    public ObservableCollection<FolderZipOptionViewModel> FolderOptions { get; }

    public event EventHandler? Changed;

    public void SetRowSelection(bool isSelected)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = isSelected;
        }
    }

    public bool GetIncludeFolderInArchive(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.Equals(folderName, "-", StringComparison.Ordinal))
        {
            return false;
        }

        var option = FolderOptions.FirstOrDefault(item => item.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase));
        return option?.IncludeFolderInArchive ?? true;
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class CustomTabPreviewRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public CustomTabPreviewRow(
        bool isSelected,
        string folder,
        string fileName,
        string sourcePath,
        string metadata)
    {
        _isSelected = isSelected;
        Folder = folder;
        FileName = fileName;
        SourcePath = sourcePath;
        Metadata = metadata;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Folder { get; }
    public string FileName { get; }
    public string SourcePath { get; }
    public string Metadata { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class FolderZipOptionViewModel : INotifyPropertyChanged
{
    private bool _includeFolderInArchive;

    public FolderZipOptionViewModel(string folderName, bool includeFolderInArchive)
    {
        FolderName = folderName;
        _includeFolderInArchive = includeFolderInArchive;
    }

    public string FolderName { get; }

    public bool IncludeFolderInArchive
    {
        get => _includeFolderInArchive;
        set
        {
            if (_includeFolderInArchive == value)
            {
                return;
            }

            _includeFolderInArchive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncludeFolderInArchive)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
