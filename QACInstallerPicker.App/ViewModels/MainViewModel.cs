using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;
using QACInstallerPicker.App.Services;
using Win32 = Microsoft.Win32;
using WpfMessageBox = System.Windows.MessageBox;

namespace QACInstallerPicker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ExcelService _excelService;
    private readonly InstallerScanService _scanService;
    private readonly MemoParserService _memoService;
    private readonly DatabaseService _databaseService;
    private readonly HashService _hashService;
    private readonly CopyService _copyService;
    private TransferManager? _transferManager;

    private CompatibilityData? _compatibility;
    private Dictionary<string, List<string>> _synonyms = new(StringComparer.OrdinalIgnoreCase);
    private List<LogicalItem> _logicalItems = new();
    private readonly List<ManualPickEntry> _manualPicks = new();
    private bool _suppressSelectionSync;
    private static readonly Regex VersionRegex = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);
    private static readonly Regex VersionNumberRegex = new(@"\d+", RegexOptions.Compiled);
    private const string ScanOnlyVersionLabel = "共有スキャン";
    private const string HelixQacCode = "HelixQAC";
    private const string OsTokenWindows = "windows";
    private const string OsTokenLinux = "linux";
    private static readonly IReadOnlyDictionary<string, string> BundledModuleMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HelixQAC"] = "QAC",
            ["Helix"] = "QAC",
            ["QAC++"] = "QAC",
            ["QACPP"] = "QAC",
            ["RCMA"] = "QAC",
            ["NAMECHECK"] = "QAC",
            ["MTA"] = "QAC",
            ["DFA"] = "QAC"
        };
    private static readonly HashSet<string> HelixQacBundleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        HelixQacCode,
        "QAC",
        "Helix",
        "QAC++",
        "QACPP",
        "RCMA",
        "NAMECHECK",
        "MTA",
        "DFA"
    };
    private static readonly IReadOnlyDictionary<string, string[]> ModuleCodeAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["QACPP"] = new[] { "QAC++" },
            ["QAC++"] = new[] { "QACPP" }
        };
    private static readonly HashSet<string> ExtraModuleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        HelixQacCode,
        "Helix",
        "QAC",
        "QAC++",
        "QACPP",
        "RCMA",
        "NAMECHECK",
        "MTA",
        "DFA",
        "VALIDATE",
        "DASHBOARD"
    };
    private static readonly string[] ComplianceModuleCodes =
    {
        "MCM",
        "M2CM",
        "M3CM",
        "MCPP",
        "M2CPP",
        "CERTCCM",
        "CERTCPPCM",
        "CWECCM",
        "CWECPPCM",
        "ASCM",
        "SECCCM"
    };
    private const int TransferTabIndex = 2;
    private const string ComplianceModuleSuffix = "コンプライアンスモジュール";

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _excelService = new ExcelService();
        _scanService = new InstallerScanService();
        _memoService = new MemoParserService();
        _databaseService = new DatabaseService();
        _hashService = new HashService(_databaseService);
        _copyService = new CopyService();

        _settings = _settingsService.Load();
        _maxConcurrentTransfers = _settings.MaxConcurrentTransfers;
        _maxConcurrentTransfersInput = _maxConcurrentTransfers.ToString();

        HelixVersions = new ObservableCollection<HelixVersionViewModel>();
        BasketItems = new ObservableCollection<BasketItemViewModel>();
        TransferItems = new ObservableCollection<TransferItemViewModel>();
        HistoryItems = new ObservableCollection<HistoryItemViewModel>();
        UnresolvedTerms = new ObservableCollection<string>();
        AmbiguousTerms = new ObservableCollection<AmbiguousMatchViewModel>();
        TransferSummary = new TransferSummaryViewModel();
        ScanLogicalItems = new ObservableCollection<LogicalItem>();
        ScanAssets = new ObservableCollection<InstallerAsset>();
        ScanErrors = new ObservableCollection<string>();
        ScanSelectionItems = new ObservableCollection<ScanSelectionItemViewModel>();
    }

    public ObservableCollection<HelixVersionViewModel> HelixVersions { get; }
    public ObservableCollection<BasketItemViewModel> BasketItems { get; }
    public ObservableCollection<TransferItemViewModel> TransferItems { get; }
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; }
    public ObservableCollection<string> UnresolvedTerms { get; }
    public ObservableCollection<AmbiguousMatchViewModel> AmbiguousTerms { get; }
    public TransferSummaryViewModel TransferSummary { get; }
    public ObservableCollection<LogicalItem> ScanLogicalItems { get; }
    public ObservableCollection<InstallerAsset> ScanAssets { get; }
    public ObservableCollection<string> ScanErrors { get; }
    public ObservableCollection<ScanSelectionItemViewModel> ScanSelectionItems { get; }

    public event EventHandler? RequestOpenSettings;

    [ObservableProperty]
    private SettingsModel _settings;

    [ObservableProperty]
    private HelixVersionViewModel? _selectedVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _memoText = string.Empty;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private int _selectedMainTabIndex;

    [ObservableProperty]
    private string _scanSummaryText = "スキャン未実施";

    [ObservableProperty]
    private string _quickRequestResult = string.Empty;

    [ObservableProperty]
    private string _uploadListText = string.Empty;

    [ObservableProperty]
    private int _maxConcurrentTransfers;

    [ObservableProperty]
    private string _maxConcurrentTransfersInput = string.Empty;

    private bool _suppressUploadListEdit;
    private bool _uploadListUserEdited;

    public string OutputFolderPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputBaseFolder) || string.IsNullOrWhiteSpace(CompanyName))
            {
                return string.Empty;
            }

            return Path.Combine(OutputBaseFolder, CompanyName, DateTime.Now.ToString("yyyyMMdd"));
        }
    }

    public string OutputBaseFolder
    {
        get => Settings.OutputBaseFolder;
        set
        {
            if (!string.Equals(Settings.OutputBaseFolder, value, StringComparison.Ordinal))
            {
                Settings.OutputBaseFolder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputFolderPreview));
            }
        }
    }

    public string SettingsSummary
    {
        get
        {
            return $"対応表: {Settings.ExcelPath} | 共有: {Settings.UncRoot}";
        }
    }

    public async Task InitializeAsync()
    {
        await _databaseService.InitializeAsync();
        _synonyms = _memoService.LoadSynonyms(AppPaths.SynonymsPath);
        AddComplianceAliases(_synonyms);

        if (string.IsNullOrWhiteSpace(Settings.ExcelPath) || !File.Exists(Settings.ExcelPath))
        {
            RequestOpenSettings?.Invoke(this, EventArgs.Empty);
        }

        if (!string.IsNullOrWhiteSpace(Settings.ExcelPath) && File.Exists(Settings.ExcelPath))
        {
            await LoadExcelAsync();
        }

        if (!string.IsNullOrWhiteSpace(Settings.UncRoot) && Directory.Exists(Settings.UncRoot))
        {
            await ScanInstallersAsync();
        }

        await LoadPendingTransfersAsync();
        await RefreshHistoryAsync();
        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
    }

    public void ReloadSettings()
    {
        Settings = _settingsService.Load();
        if (TransferItems.Count == 0)
        {
            _transferManager = null;
        }

        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(OutputFolderPreview));
    }

    public async Task ApplySettingsAndReloadAsync()
    {
        ReloadSettings();

        if (!string.IsNullOrWhiteSpace(Settings.ExcelPath) && File.Exists(Settings.ExcelPath))
        {
            await LoadExcelAsync();
        }

        if (!string.IsNullOrWhiteSpace(Settings.UncRoot) && Directory.Exists(Settings.UncRoot))
        {
            await ScanInstallersAsync();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        RequestOpenSettings?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task LoadExcelAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Settings.ExcelPath))
            {
                WpfMessageBox.Show("対応表(Excel)パスを設定してください。", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(Settings.ExcelPath))
            {
                WpfMessageBox.Show($"対応表(Excel)が見つかりません: {Settings.ExcelPath}", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _compatibility = _excelService.LoadCompatibility(Settings.ExcelPath);
            BuildHelixTabs();
        }
        catch (IOException ex)
        {
            WpfMessageBox.Show($"対応表が他のアプリで使用中です。閉じてから再試行してください。\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"対応表読み込みに失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ScanInstallersAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Settings.UncRoot))
            {
                WpfMessageBox.Show("UNCルートを設定してください。", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
                ScanSummaryText = "UNCルートが未設定です";
                return;
            }

            using var cts = new CancellationTokenSource();
            var result = await _scanService.ScanAsync(Settings.UncRoot, cts.Token);
            _logicalItems = result.Items;
            UpdateScanResults(result);

            if (result.Errors.Count > 0)
            {
                WpfMessageBox.Show(string.Join(Environment.NewLine, result.Errors.Take(5)), "Scan Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            UpdateBasket();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"UNCスキャンに失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ScanSummaryText = $"スキャン失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ApplyMemoParse()
    {
        if (SelectedVersion == null && ScanSelectionItems.Count == 0)
        {
            WpfMessageBox.Show("先に共有スキャンを実行してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var knownCodes = GetKnownModuleCodes();
        var result = _memoService.ParseMemo(MemoText ?? string.Empty, knownCodes, _synonyms);

        UnresolvedTerms.Clear();
        foreach (var term in result.UnresolvedTerms)
        {
            UnresolvedTerms.Add(term);
        }

        AmbiguousTerms.Clear();
        foreach (var match in result.AmbiguousMatches)
        {
            var vm = new AmbiguousMatchViewModel(match.Term, match.Candidates);
            vm.SelectedCodeChanged += (_, code) => SelectByCode(code, null);
            AmbiguousTerms.Add(vm);
        }

        foreach (var code in result.MatchedCodes)
        {
            SelectByCode(code, null);
        }

        UpdateBasket();
    }

    [RelayCommand]
    private void ApplyQuickRequest()
    {
        if (string.IsNullOrWhiteSpace(MemoText))
        {
            WpfMessageBox.Show("メール/メモを入力してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var hasModules = SelectedVersion != null;
        var hasScanItems = ScanSelectionItems.Count > 0;
        if (!hasModules && !hasScanItems)
        {
            WpfMessageBox.Show("先に共有スキャンを実行してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearManualPicks();

        var helixMatch = FindHelixVersionFromText(MemoText);
        var versionResult = "バージョン: (選択中のまま)";
        if (helixMatch != null)
        {
            SelectedVersion = helixMatch;
            versionResult = $"バージョン: {helixMatch.Version}";
        }
        else
        {
            var requestedHelixVersion = FindRequestedHelixVersionToken(MemoText);
            if (!string.IsNullOrWhiteSpace(requestedHelixVersion))
            {
                versionResult = $"該当バージョンなし: {requestedHelixVersion}";
            }
        }

        WithSelectionSyncSuppressed(() =>
        {
            foreach (var helix in HelixVersions)
            {
                foreach (var module in helix.Modules)
                {
                    module.SetSelectedSilently(false);
                }
            }

            foreach (var item in ScanSelectionItems)
            {
                item.IsSelected = false;
            }
        });

        var knownCodes = GetKnownModuleCodes();
        var versionedRequests = ParseVersionedRequests(MemoText, knownCodes);
        var unmatchedCodes = new List<string>();
        var handledSelectionTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var osRequests = new Dictionary<ModuleRowViewModel, RequestedOs>();
        foreach (var request in versionedRequests)
        {
            if (!SelectAcrossTabsByVersion(request.Code, request.Version, request.OsSelection, osRequests))
            {
                unmatchedCodes.Add(request.Code);
            }

            handledSelectionTargets.Add(GetSelectionTargetCode(request.Code));
        }

        var parseResult = _memoService.ParseMemo(MemoText, knownCodes, _synonyms);
        UnresolvedTerms.Clear();
        foreach (var term in parseResult.UnresolvedTerms)
        {
            UnresolvedTerms.Add(term);
        }

        AmbiguousTerms.Clear();
        foreach (var match in parseResult.AmbiguousMatches)
        {
            var vm = new AmbiguousMatchViewModel(match.Term, match.Candidates);
            vm.SelectedCodeChanged += (_, code) => SelectByCode(code, null);
            AmbiguousTerms.Add(vm);
        }

        foreach (var code in parseResult.MatchedCodes)
        {
            if (handledSelectionTargets.Contains(GetSelectionTargetCode(code)))
            {
                continue;
            }

            if (!SelectByCode(code, string.Empty))
            {
                unmatchedCodes.Add(code);
            }
        }

        if (versionedRequests.Count == 0)
        {
            AddBasePickIfRequested(MemoText, string.Empty);
        }

        ApplyQuickRequestOsSelection(osRequests);
        UpdateBasket();

        var selectedCodes = HelixVersions.Count > 0
            ? HelixVersions.SelectMany(h => h.Modules).Where(m => m.IsSelected).Select(m => m.Code).Distinct().ToArray()
            : ScanSelectionItems.Where(item => item.IsSelected).Select(item => item.Code).Distinct().ToArray();
        var unresolved = parseResult.AmbiguousMatches.Select(m => m.Term).ToArray();

        var summary = new List<string> { versionResult };
        if (selectedCodes.Length > 0)
        {
            summary.Add($"モジュール: {string.Join(", ", selectedCodes)}");
        }
        if (unmatchedCodes.Count > 0)
        {
            summary.Add($"選択不可: {string.Join(", ", unmatchedCodes)}");
        }
        if (unresolved.Length > 0)
        {
            summary.Add($"曖昧: {string.Join(", ", unresolved)}");
        }

        QuickRequestResult = string.Join(" | ", summary);
    }

    [RelayCommand]
    private void ClearMemo()
    {
        MemoText = string.Empty;
        QuickRequestResult = string.Empty;
        UnresolvedTerms.Clear();
        AmbiguousTerms.Clear();
    }

    [RelayCommand]
    private void ApplyMaxConcurrentTransfers()
    {
        if (!int.TryParse(MaxConcurrentTransfersInput, out var value) || value < 1)
        {
            WpfMessageBox.Show("同時実行数は1以上の数値で入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            MaxConcurrentTransfersInput = MaxConcurrentTransfers.ToString();
            return;
        }

        MaxConcurrentTransfers = value;
    }

    [RelayCommand]
    private void SelectAllModules()
    {
        if (SelectedVersion != null)
        {
            foreach (var module in SelectedVersion.Modules)
            {
                if (module.IsEnabled)
                {
                    module.IsSelected = true;
                }
            }

            UpdateBasket();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            return;
        }

        foreach (var item in ScanSelectionItems)
        {
            if (item.IsEnabled)
            {
                item.IsSelected = true;
            }
        }

        UpdateBasket();
    }

    [RelayCommand]
    private void ClearModuleSelection()
    {
        if (SelectedVersion != null)
        {
            foreach (var module in SelectedVersion.Modules)
            {
                module.IsSelected = false;
            }

            ClearManualPicks();
            QuickRequestResult = string.Empty;
            UpdateBasket();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            return;
        }

        foreach (var item in ScanSelectionItems)
        {
            item.IsSelected = false;
        }

        ClearManualPicks();
        QuickRequestResult = string.Empty;
        UpdateBasket();
    }

    [RelayCommand]
    private void CopyUploadList()
    {
        if (string.IsNullOrWhiteSpace(UploadListText))
        {
            return;
        }

        System.Windows.Clipboard.SetText(UploadListText);
    }

    [RelayCommand]
    private void RemoveBasketItem(BasketItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        if (TryUpdateModuleSelectionFromBasketItem(item))
        {
            return;
        }

        if (TryRemoveManualPick(item))
        {
            UpdateBasket();
        }
    }

    [RelayCommand]
    private async Task QueueAddAsync()
    {
        var hasScanItems = ScanSelectionItems.Count > 0;
        if (SelectedVersion == null && !hasScanItems)
        {
            WpfMessageBox.Show("Helixバージョンを選択してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(CompanyName))
        {
            WpfMessageBox.Show("会社名を入力してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputBaseFolder))
        {
            WpfMessageBox.Show("出力フォルダを設定してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MaxConcurrentTransfers = Math.Max(1, MaxConcurrentTransfers);
        _settingsService.Save(Settings);

        var outputRoot = OutputFolderPreview;
        Directory.CreateDirectory(outputRoot);

        var selected = BasketItems.Where(b => !b.IsMissing).ToList();
        var missing = BasketItems.Where(b => b.IsMissing).ToList();
        if (missing.Count > 0)
        {
            WpfMessageBox.Show($"{missing.Count} 件の配布物が未検出です。", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (selected.Count == 0)
        {
            WpfMessageBox.Show("転送対象がありません。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var batches = selected
            .GroupBy(item => NormalizeHelixVersionLabel(item.HelixVersion))
            .ToList();

        foreach (var group in batches)
        {
            var helixVersion = group.Key;
            var versionFolder = GetSafeVersionFolderName(helixVersion);
            var versionOutputRoot = Path.Combine(outputRoot, versionFolder);
            Directory.CreateDirectory(versionOutputRoot);

            var batch = new TransferBatch
            {
                TimestampUtc = DateTime.UtcNow,
                Company = CompanyName,
                HelixVersion = helixVersion,
                OutputRoot = versionOutputRoot,
                Memo = MemoText ?? string.Empty,
                SelectedLogicalItemsJson = JsonSerializer.Serialize(group.Select(s => new { s.Code, s.ModuleVersion, s.Os }))
            };

            batch.Id = await _databaseService.InsertBatchAsync(batch);

            foreach (var basketItem in group)
            {
                var asset = FindAssetBySourcePath(basketItem.SourcePath)
                            ?? FindLogicalAsset(basketItem.Code, basketItem.ModuleVersion, out _);
                if (asset == null)
                {
                    continue;
                }

                var destPath = Path.Combine(versionOutputRoot, asset.FileName);
                var record = new TransferItemRecord
                {
                    BatchId = batch.Id,
                    Company = CompanyName,
                    LogicalKey = $"{asset.Code}|{asset.Version}|{asset.Os}",
                    AssetSourcePath = asset.SourcePath,
                    DestPath = destPath,
                    Size = asset.Size,
                    Status = TransferStatus.Queued,
                    SourceLastWriteTimeUtc = asset.LastWriteTimeUtc
                };

                record.Id = await _databaseService.InsertTransferItemAsync(record);
                var vm = new TransferItemViewModel(record, TransferManager);
                vm.ProgressChanged += (_, _) => TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
                TransferItems.Add(vm);
                await TransferManager.StartAsync(vm);
            }
        }

        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
        SelectedMainTabIndex = TransferTabIndex;
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private async Task RefreshHistoryAsync()
    {
        HistoryItems.Clear();
        var history = await _databaseService.LoadHistoryAsync();
        foreach (var item in history)
        {
            HistoryItems.Add(new HistoryItemViewModel(item));
        }
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        var dialog = new Win32.SaveFileDialog
        {
            FileName = "history.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _databaseService.ExportHistoryCsvAsync(dialog.FileName);
        WpfMessageBox.Show("CSVを出力しました。", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private TransferManager TransferManager
    {
        get
        {
            _transferManager ??= new TransferManager(_databaseService, _hashService, _copyService, MaxConcurrentTransfers);
            return _transferManager;
        }
    }

    private void BuildHelixTabs()
    {
        HelixVersions.Clear();
        if (_compatibility == null)
        {
            return;
        }

        var moduleCodes = new List<string>(_compatibility.ModuleCodes);
        AddExtraModuleCodes(moduleCodes);
        moduleCodes = NormalizeModuleCodes(moduleCodes);

        foreach (var helix in _compatibility.Versions)
        {
            var modules = new List<ModuleRowViewModel>();
            foreach (var code in moduleCodes)
            {
                var supportInfo = code.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase)
                    ? GetHelixQacSupportInfo(helix)
                    : helix.ModuleSupport.TryGetValue(code, out var info)
                        ? info
                        : null;
                var supported = supportInfo?.IsSupported ?? ExtraModuleCodes.Contains(code);
                var moduleVersion = supportInfo?.ModuleVersion ?? string.Empty;
                if (code.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase))
                {
                    moduleVersion = helix.Version;
                }
                var name = ModuleCatalog.GetDescription(code);
                var isEnabled = supported;
                string? reason = null;

                if (!supported)
                {
                    reason = "対応表で未対応";
                }
                else if (!CompatibilityRules.TryCheckMinVersion(helix.Version, code, out reason))
                {
                    isEnabled = false;
                }

                var aliases = GetAliasesForCode(code);
                var selectionGroupKey = string.Empty;
                var isSelectionLeader = true;
                var moduleVm = new ModuleRowViewModel(
                    code,
                    name,
                    moduleVersion,
                    supported,
                    isEnabled,
                    reason,
                    aliases,
                    selectionGroupKey,
                    isSelectionLeader);
                modules.Add(moduleVm);
            }

            var helixVm = new HelixVersionViewModel(helix.Version, modules);
            helixVm.SelectionChanged += OnModuleSelectionChanged;
            helixVm.OsSelectionChanged += OnModuleOsSelectionChanged;
            helixVm.InstallerVersionChanged += OnModuleInstallerVersionChanged;
            helixVm.ApplyFilter(SearchText);
            HelixVersions.Add(helixVm);
        }

        MergeScanModulesIntoHelixTabs();
        SelectedVersion = HelixVersions.FirstOrDefault();
        UpdateModuleAvailabilityFromScan();
    }

    private void UpdateBasket()
    {
        BasketItems.Clear();
        if (HelixVersions.Count > 0)
        {
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var helix in HelixVersions)
            {
                var helixLabel = NormalizeHelixVersionLabel(helix.Version);
                foreach (var module in helix.Modules.Where(m => m.IsSelected))
                {
                    AddBasketItemsForModule(helixLabel, module, existingKeys, missingKeys);
                }
            }

            foreach (var pick in _manualPicks)
            {
                var helixLabel = NormalizeHelixVersionLabel(pick.HelixVersion);
                if (pick.Asset != null && existingKeys.Contains(GetBasketKey(helixLabel, pick.Asset)))
                {
                    continue;
                }

                var name = ModuleCatalog.GetDescription(pick.Code);
                if (pick.Asset == null)
                {
                    BasketItems.Add(new BasketItemViewModel(
                        helixLabel,
                        pick.Code,
                        name,
                        pick.RequestedVersion,
                        string.Empty,
                        "-",
                        "(Not Found)",
                        string.Empty,
                        true,
                        pick.Reason,
                        true));
                    continue;
                }

                BasketItems.Add(new BasketItemViewModel(
                    helixLabel,
                    pick.Code,
                    name,
                    pick.RequestedVersion,
                    pick.Asset.Version,
                    pick.Asset.Os.ToString(),
                    pick.Asset.FileName,
                    pick.Asset.SourcePath,
                    false,
                    pick.Reason,
                    true));
            }

            UpdateUploadListText();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            UpdateUploadListText();
            return;
        }

        foreach (var item in ScanSelectionItems.Where(item => item.IsSelected))
        {
            BasketItems.Add(new BasketItemViewModel(
                ScanOnlyVersionLabel,
                item.Code,
                item.Name,
                item.Version,
                item.Version,
                item.Os.ToString(),
                item.AssetFileName,
                item.SourcePath,
                false,
                string.Empty,
                false));
        }

        UpdateUploadListText();
    }

    private void AddBasketItemsForModule(
        string helixVersion,
        ModuleRowViewModel module,
        HashSet<string> existingKeys,
        HashSet<string> missingKeys)
    {
        var requestedVersion = GetRequestedVersion(module);
        var selectedOsTypes = GetSelectedOsTypes(module.OsSelection);
        if (selectedOsTypes.Count == 0)
        {
            selectedOsTypes = new List<OsType> { OsType.Windows, OsType.Linux };
        }

        foreach (var osType in selectedOsTypes)
        {
            var asset = FindLogicalAsset(module.Code, requestedVersion, osType, out var reason);
            if (asset == null)
            {
                var missingKey = GetMissingKey(helixVersion, module.Code, requestedVersion, osType);
                if (!missingKeys.Add(missingKey))
                {
                    continue;
                }

                BasketItems.Add(new BasketItemViewModel(
                    helixVersion,
                    module.Code,
                    module.Name,
                    requestedVersion,
                    string.Empty,
                    osType.ToString(),
                    "(Not Found)",
                    string.Empty,
                    true,
                    string.IsNullOrWhiteSpace(reason) ? "未検出" : reason,
                    false));
                continue;
            }

            if (!existingKeys.Add(GetBasketKey(helixVersion, asset)))
            {
                continue;
            }

            BasketItems.Add(new BasketItemViewModel(
                helixVersion,
                module.Code,
                module.Name,
                requestedVersion,
                asset.Version,
                asset.Os.ToString(),
                asset.FileName,
                asset.SourcePath,
                false,
                string.Empty,
                false));
        }
    }

    private bool TryUpdateModuleSelectionFromBasketItem(BasketItemViewModel item)
    {
        if (item.IsManualPick)
        {
            return false;
        }

        var helixLabel = NormalizeHelixVersionLabel(item.HelixVersion);
        var helix = HelixVersions.FirstOrDefault(h =>
            NormalizeHelixVersionLabel(h.Version)
                .Equals(helixLabel, StringComparison.OrdinalIgnoreCase));
        if (helix == null)
        {
            return false;
        }

        var module = helix.Modules.FirstOrDefault(m =>
            m.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase));
        if (module == null || !module.IsSelected)
        {
            return false;
        }

        if (!TryParseOsType(item.Os, out var osType))
        {
            module.IsSelected = false;
            return true;
        }

        if (module.OsSelection.Equals(ModuleRowViewModel.OsSelectionBoth, StringComparison.OrdinalIgnoreCase))
        {
            if (osType == OsType.Windows)
            {
                module.OsSelection = ModuleRowViewModel.OsSelectionLinux;
            }
            else if (osType == OsType.Linux)
            {
                module.OsSelection = ModuleRowViewModel.OsSelectionWindows;
            }
            else
            {
                module.IsSelected = false;
            }

            return true;
        }

        if (module.OsSelection.Equals(ModuleRowViewModel.OsSelectionWindows, StringComparison.OrdinalIgnoreCase))
        {
            if (osType == OsType.Windows)
            {
                module.IsSelected = false;
            }

            return true;
        }

        if (module.OsSelection.Equals(ModuleRowViewModel.OsSelectionLinux, StringComparison.OrdinalIgnoreCase))
        {
            if (osType == OsType.Linux)
            {
                module.IsSelected = false;
            }

            return true;
        }

        module.IsSelected = false;
        return true;
    }

    private bool TryRemoveManualPick(BasketItemViewModel item)
    {
        if (!item.IsManualPick)
        {
            return false;
        }

        var helixLabel = NormalizeHelixVersionLabel(item.HelixVersion);
        var removed = _manualPicks.RemoveAll(p =>
            NormalizeHelixVersionLabel(p.HelixVersion)
                .Equals(helixLabel, StringComparison.OrdinalIgnoreCase) &&
            p.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase) &&
            p.RequestedVersion.Equals(item.ModuleVersion, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    private static bool TryParseOsType(string? value, out OsType osType)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, true, out OsType parsed))
        {
            osType = parsed;
            return true;
        }

        osType = OsType.Unknown;
        return false;
    }

    private static void AddExtraModuleCodes(List<string> moduleCodes)
    {
        foreach (var code in ExtraModuleCodes)
        {
            if (code.Equals("QAC++", StringComparison.OrdinalIgnoreCase))
            {
                if (moduleCodes.Any(existing =>
                        existing.Equals("QAC++", StringComparison.OrdinalIgnoreCase) ||
                        existing.Equals("QACPP", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (moduleCodes.Any(existing => existing.Equals(code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            moduleCodes.Add(code);
        }
    }

    private static bool IsHelixQacBundleCode(string code)
    {
        return HelixQacBundleCodes.Contains(code);
    }

    private static string GetSelectionTargetCode(string code)
    {
        return IsHelixQacBundleCode(code) ? HelixQacCode : code;
    }

    private static List<string> NormalizeModuleCodes(IEnumerable<string> codes)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedHelixQac = false;
        foreach (var code in codes)
        {
            if (IsHelixQacBundleCode(code))
            {
                if (!addedHelixQac && seen.Add(HelixQacCode))
                {
                    normalized.Add(HelixQacCode);
                    addedHelixQac = true;
                }

                continue;
            }

            if (seen.Add(code))
            {
                normalized.Add(code);
            }
        }

        return normalized;
    }

    private static ModuleSupportInfo? GetHelixQacSupportInfo(HelixVersionData helix)
    {
        var priority = new[]
        {
            "Helix",
            "QAC",
            "QAC++",
            "QACPP",
            "RCMA",
            "NAMECHECK",
            "MTA",
            "DFA"
        };

        foreach (var code in priority)
        {
            if (helix.ModuleSupport.TryGetValue(code, out var info))
            {
                return info;
            }
        }

        return null;
    }

    private static List<OsType> GetSelectedOsTypes(string osSelection)
    {
        if (osSelection.Equals(ModuleRowViewModel.OsSelectionWindows, StringComparison.OrdinalIgnoreCase))
        {
            return new List<OsType> { OsType.Windows };
        }

        if (osSelection.Equals(ModuleRowViewModel.OsSelectionLinux, StringComparison.OrdinalIgnoreCase))
        {
            return new List<OsType> { OsType.Linux };
        }

        if (osSelection.Equals(ModuleRowViewModel.OsSelectionBoth, StringComparison.OrdinalIgnoreCase))
        {
            return new List<OsType> { OsType.Windows, OsType.Linux };
        }

        return new List<OsType>();
    }

    private static string GetDefaultOsSelection(IReadOnlyCollection<OsType> osTypes)
    {
        var hasWindows = osTypes.Contains(OsType.Windows);
        var hasLinux = osTypes.Contains(OsType.Linux);

        if (hasWindows && hasLinux)
        {
            return ModuleRowViewModel.OsSelectionBoth;
        }

        if (hasWindows)
        {
            return ModuleRowViewModel.OsSelectionWindows;
        }

        if (hasLinux)
        {
            return ModuleRowViewModel.OsSelectionLinux;
        }

        return ModuleRowViewModel.OsSelectionBoth;
    }

    private static bool IsInstallerVersionSelectable(string code)
    {
        return code.Equals("VALIDATE", StringComparison.OrdinalIgnoreCase) ||
               code.Equals("DASHBOARD", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRequestedVersion(ModuleRowViewModel module)
    {
        if (IsInstallerVersionSelectable(module.Code) &&
            !string.IsNullOrWhiteSpace(module.SelectedInstallerVersion))
        {
            return module.SelectedInstallerVersion;
        }

        return module.ModuleVersion;
    }

    private static string NormalizeHelixVersionLabel(string version)
    {
        return string.IsNullOrWhiteSpace(version) ? ScanOnlyVersionLabel : version;
    }

    private static string GetSafeVersionFolderName(string version)
    {
        var label = NormalizeHelixVersionLabel(version);
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(label.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? ScanOnlyVersionLabel : sanitized;
    }

    private static string GetEffectiveModuleCode(string code)
    {
        return BundledModuleMap.TryGetValue(code, out var bundledCode) ? bundledCode : code;
    }

    private static string GetMissingKey(string helixVersion, string code, string moduleVersion, OsType osType)
    {
        var effectiveCode = GetEffectiveModuleCode(code);
        return $"{helixVersion}|{effectiveCode}|{moduleVersion}|{osType}";
    }

    private List<string> GetAliasesForCode(string code)
    {
        var aliases = _synonyms
            .Where(pair => pair.Value.Any(c => c.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .ToList();

        if (code.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var bundled in HelixQacBundleCodes)
            {
                if (bundled.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!aliases.Any(alias => alias.Equals(bundled, StringComparison.OrdinalIgnoreCase)))
                {
                    aliases.Add(bundled);
                }
            }
        }

        return aliases;
    }

    private static void AddComplianceAliases(Dictionary<string, List<string>> synonyms)
    {
        foreach (var code in ComplianceModuleCodes)
        {
            var description = ModuleCatalog.GetDescription(code);
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var alias = TrimComplianceSuffix(description);
            AddSynonymAlias(synonyms, alias, code);
        }
    }

    private static string TrimComplianceSuffix(string description)
    {
        if (!description.EndsWith(ComplianceModuleSuffix, StringComparison.Ordinal))
        {
            return description;
        }

        return description[..^ComplianceModuleSuffix.Length].TrimEnd();
    }

    private static void AddSynonymAlias(Dictionary<string, List<string>> synonyms, string term, string code)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        if (!synonyms.TryGetValue(term, out var list))
        {
            list = new List<string>();
            synonyms[term] = list;
        }

        if (!list.Any(existing => existing.Equals(code, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(code);
        }
    }

    private List<string> GetKnownModuleCodes()
    {
        var codes = new List<string>();
        if (_compatibility != null)
        {
            codes.AddRange(_compatibility.ModuleCodes);
        }

        AddExtraModuleCodes(codes);

        foreach (var item in ScanSelectionItems)
        {
            if (!codes.Any(code => code.Equals(item.Code, StringComparison.OrdinalIgnoreCase)))
            {
                codes.Add(item.Code);
            }
        }

        return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private HashSet<string> GetAllowedModuleCodesForScan()
    {
        var codes = new List<string>();
        if (_compatibility != null)
        {
            codes.AddRange(_compatibility.ModuleCodes);
        }

        AddExtraModuleCodes(codes);
        codes = NormalizeModuleCodes(codes);
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    private void MergeScanModulesIntoHelixTabs()
    {
        if (_logicalItems.Count == 0)
        {
            return;
        }

        var scanCodes = _logicalItems
            .Select(item => item.Code)
            .Where(code => !code.Equals("Unclassified", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        scanCodes = NormalizeModuleCodes(scanCodes);
        var allowedCodes = GetAllowedModuleCodesForScan();
        scanCodes = scanCodes
            .Where(code => allowedCodes.Contains(code))
            .ToList();

        if (scanCodes.Count == 0)
        {
            return;
        }

        if (HelixVersions.Count == 0)
        {
            var modules = new List<ModuleRowViewModel>();
            foreach (var code in scanCodes)
            {
                modules.Add(CreateScanModuleRow(code, string.Empty));
            }

            var helixVm = new HelixVersionViewModel("共有スキャン", modules);
            helixVm.SelectionChanged += OnModuleSelectionChanged;
            helixVm.OsSelectionChanged += OnModuleOsSelectionChanged;
            helixVm.InstallerVersionChanged += OnModuleInstallerVersionChanged;
            helixVm.ApplyFilter(SearchText);
            HelixVersions.Add(helixVm);
            SelectedVersion ??= helixVm;
            return;
        }

        foreach (var helix in HelixVersions)
        {
            foreach (var code in scanCodes)
            {
                if (helix.Modules.Any(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                helix.AddModule(CreateScanModuleRow(code, helix.Version));
            }

            helix.ApplyFilter(SearchText);
        }
    }

    private ModuleRowViewModel CreateScanModuleRow(string code, string helixVersion)
    {
        var name = ModuleCatalog.GetDescription(code);
        var aliases = GetAliasesForCode(code);
        var isEnabled = true;
        string? reason = null;

        if (!string.IsNullOrWhiteSpace(helixVersion) &&
            !CompatibilityRules.TryCheckMinVersion(helixVersion, code, out reason))
        {
            isEnabled = false;
        }

        return new ModuleRowViewModel(
            code,
            name,
            string.Empty,
            true,
            isEnabled,
            reason,
            aliases,
            string.Empty,
            true);
    }

    private List<LogicalItem> GetLogicalItemsForModule(string moduleCode, out bool usedBundled)
    {
        var items = _logicalItems
            .Where(item => item.Code.Equals(moduleCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (items.Count > 0)
        {
            usedBundled = false;
            return items;
        }

        if (moduleCode.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase) &&
            BundledModuleMap.TryGetValue(moduleCode, out var helixBundledCode))
        {
            var bundledItems = _logicalItems
                .Where(item => item.Code.Equals(helixBundledCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bundledItems.Count > 0)
            {
                usedBundled = false;
                return bundledItems;
            }
        }

        if (BundledModuleMap.TryGetValue(moduleCode, out var bundledCode))
        {
            var bundledItems = _logicalItems
                .Where(item => item.Code.Equals(bundledCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bundledItems.Count > 0)
            {
                usedBundled = true;
                return bundledItems;
            }
        }

        usedBundled = false;
        return items;
    }

    private List<LogicalItem> GetLogicalItemsForModule(string moduleCode)
    {
        return GetLogicalItemsForModule(moduleCode, out _);
    }

    private List<ScanSelectionItemViewModel> GetScanItemsForModule(string moduleCode, out bool usedBundled)
    {
        var items = ScanSelectionItems
            .Where(item => item.Code.Equals(moduleCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (items.Count > 0)
        {
            usedBundled = false;
            return items;
        }

        if (moduleCode.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase) &&
            BundledModuleMap.TryGetValue(moduleCode, out var helixBundledCode))
        {
            var bundledItems = ScanSelectionItems
                .Where(item => item.Code.Equals(helixBundledCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bundledItems.Count > 0)
            {
                usedBundled = false;
                return bundledItems;
            }
        }

        if (BundledModuleMap.TryGetValue(moduleCode, out var bundledCode))
        {
            var bundledItems = ScanSelectionItems
                .Where(item => item.Code.Equals(bundledCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bundledItems.Count > 0)
            {
                usedBundled = true;
                return bundledItems;
            }
        }

        usedBundled = false;
        return items;
    }

    private List<ScanSelectionItemViewModel> GetScanItemsForModule(string moduleCode)
    {
        return GetScanItemsForModule(moduleCode, out _);
    }

    private List<ScanSelectionItemViewModel> GetScanItemsForSelectionCode(string selectionCode)
    {
        return ScanSelectionItems
            .Where(item => GetSelectionTargetCode(item.Code)
                .Equals(selectionCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string BuildOsDisplay(string moduleCode, string moduleVersion, out List<OsType> availableOs)
    {
        availableOs = new List<OsType>();
        var candidates = GetLogicalItemsForModule(moduleCode, out var usedBundled);
        if (candidates.Count == 0)
        {
            return "-";
        }

        var requestedVersion = usedBundled ? string.Empty : moduleVersion;
        var bestItems = SelectBestVersionItems(candidates, requestedVersion);
        if (bestItems.Count == 0)
        {
            return "-";
        }

        availableOs = bestItems.Select(item => item.Os).Distinct().ToList();
        return FormatOsDisplay(availableOs);
    }

    private static List<LogicalItem> SelectBestVersionItems(List<LogicalItem> items, string moduleVersion)
    {
        if (items.Count == 0)
        {
            return new List<LogicalItem>();
        }

        if (string.IsNullOrWhiteSpace(moduleVersion))
        {
            var best = SelectBestVersion(items);
            return best == null
                ? new List<LogicalItem>()
                : items.Where(item => item.Version.Equals(best.Version, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var exact = items
            .Where(item => item.Version.Equals(moduleVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count > 0)
        {
            return exact;
        }

        var bestMatch = SelectBestVersionMatch(items, moduleVersion);
        if (bestMatch != null)
        {
            return items.Where(item => item.Version.Equals(bestMatch.Version, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new List<LogicalItem>();
    }

    private static string FormatOsDisplay(IEnumerable<OsType> osTypes)
    {
        var list = osTypes.Distinct().ToList();
        if (list.Count == 0)
        {
            return "-";
        }

        var hasWindows = list.Contains(OsType.Windows);
        var hasLinux = list.Contains(OsType.Linux);

        if (hasWindows || hasLinux)
        {
            var parts = new List<string>();
            if (hasWindows)
            {
                parts.Add("Windows");
            }

            if (hasLinux)
            {
                parts.Add("Linux");
            }

            return string.Join("/", parts);
        }

        return list.Contains(OsType.Unknown) ? "Unknown" : "-";
    }

    private void UpdateInstallerVersionOptions(ModuleRowViewModel module)
    {
        if (!IsInstallerVersionSelectable(module.Code))
        {
            if (module.HasInstallerVersionOptions)
            {
                module.SetInstallerVersionOptions(Array.Empty<string>());
            }

            return;
        }

        var candidates = GetLogicalItemsForModule(module.Code, out _);
        var versions = candidates
            .Select(item => item.Version)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (versions.Count == 0)
        {
            module.SetInstallerVersionOptions(Array.Empty<string>());
            return;
        }

        versions.Sort((left, right) => VersionUtil.CompareVersionLike(right, left));
        module.SetInstallerVersionOptions(versions);
    }

    private static IEnumerable<string> GetModuleCodeCandidates(string code)
    {
        yield return code;
        if (ModuleCodeAliases.TryGetValue(code, out var aliases))
        {
            foreach (var alias in aliases)
            {
                yield return alias;
            }
        }
    }

    private void SyncBundleSelection(ModuleRowViewModel module, bool isSelected)
    {
        if (SelectedVersion == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(module.SelectionGroupKey))
        {
            return;
        }

        foreach (var other in SelectedVersion.Modules)
        {
            if (!other.SelectionGroupKey.Equals(module.SelectionGroupKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (isSelected && !other.IsEnabled)
            {
                continue;
            }

            if (other.IsSelected == isSelected)
            {
                continue;
            }

            other.SetSelectedSilently(isSelected);
        }
    }

    private void SyncBundleOsSelection(ModuleRowViewModel module)
    {
        if (SelectedVersion == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(module.SelectionGroupKey))
        {
            return;
        }

        foreach (var other in SelectedVersion.Modules)
        {
            if (!other.SelectionGroupKey.Equals(module.SelectionGroupKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (ReferenceEquals(other, module))
            {
                continue;
            }

            other.SetOsSelectionFromGroup(module.OsSelection);
        }
    }

    private void OnModuleSelectionChanged(object? sender, ModuleSelectionChangedEventArgs e)
    {
        SyncBundleSelection(e.Module, e.IsSelected);

        if (_suppressSelectionSync)
        {
            UpdateBasket();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            UpdateBasket();
            return;
        }
        if (e.IsSelected)
        {
            if (!SyncScanSelectionForModule(e.Module, out var reason))
            {
                e.Module.SetSelectedSilently(false);
                SyncBundleSelection(e.Module, false);
                WpfMessageBox.Show(
                    $"{e.Module.Code} は共有スキャンで未検出のため選択できません。{reason}",
                    "未検出",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            ClearScanSelectionForModule(e.Module);
        }

        UpdateBasket();
    }

    private void OnModuleOsSelectionChanged(object? sender, ModuleOsSelectionChangedEventArgs e)
    {
        SyncBundleOsSelection(e.Module);

        if (_suppressSelectionSync)
        {
            UpdateBasket();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            UpdateBasket();
            return;
        }

        if (e.Module.IsSelected)
        {
            SyncScanSelectionForModule(e.Module, out _);
        }

        UpdateBasket();
    }

    private void OnModuleInstallerVersionChanged(object? sender, ModuleInstallerVersionChangedEventArgs e)
    {
        var requestedVersion = GetRequestedVersion(e.Module);
        var asset = FindLogicalAsset(e.Module.Code, requestedVersion, out var reason);
        e.Module.ApplyAvailability(asset != null, reason);
        var osDisplay = BuildOsDisplay(e.Module.Code, requestedVersion, out var availableOs);
        e.Module.OsDisplay = osDisplay;
        e.Module.SetOsSelectionDefault(GetDefaultOsSelection(availableOs));

        if (_suppressSelectionSync)
        {
            UpdateBasket();
            return;
        }

        if (ScanSelectionItems.Count == 0)
        {
            UpdateBasket();
            return;
        }

        if (e.Module.IsSelected)
        {
            SyncScanSelectionForModule(e.Module, out _);
        }

        UpdateBasket();
    }

    private bool SyncScanSelectionForModule(ModuleRowViewModel module, out string reason)
    {
        reason = string.Empty;
        var requestedVersion = GetRequestedVersion(module);
        var selectedOsTypes = GetSelectedOsTypes(module.OsSelection);
        if (selectedOsTypes.Count == 0)
        {
            selectedOsTypes = new List<OsType> { OsType.Windows, OsType.Linux };
        }

        var matches = new List<ScanSelectionItemViewModel>();
        var reasonParts = new List<string>();
        foreach (var osType in selectedOsTypes)
        {
            var asset = FindLogicalAsset(module.Code, requestedVersion, osType, out var osReason);
            if (asset == null)
            {
                if (!string.IsNullOrWhiteSpace(osReason))
                {
                    reasonParts.Add(osReason);
                }

                continue;
            }

            var match = ScanSelectionItems.FirstOrDefault(item =>
                item.Code.Equals(asset.Code, StringComparison.OrdinalIgnoreCase) &&
                item.Version.Equals(asset.Version, StringComparison.OrdinalIgnoreCase) &&
                item.Os == asset.Os);

            match ??= ScanSelectionItems.FirstOrDefault(item =>
                item.SourcePath.Equals(asset.SourcePath, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                matches.Add(match);
            }
            else
            {
                reasonParts.Add("共有スキャン未検出");
            }
        }

        if (matches.Count == 0)
        {
            reason = reasonParts.Count > 0
                ? string.Join(" / ", reasonParts.Distinct())
                : "共有スキャン未検出";
            return false;
        }

        var matchSet = new HashSet<ScanSelectionItemViewModel>(matches);
        WithSelectionSyncSuppressed(() =>
        {
            var scanItems = GetScanItemsForSelectionCode(module.Code);
            foreach (var item in scanItems)
            {
                item.IsSelected = matchSet.Contains(item);
            }
        });

        return true;
    }

    private void ClearScanSelectionForModule(ModuleRowViewModel module)
    {
        var scanItems = GetScanItemsForSelectionCode(module.Code);
        if (scanItems.Count == 0)
        {
            return;
        }

        WithSelectionSyncSuppressed(() =>
        {
            foreach (var item in scanItems)
            {
                item.IsSelected = false;
            }
        });
    }

    private void UpdateScanResults(ScanResult result)
    {
        ScanLogicalItems.Clear();
        foreach (var item in result.Items
                     .OrderBy(i => i.Code)
                     .ThenBy(i => i.Version)
                     .ThenBy(i => i.Os))
        {
            ScanLogicalItems.Add(item);
        }

        ScanAssets.Clear();
        foreach (var asset in result.Items
                     .SelectMany(i => i.Assets)
                     .OrderBy(a => a.Code)
                     .ThenBy(a => a.Version)
                     .ThenBy(a => a.Os))
        {
            ScanAssets.Add(asset);
        }

        ScanSelectionItems.Clear();
        foreach (var item in result.Items
                     .OrderBy(i => i.Code)
                     .ThenBy(i => i.Version)
                     .ThenBy(i => i.Os))
        {
            var vm = new ScanSelectionItemViewModel(item);
            vm.SelectionChanged += OnScanSelectionChanged;
            ScanSelectionItems.Add(vm);
        }

        ScanErrors.Clear();
        foreach (var error in result.Errors)
        {
            ScanErrors.Add(error);
        }

        var unclassified = ScanLogicalItems.Count(i => i.Code.Equals("Unclassified", StringComparison.OrdinalIgnoreCase));
        ScanSummaryText = $"ルート: {Settings.UncRoot} | 論理: {ScanLogicalItems.Count} | 実体: {ScanAssets.Count} | 未分類: {unclassified} | エラー: {ScanErrors.Count}";
        MergeScanModulesIntoHelixTabs();
        UpdateModuleAvailabilityFromScan();
    }

    private void OnScanSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not ScanSelectionItemViewModel item)
        {
            UpdateBasket();
            return;
        }

        if (_suppressSelectionSync || SelectedVersion == null)
        {
            UpdateBasket();
            return;
        }

        var selectionCode = GetSelectionTargetCode(item.Code);
        var module = SelectedVersion.Modules.FirstOrDefault(m =>
            m.Code.Equals(selectionCode, StringComparison.OrdinalIgnoreCase));
        if (module == null || !module.IsEnabled)
        {
            UpdateBasket();
            return;
        }

        if (item.IsSelected)
        {
            WithSelectionSyncSuppressed(() => module.IsSelected = true);
        }
        else
        {
            var anySelected = ScanSelectionItems.Any(scan =>
                scan.IsSelected &&
                GetSelectionTargetCode(scan.Code).Equals(module.Code, StringComparison.OrdinalIgnoreCase));
            if (!anySelected)
            {
                WithSelectionSyncSuppressed(() => module.IsSelected = false);
            }
        }

        UpdateBasket();
    }

    private void UpdateModuleAvailabilityFromScan()
    {
        var hasScan = _logicalItems.Count > 0;
        foreach (var helix in HelixVersions)
        {
            foreach (var module in helix.Modules)
            {
                if (!hasScan)
                {
                    module.ApplyAvailability(null, null);
                    module.OsDisplay = "-";
                    module.SetInstallerVersionOptions(Array.Empty<string>());
                    continue;
                }

                UpdateInstallerVersionOptions(module);
                var requestedVersion = GetRequestedVersion(module);
                var asset = FindLogicalAsset(module.Code, requestedVersion, out var reason);
                module.ApplyAvailability(asset != null, reason);
                var osDisplay = BuildOsDisplay(module.Code, requestedVersion, out var availableOs);
                module.OsDisplay = osDisplay;
                module.SetOsSelectionDefault(GetDefaultOsSelection(availableOs));
            }
        }

        UpdateBasket();
    }

    private InstallerAsset? FindLogicalAsset(string code, string moduleVersion, out string reason)
    {
        return FindLogicalAsset(code, moduleVersion, null, out reason);
    }

    private InstallerAsset? FindLogicalAsset(string code, string moduleVersion, OsType? osType, out string reason)
    {
        reason = string.Empty;
        var candidates = GetLogicalItemsForModule(code, out var usedBundled);
        if (candidates.Count == 0)
        {
            reason = "未検出";
            return null;
        }

        if (osType != null)
        {
            candidates = candidates.Where(item => item.Os == osType.Value).ToList();
            if (candidates.Count == 0)
            {
                reason = osType == OsType.Windows
                    ? "Windows未検出"
                    : osType == OsType.Linux
                        ? "Linux未検出"
                        : "未検出";
                return null;
            }
        }

        var requestedVersion = usedBundled ? string.Empty : moduleVersion;
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            var osCandidates = osType == null ? FilterByOsPreference(candidates) : candidates;
            if (osCandidates.Count == 0)
            {
                reason = "未検出";
                return null;
            }

            return SelectBestVersion(osCandidates)?.PreferredAsset;
        }

        if (osType != null)
        {
            var exact = candidates.FirstOrDefault(c =>
                string.Equals(c.Version, requestedVersion, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact.PreferredAsset;
            }

            var bestMatch = SelectBestVersionMatch(candidates, requestedVersion);
            if (bestMatch != null)
            {
                return bestMatch.PreferredAsset;
            }

            reason = "版数一致なし";
            return null;
        }

        var windows = candidates.Where(item => item.Os == OsType.Windows).ToList();
        var linux = candidates.Where(item => item.Os == OsType.Linux).ToList();
        var osSets = new List<List<LogicalItem>>();
        if (windows.Count > 0)
        {
            osSets.Add(windows);
        }
        if (linux.Count > 0)
        {
            osSets.Add(linux);
        }
        if (osSets.Count == 0)
        {
            osSets.Add(candidates);
        }

        foreach (var set in osSets)
        {
            var exact = set.FirstOrDefault(c => string.Equals(c.Version, requestedVersion, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact.PreferredAsset;
            }
        }

        foreach (var set in osSets)
        {
            var bestMatch = SelectBestVersionMatch(set, requestedVersion);
            if (bestMatch != null)
            {
                return bestMatch.PreferredAsset;
            }
        }

        var anyMatch = SelectBestVersionMatch(candidates, requestedVersion);
        if (anyMatch != null)
        {
            return anyMatch.PreferredAsset;
        }

        reason = "版数一致なし";
        return null;
    }

    private InstallerAsset? FindAssetBySourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        return _logicalItems
            .SelectMany(item => item.Assets)
            .FirstOrDefault(asset => string.Equals(asset.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
    }

    private bool SelectModuleByCode(string code)
    {
        if (SelectedVersion == null)
        {
            return false;
        }

        var selectionCode = GetSelectionTargetCode(code);
        foreach (var candidate in GetModuleCodeCandidates(selectionCode))
        {
            var module = SelectedVersion.Modules.FirstOrDefault(m =>
                m.Code.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (module == null)
            {
                continue;
            }

            if (!module.IsEnabled)
            {
                return false;
            }

            module.IsSelected = true;
            return true;
        }

        return false;
    }

    private bool SelectByCode(string code, string? requestedVersion)
    {
        if (SelectedVersion != null)
        {
            return SelectModuleByCode(code);
        }

        return SelectScanItemsByCode(code, requestedVersion);
    }

    private bool SelectScanItemsByCode(string code, string? requestedVersion)
    {
        var selectionCode = GetSelectionTargetCode(code);
        var matches = GetScanItemsForModule(selectionCode, out var usedBundled)
            .AsEnumerable();

        if (!usedBundled && !string.IsNullOrWhiteSpace(requestedVersion))
        {
            matches = matches.Where(item => item.Version.Contains(requestedVersion, StringComparison.OrdinalIgnoreCase));
        }

        var any = false;
        foreach (var item in matches)
        {
            if (!item.IsEnabled)
            {
                continue;
            }

            item.IsSelected = true;
            any = true;
        }

        return any;
    }

    private void ClearManualPicks()
    {
        _manualPicks.Clear();
    }

    private void AddBasePickIfRequested(string text, string? requestedVersion)
    {
        var lower = text.ToLowerInvariant();
        var hasQacPlus = lower.Contains("qac++") || lower.Contains("qacpp");
        if (hasQacPlus)
        {
            if (ScanSelectionItems.Count > 0)
            {
                SelectByCode("QACPP", requestedVersion);
            }
            else
            {
                TryAddManualPick("QACPP", requestedVersion);
            }
        }
        else if (lower.Contains("本体") || lower.Contains("qac"))
        {
            if (ScanSelectionItems.Count > 0)
            {
                SelectByCode("QAC", requestedVersion);
            }
            else
            {
                TryAddManualPick("QAC", requestedVersion);
            }
        }
    }

    private void TryAddManualPick(string code, string? requestedVersion)
    {
        var selectionCode = GetSelectionTargetCode(code);
        var helixLabel = NormalizeHelixVersionLabel(SelectedVersion?.Version ?? string.Empty);
        if (_logicalItems.Count == 0)
        {
            _manualPicks.Add(new ManualPickEntry(helixLabel, selectionCode, requestedVersion ?? string.Empty, null, "スキャン未実施"));
            return;
        }

        var asset = FindLogicalAsset(selectionCode, requestedVersion ?? string.Empty, out var reason);
        if (asset != null)
        {
            _manualPicks.Add(new ManualPickEntry(helixLabel, selectionCode, requestedVersion ?? string.Empty, asset, "要望文から選択"));
            return;
        }

        _manualPicks.Add(new ManualPickEntry(helixLabel, selectionCode, requestedVersion ?? string.Empty, null, reason));
    }

    private static List<VersionedRequest> ParseVersionedRequests(string text, IReadOnlyCollection<string> knownCodes)
    {
        var requests = new List<VersionedRequest>();
        if (string.IsNullOrWhiteSpace(text) || knownCodes.Count == 0)
        {
            return requests;
        }

        var codes = knownCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .OrderByDescending(code => code.Length)
            .ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var requestIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var normalizedLine = trimmed.Normalize(NormalizationForm.FormKC);
            var requestedOs = GetRequestedOsFromText(normalizedLine);
            foreach (var code in codes)
            {
                var index = normalizedLine.IndexOf(code, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                var match = VersionRegex.Match(normalizedLine, index + code.Length);
                if (!match.Success)
                {
                    continue;
                }

                var version = match.Value;
                var key = $"{code}|{version}";
                if (!seen.Add(key))
                {
                    if (requestIndex.TryGetValue(key, out var existingIndex))
                    {
                        var existing = requests[existingIndex];
                        var merged = MergeRequestedOs(existing.OsSelection, requestedOs);
                        requests[existingIndex] = existing with { OsSelection = merged };
                    }
                    break;
                }

                requestIndex[key] = requests.Count;
                requests.Add(new VersionedRequest(code, version, requestedOs));

                break;
            }
        }

        return requests;
    }

    private bool SelectAcrossTabsByVersion(
        string code,
        string requestedVersion,
        RequestedOs requestedOs,
        Dictionary<ModuleRowViewModel, RequestedOs> osRequests)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(requestedVersion))
        {
            return false;
        }

        if (_compatibility == null || HelixVersions.Count == 0)
        {
            var selected = SelectByCode(code, requestedVersion);
            if (selected && SelectedVersion != null)
            {
                var requestedSelectionCode = GetSelectionTargetCode(code);
                foreach (var module in SelectedVersion.Modules.Where(m =>
                             m.IsSelected &&
                             m.Code.Equals(requestedSelectionCode, StringComparison.OrdinalIgnoreCase)))
                {
                    RecordRequestedOs(osRequests, module, requestedOs);
                }
            }

            return selected;
        }

        var selectionCode = GetSelectionTargetCode(code);
        var matched = false;

        foreach (var helix in HelixVersions)
        {
            var helixData = _compatibility.Versions.FirstOrDefault(v =>
                v.Version.Equals(helix.Version, StringComparison.OrdinalIgnoreCase));
            if (helixData == null)
            {
                continue;
            }

            var moduleVersion = GetCompatibilityModuleVersion(helixData, code);
            if (string.IsNullOrWhiteSpace(moduleVersion))
            {
                continue;
            }

            if (!IsVersionMatch(requestedVersion, moduleVersion))
            {
                continue;
            }

            var module = helix.Modules.FirstOrDefault(m =>
                m.Code.Equals(selectionCode, StringComparison.OrdinalIgnoreCase));
            if (module == null || !module.IsEnabled)
            {
                continue;
            }

            module.IsSelected = true;
            RecordRequestedOs(osRequests, module, requestedOs);
            if (module.IsSelected)
            {
                matched = true;
            }
        }

        return matched;
    }

    private static string? GetCompatibilityModuleVersion(HelixVersionData helix, string code)
    {
        if (code.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase) ||
            code.Equals("Helix", StringComparison.OrdinalIgnoreCase))
        {
            return helix.Version;
        }

        foreach (var candidate in GetModuleCodeCandidates(code))
        {
            if (helix.ModuleSupport.TryGetValue(candidate, out var info) &&
                !string.IsNullOrWhiteSpace(info.ModuleVersion))
            {
                return info.ModuleVersion;
            }
        }

        return null;
    }

    private static RequestedOs GetRequestedOsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RequestedOs.Unspecified;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var hasWindows = normalized.Contains(OsTokenWindows) || normalized.Contains("win");
        var hasLinux = normalized.Contains(OsTokenLinux);

        if (hasWindows && hasLinux)
        {
            return RequestedOs.Both;
        }

        if (hasWindows)
        {
            return RequestedOs.Windows;
        }

        if (hasLinux)
        {
            return RequestedOs.Linux;
        }

        return RequestedOs.Unspecified;
    }

    private static RequestedOs MergeRequestedOs(RequestedOs existing, RequestedOs incoming)
    {
        if (existing == RequestedOs.Unspecified)
        {
            return incoming;
        }

        if (incoming == RequestedOs.Unspecified)
        {
            return existing;
        }

        if (existing == RequestedOs.Both || incoming == RequestedOs.Both)
        {
            return RequestedOs.Both;
        }

        return existing == incoming ? existing : RequestedOs.Both;
    }

    private static void RecordRequestedOs(
        Dictionary<ModuleRowViewModel, RequestedOs> osRequests,
        ModuleRowViewModel module,
        RequestedOs requestedOs)
    {
        if (!osRequests.TryGetValue(module, out var existing))
        {
            osRequests[module] = requestedOs;
            return;
        }

        osRequests[module] = MergeRequestedOs(existing, requestedOs);
    }

    private void ApplyQuickRequestOsSelection(Dictionary<ModuleRowViewModel, RequestedOs> osRequests)
    {
        if (HelixVersions.Count == 0)
        {
            return;
        }

        foreach (var module in HelixVersions.SelectMany(h => h.Modules).Where(m => m.IsSelected))
        {
            var requested = osRequests.TryGetValue(module, out var value)
                ? value
                : RequestedOs.Unspecified;
            module.OsSelection = ResolveRequestedOsSelection(module, requested);
        }
    }

    private static string ResolveRequestedOsSelection(ModuleRowViewModel module, RequestedOs requestedOs)
    {
        return requestedOs switch
        {
            RequestedOs.Windows => ModuleRowViewModel.OsSelectionWindows,
            RequestedOs.Linux => ModuleRowViewModel.OsSelectionLinux,
            RequestedOs.Both => ModuleRowViewModel.OsSelectionBoth,
            _ => GetDefaultOsSelection(GetAvailableOsTypesFromDisplay(module.OsDisplay))
        };
    }

    private static List<OsType> GetAvailableOsTypesFromDisplay(string osDisplay)
    {
        var types = new List<OsType>();
        if (string.IsNullOrWhiteSpace(osDisplay) || osDisplay == "-")
        {
            return types;
        }

        if (osDisplay.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            types.Add(OsType.Windows);
        }

        if (osDisplay.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            types.Add(OsType.Linux);
        }

        return types;
    }

    private HelixVersionViewModel? FindHelixVersionFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        foreach (Match match in VersionRegex.Matches(normalized))
        {
            var token = match.Value;
            var helix = FindHelixVersion(token);
            if (helix != null)
            {
                return helix;
            }
        }

        return null;
    }

    private static string? FindRequestedHelixVersionToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        foreach (Match match in VersionRegex.Matches(normalized))
        {
            var token = match.Value;
            if (IsLikelyHelixVersionToken(token))
            {
                return token;
            }
        }

        return null;
    }

    private HelixVersionViewModel? FindHelixVersion(string token)
    {
        var exact = HelixVersions.FirstOrDefault(v => string.Equals(v.Version, token, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        return HelixVersions.FirstOrDefault(v => v.Version.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetBasketKey(string helixVersion, InstallerAsset asset)
    {
        return $"{helixVersion}|{asset.Code}|{asset.Version}|{asset.Os}";
    }

    private static List<int> ExtractVersionNumbers(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return new List<int>();
        }

        var numbers = new List<int>();
        foreach (Match match in VersionNumberRegex.Matches(version))
        {
            if (int.TryParse(match.Value, out var value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    private static bool IsLikelyHelixVersionToken(string token)
    {
        var numbers = ExtractVersionNumbers(token);
        if (numbers.Count < 2)
        {
            return false;
        }

        return numbers[0] >= 2000;
    }

    private static int GetRequiredVersionMatchCount(string version)
    {
        var numbers = ExtractVersionNumbers(version);
        if (numbers.Count == 0)
        {
            return 0;
        }

        return numbers.Count == 1 ? 1 : 2;
    }

    private static int GetVersionMatchScore(string requestedVersion, string candidateVersion)
    {
        var requestedNumbers = ExtractVersionNumbers(requestedVersion);
        var candidateNumbers = ExtractVersionNumbers(candidateVersion);
        if (requestedNumbers.Count == 0 || candidateNumbers.Count == 0)
        {
            return 0;
        }

        var limit = Math.Min(requestedNumbers.Count, candidateNumbers.Count);
        var score = 0;
        for (var i = 0; i < limit; i++)
        {
            if (requestedNumbers[i] != candidateNumbers[i])
            {
                break;
            }

            score++;
        }

        return score;
    }

    private static bool IsVersionMatch(string requestedVersion, string candidateVersion)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion) || string.IsNullOrWhiteSpace(candidateVersion))
        {
            return false;
        }

        var required = GetRequiredVersionMatchCount(requestedVersion);
        if (required == 0)
        {
            return false;
        }

        return GetVersionMatchScore(requestedVersion, candidateVersion) >= required;
    }

    private static List<LogicalItem> FilterByOsPreference(IEnumerable<LogicalItem> items)
    {
        var windows = items.Where(item => item.Os == OsType.Windows).ToList();
        if (windows.Count > 0)
        {
            return windows;
        }

        var linux = items.Where(item => item.Os == OsType.Linux).ToList();
        if (linux.Count > 0)
        {
            return linux;
        }

        return items.ToList();
    }

    private static LogicalItem? SelectBestVersion(IEnumerable<LogicalItem> items)
    {
        LogicalItem? best = null;
        foreach (var item in items)
        {
            if (best == null || VersionUtil.CompareVersionLike(item.Version, best.Version) > 0)
            {
                best = item;
            }
        }

        return best;
    }

    private static LogicalItem? SelectBestVersionMatch(IEnumerable<LogicalItem> items, string requestedVersion)
    {
        var required = GetRequiredVersionMatchCount(requestedVersion);
        LogicalItem? best = null;
        var bestScore = 0;
        foreach (var item in items)
        {
            var score = GetVersionMatchScore(requestedVersion, item.Version);
            if (score < required)
            {
                continue;
            }

            if (best == null ||
                score > bestScore ||
                (score == bestScore && VersionUtil.CompareVersionLike(item.Version, best.Version) > 0))
            {
                best = item;
                bestScore = score;
            }
        }

        return best;
    }

    private ScanSelectionItemViewModel? FindBestScanItemByCode(string code, string moduleVersion)
    {
        var candidates = GetScanItemsForModule(code, out var usedBundled);

        if (candidates.Count == 0)
        {
            return null;
        }

        var requestedVersion = usedBundled ? string.Empty : moduleVersion;
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return candidates.FirstOrDefault();
        }

        var required = GetRequiredVersionMatchCount(requestedVersion);
        ScanSelectionItemViewModel? best = null;
        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            var score = GetVersionMatchScore(requestedVersion, candidate.Version);
            if (score < required)
            {
                continue;
            }

            if (best == null ||
                score > bestScore ||
                (score == bestScore && VersionUtil.CompareVersionLike(candidate.Version, best.Version) > 0))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private void WithSelectionSyncSuppressed(Action action)
    {
        var previous = _suppressSelectionSync;
        _suppressSelectionSync = true;
        try
        {
            action();
        }
        finally
        {
            _suppressSelectionSync = previous;
        }
    }

    private enum RequestedOs
    {
        Unspecified,
        Windows,
        Linux,
        Both
    }

    private sealed record VersionedRequest(string Code, string Version, RequestedOs OsSelection);

    private sealed record ManualPickEntry(
        string HelixVersion,
        string Code,
        string RequestedVersion,
        InstallerAsset? Asset,
        string Reason);

    private async Task LoadPendingTransfersAsync()
    {
        var pending = await _databaseService.LoadPendingTransfersAsync();
        foreach (var record in pending)
        {
            record.Status = TransferStatus.Paused;
            var vm = new TransferItemViewModel(record, TransferManager);
            vm.MarkPaused();
            vm.ProgressChanged += (_, _) => TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
            TransferItems.Add(vm);
            await _databaseService.UpdateTransferItemAsync(record);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        foreach (var helix in HelixVersions)
        {
            helix.ApplyFilter(value);
        }
    }

    partial void OnSelectedVersionChanged(HelixVersionViewModel? value)
    {
        UpdateBasket();
    }

    partial void OnCompanyNameChanged(string value)
    {
        OnPropertyChanged(nameof(OutputFolderPreview));
    }

    partial void OnSettingsChanged(SettingsModel value)
    {
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(OutputFolderPreview));
        MaxConcurrentTransfers = value.MaxConcurrentTransfers;
        MaxConcurrentTransfersInput = value.MaxConcurrentTransfers.ToString();
    }

    partial void OnMaxConcurrentTransfersChanged(int value)
    {
        if (value < 1)
        {
            MaxConcurrentTransfers = 1;
            return;
        }

        Settings.MaxConcurrentTransfers = value;
        _transferManager?.UpdateMaxConcurrent(value);
        TransferSummary.Update(TransferItems, value);
        if (!string.Equals(MaxConcurrentTransfersInput, value.ToString(), StringComparison.Ordinal))
        {
            MaxConcurrentTransfersInput = value.ToString();
        }
    }

    partial void OnUploadListTextChanged(string value)
    {
        if (_suppressUploadListEdit)
        {
            return;
        }

        _uploadListUserEdited = true;
    }

    private void UpdateUploadListText()
    {
        if (_uploadListUserEdited && !string.IsNullOrWhiteSpace(UploadListText))
        {
            return;
        }

        var text = BuildUploadListText();
        _suppressUploadListEdit = true;
        UploadListText = text;
        _suppressUploadListEdit = false;
        _uploadListUserEdited = false;
    }

    private string BuildUploadListText()
    {
        var lines = new List<string> { "■アップロードしたファイル" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in BasketItems.Where(b => !b.IsMissing))
        {
            var key = string.IsNullOrWhiteSpace(item.SourcePath) ? item.AssetFileName : item.SourcePath;
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            lines.Add($"・{item.AssetFileName}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}


