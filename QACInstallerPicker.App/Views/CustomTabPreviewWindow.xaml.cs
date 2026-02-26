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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    private RegisteredCustomZipPlanViewModel? _selectedRegisteredZipPlan;
    private string _currentArchiveBaseName = string.Empty;

    public CustomTabPreviewWindow(MainViewModel mainVm)
    {
        MainVm = mainVm;
        InitializeComponent();
        DataContext = this;

        MainVm.CustomTabs.CollectionChanged += OnCustomTabsCollectionChanged;
        AttachHandlers(MainVm.CustomTabs);
        RefreshPreview();
        LoadRegisteredZipPlansFromMainViewModel();
        Closed += OnClosed;
    }

    public MainViewModel MainVm { get; }

    public ObservableCollection<CustomTabPreviewTabViewModel> PreviewTabs { get; } = new();
    public ObservableCollection<RegisteredCustomZipPlanViewModel> RegisteredZipPlans { get; } = new();

    public ObservableCollection<FolderZipOptionViewModel> CurrentFolderOptions
    {
        get => SelectedPreviewTab?.FolderOptions ?? EmptyFolderOptions;
    }

    public string CurrentArchiveBaseName
    {
        get => _currentArchiveBaseName;
        set
        {
            if (string.Equals(_currentArchiveBaseName, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentArchiveBaseName = value;
            OnPropertyChanged();
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
            EnsureArchiveBaseName();
            UpdatePreviewSummary();
        }
    }

    public RegisteredCustomZipPlanViewModel? SelectedRegisteredZipPlan
    {
        get => _selectedRegisteredZipPlan;
        set
        {
            if (ReferenceEquals(_selectedRegisteredZipPlan, value))
            {
                return;
            }

            _selectedRegisteredZipPlan = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static ObservableCollection<FolderZipOptionViewModel> EmptyFolderOptions { get; } = new();

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

        Title = $"カスタムタブ編集/圧縮プレビュー ({previewTabs.Count})";
        PruneRegisteredZipPlans();
        EnsureArchiveBaseName();
        UpdatePreviewSummary();
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
            .Select(file => new CustomTabPreviewRow(
                selectedSourcePaths == null || selectedSourcePaths.Contains(file.SourcePath),
                GetNearestFolderName(file.SourcePath),
                file.FileName,
                file.SourcePath,
                BuildMetadata(file.ColumnValues),
                file.IsSelectionEnabled,
                file.SelectionLockReason))
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
    }

    private void UpdatePreviewSummary()
    {
        var tabCount = PreviewTabs.Count;
        var fileCount = PreviewTabs.Sum(tab => tab.Count);
        var selectedCount = PreviewTabs.Sum(tab => tab.SelectedCount);
        var zipCount = RegisteredZipPlans.Count;
        PreviewSummary = $"タブ数: {tabCount}  表示ファイル合計: {fileCount}  圧縮対象: {selectedCount}  登録ZIP: {zipCount}";
    }

    private void EnsureArchiveBaseName()
    {
        if (SelectedPreviewTab == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentArchiveBaseName))
        {
            CurrentArchiveBaseName = SelectedPreviewTab.TabName;
        }
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

    private void RegisterZipPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreviewTab == null)
        {
            WpfMessageBox.Show("タブを選択してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rows = SelectedPreviewTab.Rows
            .Where(row => row.IsSelected)
            .ToList();
        if (rows.Count == 0)
        {
            WpfMessageBox.Show("圧縮対象のファイルにチェックを入れてください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var archiveBaseName = SanitizeFileName(CurrentArchiveBaseName);
        if (string.IsNullOrWhiteSpace(archiveBaseName))
        {
            archiveBaseName = SanitizeFileName(SelectedPreviewTab.TabName);
        }

        if (string.IsNullOrWhiteSpace(archiveBaseName))
        {
            archiveBaseName = "custom";
        }

        var items = rows
            .Select(row => new CustomZipPlanItem(
                row.SourcePath,
                row.Folder,
                row.FileName,
                SelectedPreviewTab.GetIncludeFolderInArchive(row.Folder)))
            .ToList();

        var plan = new CustomZipPlan(SelectedPreviewTab.TabName, archiveBaseName, items);
        var vm = new RegisteredCustomZipPlanViewModel(plan);
        var existing = RegisteredZipPlans
            .Select((item, index) => new { item, index })
            .FirstOrDefault(entry => entry.item.Matches(plan.TabName, plan.ArchiveBaseName));

        if (existing != null)
        {
            RegisteredZipPlans[existing.index] = vm;
            SelectedRegisteredZipPlan = RegisteredZipPlans[existing.index];
        }
        else
        {
            RegisteredZipPlans.Add(vm);
            SelectedRegisteredZipPlan = vm;
        }

        SyncRegisteredZipPlansToMainViewModel();
        UpdatePreviewSummary();
    }

    private void RemoveRegisteredZipPlanButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRegisteredZipPlan == null)
        {
            WpfMessageBox.Show("削除する圧縮登録を選択してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RegisteredZipPlans.Remove(SelectedRegisteredZipPlan);
        SelectedRegisteredZipPlan = RegisteredZipPlans.FirstOrDefault();
        SyncRegisteredZipPlansToMainViewModel();
        UpdatePreviewSummary();
    }

    private void LoadRegisteredZipPlansFromMainViewModel()
    {
        RegisteredZipPlans.Clear();
        foreach (var plan in MainVm.GetCustomZipPlans())
        {
            if (string.IsNullOrWhiteSpace(plan.TabName) || string.IsNullOrWhiteSpace(plan.ArchiveBaseName))
            {
                continue;
            }

            if (plan.Items == null || plan.Items.Count == 0)
            {
                continue;
            }

            RegisteredZipPlans.Add(new RegisteredCustomZipPlanViewModel(plan));
        }

        SelectedRegisteredZipPlan = RegisteredZipPlans.FirstOrDefault();
        UpdatePreviewSummary();
    }

    private void PruneRegisteredZipPlans()
    {
        var tabNames = MainVm.CustomTabs
            .Select(tab => tab.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = false;
        for (var index = RegisteredZipPlans.Count - 1; index >= 0; index--)
        {
            if (tabNames.Contains(RegisteredZipPlans[index].TabName))
            {
                continue;
            }

            RegisteredZipPlans.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            SelectedRegisteredZipPlan = RegisteredZipPlans.FirstOrDefault();
            SyncRegisteredZipPlansToMainViewModel();
        }
    }

    private void SyncRegisteredZipPlansToMainViewModel()
    {
        var plans = RegisteredZipPlans
            .Select(plan => plan.ToPlan())
            .ToList();

        MainVm.SetCustomZipPlans(plans);
        MainVm.RefreshBasketForCustomZipPlans();
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
        if (string.Equals(e.PropertyName, CustomTabViewModel.SourcePathColumnName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.PropertyName, CustomTabViewModel.SelectionEnabledColumnName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.PropertyName, CustomTabViewModel.SelectionLockReasonColumnName, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            return;
        }

        if (string.Equals(e.PropertyName, CustomTabViewModel.SelectColumnName, StringComparison.OrdinalIgnoreCase))
        {
            var checkBoxFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
            checkBoxFactory.SetValue(System.Windows.Controls.CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            checkBoxFactory.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding($"[{CustomTabViewModel.SelectColumnName}]")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
            checkBoxFactory.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding($"[{CustomTabViewModel.SelectionEnabledColumnName}]"));
            checkBoxFactory.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding($"[{CustomTabViewModel.SelectionLockReasonColumnName}]"));

            var template = new DataTemplate
            {
                VisualTree = checkBoxFactory
            };

            e.Column = new DataGridTemplateColumn
            {
                Header = CustomTabViewModel.SelectColumnName,
                Width = new DataGridLength(60),
                CellTemplate = template
            };
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
            if (row.IsSelectionEnabled)
            {
                row.IsSelected = isSelected;
            }
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
        string metadata,
        bool isSelectionEnabled,
        string selectionLockReason)
    {
        IsSelectionEnabled = isSelectionEnabled;
        SelectionLockReason = selectionLockReason;
        _isSelected = isSelectionEnabled ? isSelected : true;
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
            var target = IsSelectionEnabled ? value : true;
            if (_isSelected == target)
            {
                return;
            }

            _isSelected = target;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool IsSelectionEnabled { get; }
    public string SelectionLockReason { get; }
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

public sealed class RegisteredCustomZipPlanViewModel
{
    public RegisteredCustomZipPlanViewModel(CustomZipPlan plan)
    {
        TabName = plan.TabName;
        ArchiveBaseName = plan.ArchiveBaseName;
        Items = plan.Items.ToList();
    }

    public string TabName { get; }
    public string ArchiveBaseName { get; }
    public IReadOnlyList<CustomZipPlanItem> Items { get; }
    public string ZipFileName => $"{ArchiveBaseName}.zip";
    public int ItemCount => Items.Count;

    public string ContentsPreview
    {
        get
        {
            if (Items.Count == 0)
            {
                return "-";
            }

            var previews = Items
                .Take(5)
                .Select(BuildEntryName)
                .ToList();
            if (Items.Count > previews.Count)
            {
                previews.Add($"...（他 {Items.Count - previews.Count} 件）");
            }

            return string.Join(" / ", previews);
        }
    }

    public string ContentsFull
    {
        get
        {
            if (Items.Count == 0)
            {
                return "-";
            }

            var lines = Items
                .Select(BuildEntryName)
                .ToArray();
            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool Matches(string tabName, string archiveBaseName)
    {
        return TabName.Equals(tabName, StringComparison.OrdinalIgnoreCase)
               && ArchiveBaseName.Equals(archiveBaseName, StringComparison.OrdinalIgnoreCase);
    }

    public CustomZipPlan ToPlan()
    {
        return new CustomZipPlan(TabName, ArchiveBaseName, Items.ToList());
    }

    private static string BuildEntryName(CustomZipPlanItem item)
    {
        var prefix = item.IncludeFolderInArchive &&
                     !string.IsNullOrWhiteSpace(item.FolderName) &&
                     !string.Equals(item.FolderName, "-", StringComparison.Ordinal)
            ? $"{item.FolderName}/"
            : string.Empty;
        return $"{prefix}{item.FileName}";
    }
}
