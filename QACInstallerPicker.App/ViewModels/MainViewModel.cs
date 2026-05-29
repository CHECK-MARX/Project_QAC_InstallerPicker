using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
using Forms = System.Windows.Forms;
using Win32 = Microsoft.Win32;
using WpfMessageBox = System.Windows.MessageBox;

namespace QACInstallerPicker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ExcelService _excelService;
    private readonly BulkSelectionExcelService _bulkSelectionExcelService;
    private readonly InstallerScanService _scanService;
    private readonly MemoParserService _memoService;
    private readonly LocalLlmDecisionService _localLlmDecisionService;
    private readonly DatabaseService _databaseService;
    private readonly HashService _hashService;
    private readonly CopyService _copyService;
    private readonly ShipmentHistoryExcelService _shipmentHistoryExcelService;
    private TransferManager? _transferManager;

    private CompatibilityData? _compatibility;
    private Dictionary<string, List<string>> _synonyms = new(StringComparer.OrdinalIgnoreCase);
    private List<LogicalItem> _logicalItems = new();
    private readonly List<CustomZipPlan> _customZipPlans = new();
    private readonly List<ManualPickEntry> _manualPicks = new();
    private readonly List<ShipmentHistoryRecord> _shipmentHistoryAllRecords = new();
    private readonly Dictionary<long, TransferStatus> _transferStatusLookup = new();
    private readonly HashSet<string> _redownloadUnlockedDestinationPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSelectionSync;
    private bool _isRestoringCustomState;
    private bool _isApplyingSelectionHistory;
    private bool _isApplyingShipmentHistoryViewFilters;
    private bool _isSettingCompanyNameFromMemo;
    private bool _isCompanyNameAutoFilled;
    private static readonly Regex VersionRegex = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);
    private static readonly Regex VersionNumberRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex WindowsTokenRegex = new(
        @"(?<![A-Za-z0-9])(windows?|win(?:32|64)?)(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LinuxTokenRegex = new(
        @"(?<![A-Za-z0-9])(linux|lin)(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QuotedMessageStartRegex = new(
        @"^\s*(From|Sent|To|Cc|件名|送信|宛先)\s*[:：]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReplyHeaderStartRegex = new(
        @"^\s*(?:On\s.+\swrote:|[- ]*Original Message[- ]*|[- ]*返信メッセージ[- ]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QuotedLineRegex = new(
        @"^\s*>+",
        RegexOptions.Compiled);
    private static readonly Regex CompanyLabelRegex = new(
        @"(?:会社名|社名|企業名|法人名)\s*[:：]\s*(?<name>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CompanyHonorificRegex = new(
        @"(?<name>[^\r\n]{2,80}?)(?:御中|様)",
        RegexOptions.Compiled);
    private static readonly Regex CompanyKeywordRegex = new(
        @"(?<name>(?:株式会社|有限会社|合同会社)[^\r\n]{1,80}|[^\r\n]{1,80}(?:株式会社|有限会社|合同会社))",
        RegexOptions.Compiled);
    private static readonly Regex SignatureSeparatorRegex = new(
        @"^(?:--+|__+|==+|[-_=\u2500\u2014\u2015]{2,}|[─━]{2,})$",
        RegexOptions.Compiled);
    private static readonly Regex CompanyInlineRegex = new(
        @"(?<name>(?:株式会社|有限会社|合同会社)\s*[^\r\n,，、。.!！?？:：\(\)（）\[\]【】<>＜＞「」『』]{1,80}|[^\r\n,，、。.!！?？:：\(\)（）\[\]【】<>＜＞「」『』]{1,80}(?:株式会社|有限会社|合同会社))",
        RegexOptions.Compiled);
    private static readonly Regex AddresseeLineRegex = new(
        @"(?:様|御中)$",
        RegexOptions.Compiled);
    private static readonly Regex SelfIntroPhraseRegex = new(
        @"(?:です|でございます|と申します|となります)",
        RegexOptions.Compiled);
    private static readonly Regex SelfIntroCompanyRegex = new(
        @"(?<name>[^\s　、。,.]{2,60})の[^\s　、。,.]{1,24}(?:です|でございます|と申します|となります)",
        RegexOptions.Compiled);
    private static readonly Regex OsBothIntentRegex = new(
        @"(?:両方|双方|いずれも|どちらも|それぞれ|各OS|全OS|both)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OsQuestionRegex = new(
        @"(?:どちら|でしょうか|ですか|ますか|\?|？|確認)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OsRequestRegex = new(
        @"(?:お願いいたします|お願いします|希望|指定|利用|で\b|版で)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IntroSuffixRegex = new(
        @"の[^の]{1,24}(?:です|でございます|と申します|となります).*$",
        RegexOptions.Compiled);
    private static readonly Regex MemoMappingRegex = new(
        @"^(?<term>.+?)\s*(?:=>|->|→|=|＝)\s*(?<code>[A-Za-z0-9+]+)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex CompanyOverrideRegex = new(
        @"^(?:会社名|社名|企業名|法人名)\s*(?:[:：]|=>|->|→|=|＝)\s*(?<name>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string ScanOnlyVersionLabel = "共有スキャン";
    private const string HelixQacCode = "HelixQAC";
    private const string CustomTabLabelPrefix = "カスタム:";
    private const string CustomZipSummaryCode = "CUSTOMZIP";
    private static readonly HashSet<string> IgnoredAuxiliaryFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        "desktop.ini"
    };
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
            ["QAC++"] = new[] { "QACPP" },
            ["VALIDATE"] = new[] { "VALDATE" },
            ["VALDATE"] = new[] { "VALIDATE" }
        };
    private static readonly IReadOnlyDictionary<string, string> CanonicalModuleCodeMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VALDATE"] = "VALIDATE",
            ["DASHBOAD"] = "DASHBOARD"
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
    private const int ShipmentInputTabIndex = 4;
    private const int ShipmentHistoryViewTabIndex = 5;
    private const string ComplianceModuleSuffix = "コンプライアンスモジュール";
    private const int SelectionHistoryLimit = 5;
    private const int MemoUnresolvedHistoryLimit = 200;
    private const string AiModeLocalLlm = "LocalLlm";
    private const string AlreadyDownloadedReason = "既にダウンロード済み（ダブルクリックで再DLに含める）";
    private const string ShipmentHistoryFilterAllOption = "（すべて）";
    private const string ShipmentHistoryFilterNoneOption = "（なし）";
    private static readonly IReadOnlyList<string> ShipmentCategoryOptionsList =
    [
        "評価",
        "既存顧客",
        "商談",
        "保守",
        "再送",
        "その他"
    ];
    private static readonly IReadOnlyList<string> ShipmentHistoryPeriodOptionsList =
    [
        "全期間",
        "1週間",
        "1か月",
        "1年"
    ];

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _excelService = new ExcelService();
        _bulkSelectionExcelService = new BulkSelectionExcelService();
        _scanService = new InstallerScanService();
        _memoService = new MemoParserService();
        _localLlmDecisionService = new LocalLlmDecisionService();
        _databaseService = new DatabaseService();
        _hashService = new HashService(_databaseService);
        _copyService = new CopyService();
        _shipmentHistoryExcelService = new ShipmentHistoryExcelService();

        _settings = _settingsService.Load();
        _maxConcurrentTransfers = _settings.MaxConcurrentTransfers;
        _maxConcurrentTransfersInput = _maxConcurrentTransfers.ToString();

        HelixVersions = new ObservableCollection<HelixVersionViewModel>();
        BasketItems = new ObservableCollection<BasketItemViewModel>();
        TransferItems = new ObservableCollection<TransferItemViewModel>();
        HistoryItems = new ObservableCollection<HistoryItemViewModel>();
        UnresolvedTerms = new ObservableCollection<string>();
        MemoUnresolvedHistoryItems = new ObservableCollection<string>();
        AmbiguousTerms = new ObservableCollection<AmbiguousMatchViewModel>();
        TransferSummary = new TransferSummaryViewModel();
        ScanLogicalItems = new ObservableCollection<LogicalItem>();
        ScanAssets = new ObservableCollection<InstallerAsset>();
        ScanErrors = new ObservableCollection<string>();
        ScanSelectionItems = new ObservableCollection<ScanSelectionItemViewModel>();
        CustomTabs = new ObservableCollection<CustomTabViewModel>();
        SelectionStateHistoryEntries = new ObservableCollection<SelectionStateHistoryEntry>();
        ShipmentHistoryItems = new ObservableCollection<BasketItemViewModel>();
        ShipmentHistoryViewItems = new ObservableCollection<ShipmentHistoryRecord>();
        ShipmentHistoryCompanyMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryPersonMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryCategoryMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryHelixMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryCodeMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryNameMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryCompatibilityVersionMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistorySelectedOsMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryInstallerMultiFilterOptions = new ObservableCollection<FilterCheckItemViewModel>();
        ShipmentHistoryCompanyFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryPersonFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryCategoryFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryHelixFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryCodeFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryNameFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryCompatibilityVersionFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistorySelectedOsFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };
        ShipmentHistoryInstallerFilterOptions = new ObservableCollection<string> { ShipmentHistoryFilterAllOption };

        RestoreCustomStateFromSettings();
        LoadSelectionStateHistoryFromSettings();
    }

    public ObservableCollection<HelixVersionViewModel> HelixVersions { get; }
    public ObservableCollection<BasketItemViewModel> BasketItems { get; }
    public ObservableCollection<TransferItemViewModel> TransferItems { get; }
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; }
    public ObservableCollection<string> UnresolvedTerms { get; }
    public ObservableCollection<string> MemoUnresolvedHistoryItems { get; }
    public ObservableCollection<AmbiguousMatchViewModel> AmbiguousTerms { get; }
    public TransferSummaryViewModel TransferSummary { get; }
    public ObservableCollection<LogicalItem> ScanLogicalItems { get; }
    public ObservableCollection<InstallerAsset> ScanAssets { get; }
    public ObservableCollection<string> ScanErrors { get; }
    public ObservableCollection<ScanSelectionItemViewModel> ScanSelectionItems { get; }
    public ObservableCollection<CustomTabViewModel> CustomTabs { get; }
    public ObservableCollection<SelectionStateHistoryEntry> SelectionStateHistoryEntries { get; }
    public ObservableCollection<BasketItemViewModel> ShipmentHistoryItems { get; }
    public ObservableCollection<ShipmentHistoryRecord> ShipmentHistoryViewItems { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryCompanyMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryPersonMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryCategoryMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryHelixMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryCodeMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryNameMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryCompatibilityVersionMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistorySelectedOsMultiFilterOptions { get; }
    public ObservableCollection<FilterCheckItemViewModel> ShipmentHistoryInstallerMultiFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryCompanyFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryPersonFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryCategoryFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryHelixFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryCodeFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryNameFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryCompatibilityVersionFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistorySelectedOsFilterOptions { get; }
    public ObservableCollection<string> ShipmentHistoryInstallerFilterOptions { get; }
    public IReadOnlyList<string> ShipmentCategoryOptions => ShipmentCategoryOptionsList;
    public IReadOnlyList<string> ShipmentHistoryPeriodOptions => ShipmentHistoryPeriodOptionsList;
    public string ShipmentHistoryCompanyFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryCompanyMultiFilterOptions);
    public string ShipmentHistoryPersonFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryPersonMultiFilterOptions);
    public string ShipmentHistoryCategoryFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryCategoryMultiFilterOptions);
    public string ShipmentHistoryHelixFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryHelixMultiFilterOptions);
    public string ShipmentHistoryCodeFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryCodeMultiFilterOptions);
    public string ShipmentHistoryNameFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryNameMultiFilterOptions);
    public string ShipmentHistoryCompatibilityVersionFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryCompatibilityVersionMultiFilterOptions);
    public string ShipmentHistorySelectedOsFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistorySelectedOsMultiFilterOptions);
    public string ShipmentHistoryInstallerFilterDisplay => BuildShipmentHistoryMultiFilterDisplay(ShipmentHistoryInstallerMultiFilterOptions);

    public void SetCustomZipPlans(IEnumerable<CustomZipPlan> plans)
    {
        _customZipPlans.Clear();
        var normalized = new Dictionary<string, List<CustomZipPlanItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(plan.TabName) || string.IsNullOrWhiteSpace(plan.ArchiveBaseName))
            {
                continue;
            }

            var items = plan.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath) && !string.IsNullOrWhiteSpace(item.FileName))
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var key = BuildCustomZipPlanStorageKey(plan.TabName, plan.ArchiveBaseName);
            if (!normalized.TryGetValue(key, out var list))
            {
                list = new List<CustomZipPlanItem>();
                normalized[key] = list;
            }

            list.AddRange(items);
        }

        foreach (var pair in normalized)
        {
            if (!TryParseCustomZipPlanStorageKey(pair.Key, out var tabName, out var archiveBaseName))
            {
                continue;
            }

            var mergedItems = pair.Value
                .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath) && !string.IsNullOrWhiteSpace(item.FileName))
                .GroupBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (mergedItems.Count == 0)
            {
                continue;
            }

            _customZipPlans.Add(new CustomZipPlan(tabName, archiveBaseName, mergedItems));
        }

        PersistCustomState();
    }

    public IReadOnlyList<CustomZipPlan> GetCustomZipPlans()
    {
        return _customZipPlans
            .Select(plan => plan with { Items = plan.Items.ToList() })
            .ToList();
    }

    public void RefreshBasketForCustomZipPlans()
    {
        UpdateBasket();
    }

    public event EventHandler? RequestOpenSettings;
    public event Action<string, string>? RequestNotification;

    [ObservableProperty]
    private SettingsModel _settings;

    [ObservableProperty]
    private HelixVersionViewModel? _selectedVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _memoText = string.Empty;

    [ObservableProperty]
    private string _unresolvedMemoText = string.Empty;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private int _selectedMainTabIndex;

    [ObservableProperty]
    private string _scanSummaryText = "スキャン未実施";

    [ObservableProperty]
    private string _quickRequestResult = string.Empty;

    [ObservableProperty]
    private string _quickRequestDecisionLog = string.Empty;

    [ObservableProperty]
    private string _uploadListText = string.Empty;

    [ObservableProperty]
    private int _maxConcurrentTransfers;

    [ObservableProperty]
    private string _maxConcurrentTransfersInput = string.Empty;

    [ObservableProperty]
    private CustomTabViewModel? _selectedCustomTab;

    [ObservableProperty]
    private string _newCustomTabName = "カスタム";

    [ObservableProperty]
    private string _newCustomTabColumns = string.Empty;

    [ObservableProperty]
    private SelectionStateHistoryEntry? _selectedSelectionStateHistoryEntry;

    [ObservableProperty]
    private BasketItemViewModel? _selectedShipmentHistoryItem;

    [ObservableProperty]
    private DateTime _shipmentDate = DateTime.Today;

    [ObservableProperty]
    private string _shipmentCompanyName = string.Empty;

    [ObservableProperty]
    private string _shipmentHelixVersion = string.Empty;

    [ObservableProperty]
    private string _shipmentCode = string.Empty;

    [ObservableProperty]
    private string _shipmentName = string.Empty;

    [ObservableProperty]
    private string _shipmentCompatibilityVersion = string.Empty;

    [ObservableProperty]
    private string _shipmentSelectedOs = string.Empty;

    [ObservableProperty]
    private string _shipmentInstallerName = string.Empty;

    [ObservableProperty]
    private string _shipmentPersonName = string.Empty;

    [ObservableProperty]
    private string _shipmentCategory = string.Empty;

    [ObservableProperty]
    private string _shipmentHistoryViewSummary = "未読込";

    [ObservableProperty]
    private string _shipmentHistoryPeriod = "全期間";

    [ObservableProperty]
    private DateTime? _shipmentHistoryFromDate;

    [ObservableProperty]
    private DateTime? _shipmentHistoryToDate;

    [ObservableProperty]
    private string _shipmentHistoryCompanyFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryPersonFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryCategoryFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryHelixFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryCodeFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryNameFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryCompatibilityVersionFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistorySelectedOsFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private string _shipmentHistoryInstallerFilter = ShipmentHistoryFilterAllOption;

    [ObservableProperty]
    private bool _isShipmentHistoryCompanyFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryPersonFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryCategoryFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryHelixFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryCodeFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryNameFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryCompatibilityVersionFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistorySelectedOsFilterPopupOpen;

    [ObservableProperty]
    private bool _isShipmentHistoryInstallerFilterPopupOpen;

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
                _redownloadUnlockedDestinationPaths.Clear();
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputFolderPreview));
                if (!_isApplyingSelectionHistory && !_isRestoringCustomState)
                {
                    UpdateBasket();
                }
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

    public string ShipmentHistoryExcelPath => Settings.ShipmentHistoryExcelPath;
    public bool IsShipmentHistoryExcelConfigured => !string.IsNullOrWhiteSpace(Settings.ShipmentHistoryExcelPath);

    public string AppTitle => $"QAC インストーラ選定ツール v{AppVersion}";

    private static string AppVersion => GetAppVersion();

    private static string GetAppVersion()
    {
        var assembly = typeof(MainViewModel).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    public async Task InitializeAsync()
    {
        await _databaseService.InitializeAsync();
        _synonyms = _memoService.LoadSynonyms(AppPaths.SynonymsPath);
        RestoreMemoLearnedSynonymsFromSettings();
        AddComplianceAliases(_synonyms);
        LoadMemoUnresolvedHistoryFromSettings();

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

        await LoadTransferItemsAsync();
        await RefreshHistoryAsync();
        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
    }

    public void ReloadSettings()
    {
        Settings = _settingsService.Load();
        if (_synonyms.Count > 0)
        {
            RestoreMemoLearnedSynonymsFromSettings();
        }
        LoadMemoUnresolvedHistoryFromSettings();
        RestoreCustomStateFromSettings();
        LoadSelectionStateHistoryFromSettings();
        if (TransferItems.Count == 0)
        {
            _transferManager = null;
        }

        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(OutputFolderPreview));
        OnPropertyChanged(nameof(ShipmentHistoryExcelPath));
        OnPropertyChanged(nameof(IsShipmentHistoryExcelConfigured));
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
    private void ExportBulkConfigExcel()
    {
        try
        {
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx",
                FileName = $"QAC一括設定_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var model = BuildBulkSelectionWorkbookModel();
            var options = Settings.BulkExcelTemplateOptions ?? new BulkExcelTemplateOptions();
            _bulkSelectionExcelService.ExportTemplate(dialog.FileName, model, options);
            WpfMessageBox.Show("設定用Excelを出力しました。", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"設定Excel出力に失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ImportBulkConfigExcel()
    {
        try
        {
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var imported = _bulkSelectionExcelService.ImportTemplate(dialog.FileName);
            ApplyImportedBulkSelection(imported);
            WpfMessageBox.Show("設定Excelを取り込みました。", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"設定Excel取込に失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        var fullMemo = MemoText ?? string.Empty;
        var memoForAutoParse = GetMemoPrimarySegmentForAutoParse(MemoText ?? string.Empty);
        TryAutoFillCompanyNameFromMemo(memoForAutoParse);
        var knownCodes = GetKnownMemoCodes();
        var result = _memoService.ParseMemo(fullMemo, knownCodes, _synonyms);
        ApplyMemoParseResult(result);

        foreach (var code in result.MatchedCodes)
        {
            SelectByCode(code, null);
        }

        UpdateBasket();
    }

    [RelayCommand]
    private async Task ApplyQuickRequest()
    {
        var memo = MemoText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(memo))
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
        var memoForAutoParse = GetMemoPrimarySegmentForAutoParse(memo);
        var knownCodes = GetKnownMemoCodes();
        var knownCodeSet = new HashSet<string>(knownCodes, StringComparer.OrdinalIgnoreCase);
        var versionedRequests = ParseVersionedRequests(memo, knownCodes);
        var parseResult = _memoService.ParseMemo(memo, knownCodes, _synonyms);
        var defaultRequestedOs = GetDefaultRequestedOsFromMemo(memoForAutoParse);
        TryAutoFillCompanyNameFromMemo(memoForAutoParse);
        var decisionLines = new List<string>();
        var company = string.IsNullOrWhiteSpace(CompanyName) ? "(未設定)" : CompanyName.Trim();
        decisionLines.Add($"会社名: {company} ({(_isCompanyNameAutoFilled ? "自動抽出" : "既存/手入力")})");
        var osEvidenceLines = CollectOsEvidenceLines(memoForAutoParse);
        if (osEvidenceLines.Count > 0)
        {
            decisionLines.Add($"OS根拠: {string.Join(" / ", osEvidenceLines.Select(line => BuildPreviewText(line, 60)))}");
        }

        LocalLlmDecisionResult? llmDecision = null;
        if (ShouldRunLocalLlm(
                versionedRequests,
                parseResult,
                defaultRequestedOs,
                osEvidenceLines.Count,
                CompanyName))
        {
            llmDecision = await _localLlmDecisionService.AnalyzeMemoAsync(
                Settings.LocalLlmEndpoint,
                memoForAutoParse,
                knownCodes);
            if (llmDecision.IsSuccess)
            {
                var cacheTag = llmDecision.IsCached ? " / cache" : string.Empty;
                decisionLines.Add($"LLM判定: 成功 (model: {llmDecision.ModelName}{cacheTag})");
                if (TryApplyCompanyNameFromLlm(llmDecision.CompanyName))
                {
                    decisionLines.Add($"会社名(LLM): {CompanyName}");
                }
            }
            else
            {
                var cacheTag = llmDecision.IsCached ? " / cache" : string.Empty;
                decisionLines.Add($"LLM判定: 失敗{cacheTag} ({llmDecision.ErrorMessage})");
            }
        }
        else if (IsLocalLlmDecisionEnabled())
        {
            decisionLines.Add("LLM判定: スキップ (既存ルールで十分に抽出できたため)");
        }

        var helixMatch = FindHelixVersionFromText(memoForAutoParse);
        helixMatch ??= FindHelixVersionFromText(memo);
        var versionResult = "バージョン: (選択中のまま)";
        if (helixMatch != null)
        {
            SelectedVersion = helixMatch;
            versionResult = $"バージョン: {helixMatch.Version}";
            decisionLines.Add($"Helix判定: {helixMatch.Version}");
        }
        else
        {
            var requestedHelixVersion = FindRequestedHelixVersionToken(memoForAutoParse);
            requestedHelixVersion ??= FindRequestedHelixVersionToken(memo);
            if (!string.IsNullOrWhiteSpace(requestedHelixVersion))
            {
                versionResult = $"該当バージョンなし: {requestedHelixVersion}";
                decisionLines.Add($"Helix判定: 対応表に該当なし ({requestedHelixVersion})");
            }
            else
            {
                decisionLines.Add("Helix判定: 指定なし");
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

        if (llmDecision?.IsSuccess == true)
        {
            var llmDefaultRequestedOs = ParseRequestedOsToken(llmDecision.DefaultOs);
            if (llmDefaultRequestedOs != RequestedOs.Unspecified)
            {
                if (defaultRequestedOs == RequestedOs.Unspecified)
                {
                    defaultRequestedOs = llmDefaultRequestedOs;
                    decisionLines.Add($"既定OS(LLM採用): {FormatRequestedOs(llmDefaultRequestedOs)}");
                }
                else
                {
                    decisionLines.Add(
                        $"既定OS(LLM参考): {FormatRequestedOs(llmDefaultRequestedOs)} / 既存: {FormatRequestedOs(defaultRequestedOs)}");
                }
            }

            foreach (var llmRequest in llmDecision.VersionedRequests)
            {
                var normalizedCode = ResolveKnownCodeCandidate(llmRequest.Code, knownCodes);
                if (string.IsNullOrWhiteSpace(normalizedCode))
                {
                    decisionLines.Add($"LLM版数指定(未知コード): {llmRequest.Code}");
                    continue;
                }

                var requestedVersions = ExtractRequestedVersionTokens(llmRequest.Version);
                if (requestedVersions.Count == 0)
                {
                    decisionLines.Add($"LLM版数指定(版数なし): {normalizedCode}");
                    continue;
                }

                var requestedOs = ParseRequestedOsToken(llmRequest.Os);
                foreach (var requestedVersion in requestedVersions)
                {
                    if (!knownCodeSet.Contains(normalizedCode))
                    {
                        continue;
                    }

                    var existingIndex = versionedRequests.FindIndex(request =>
                        request.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase) &&
                        request.Version.Equals(requestedVersion, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                    {
                        var existing = versionedRequests[existingIndex];
                        versionedRequests[existingIndex] = existing with
                        {
                            OsSelection = MergeRequestedOs(existing.OsSelection, requestedOs)
                        };
                    }
                    else
                    {
                        versionedRequests.Add(new VersionedRequest(normalizedCode, requestedVersion, requestedOs));
                    }
                }
            }
        }

        decisionLines.Add($"既定OS判定: {FormatRequestedOs(defaultRequestedOs)}");
        if (versionedRequests.Count == 0)
        {
            decisionLines.Add("版数指定: なし");
        }
        else
        {
            decisionLines.Add("版数指定: " + string.Join(", ",
                versionedRequests.Select(r => $"{GetSelectionTargetCode(r.Code)} {r.Version} [{FormatRequestedOs(r.OsSelection)}]")));
        }
        var unmatchedCodes = new List<string>();
        var versionRequestedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var handledSelectionTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var osRequests = new Dictionary<ModuleRowViewModel, RequestedOs>();
        foreach (var request in versionedRequests)
        {
            var selectionCode = GetSelectionTargetCode(request.Code);
            var requestLabel = $"{selectionCode} {request.Version} [{FormatRequestedOs(request.OsSelection)}]";
            versionRequestedTargets.Add(selectionCode);
            if (SelectAcrossTabsByVersion(request.Code, request.Version, request.OsSelection, osRequests))
            {
                handledSelectionTargets.Add(selectionCode);
                decisionLines.Add($"版数一致選択: {requestLabel}");
                continue;
            }

            if (RequiresStrictVersionMatch(request.Code))
            {
                unmatchedCodes.Add($"{request.Code}({request.Version})");
                decisionLines.Add($"版数不一致で除外: {requestLabel}");
                continue;
            }

            // Version mismatch fallback: still try selecting by code only.
            if (!SelectByCode(request.Code, string.Empty))
            {
                unmatchedCodes.Add($"{request.Code}({request.Version})");
                decisionLines.Add($"版数不一致かつコード選択不可: {requestLabel}");
                continue;
            }

            handledSelectionTargets.Add(selectionCode);
            decisionLines.Add($"版数不一致のためコードのみ選択: {requestLabel}");
            if (SelectedVersion != null)
            {
                foreach (var module in SelectedVersion.Modules.Where(m =>
                             m.IsSelected &&
                             m.Code.Equals(selectionCode, StringComparison.OrdinalIgnoreCase)))
                {
                    RecordRequestedOs(osRequests, module, request.OsSelection);
                }
            }
        }

        ApplyMemoParseResult(parseResult);
        var matchedCodes = new HashSet<string>(parseResult.MatchedCodes, StringComparer.OrdinalIgnoreCase);
        if (llmDecision?.IsSuccess == true)
        {
            foreach (var llmCode in llmDecision.MatchedCodes)
            {
                var normalizedCode = ResolveKnownCodeCandidate(llmCode, knownCodes);
                if (string.IsNullOrWhiteSpace(normalizedCode))
                {
                    continue;
                }

                if (!knownCodeSet.Contains(normalizedCode))
                {
                    continue;
                }

                matchedCodes.Add(normalizedCode);
            }
        }

        if (matchedCodes.Count > 0)
        {
            decisionLines.Add("キーワード一致: " + string.Join(", ", matchedCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase)));
        }

        foreach (var code in matchedCodes)
        {
            var selectionCode = GetSelectionTargetCode(code);
            if (handledSelectionTargets.Contains(selectionCode))
            {
                continue;
            }

            // When version is explicitly requested, do not override with unversioned code picks.
            if (versionRequestedTargets.Contains(selectionCode))
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
            AddBasePickIfRequested(memo, string.Empty);
        }

        ApplyQuickRequestOsSelection(osRequests, defaultRequestedOs);
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
        decisionLines.Add($"未解決: {parseResult.UnresolvedTerms.Count}件 / 曖昧: {parseResult.AmbiguousMatches.Count}件");
        QuickRequestDecisionLog = string.Join(Environment.NewLine, decisionLines);
    }

    [RelayCommand]
    private void ClearMemo()
    {
        MemoText = string.Empty;
        UnresolvedMemoText = string.Empty;
        QuickRequestResult = string.Empty;
        QuickRequestDecisionLog = string.Empty;
        UnresolvedTerms.Clear();
        AmbiguousTerms.Clear();
    }

    [RelayCommand]
    private async Task ReapplyMemoWithCorrections()
    {
        var memo = MemoText ?? string.Empty;
        var unresolved = UnresolvedMemoText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(memo) && string.IsNullOrWhiteSpace(unresolved))
        {
            WpfMessageBox.Show("メール/メモまたは未解決内容を入力してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var correctedLines = new List<string>();
        var knownCodes = new HashSet<string>(GetKnownMemoCodes(), StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in unresolved.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryApplyCompanyNameOverrideFromCorrectionLine(line))
            {
                continue;
            }

            var mapping = MemoMappingRegex.Match(line);
            if (mapping.Success)
            {
                var term = mapping.Groups["term"].Value.Trim();
                var code = mapping.Groups["code"].Value.Trim();
                if (term.Length > 0 && code.Length > 0)
                {
                    if (knownCodes.Contains(code))
                    {
                        LearnMemoSynonym(term, code);
                        SelectByCode(code, string.Empty);
                        continue;
                    }

                    var normalizedCode = NormalizeModuleCode(code);
                    if (knownCodes.Contains(normalizedCode))
                    {
                        LearnMemoSynonym(term, normalizedCode);
                        SelectByCode(normalizedCode, string.Empty);
                        continue;
                    }
                }
            }

            correctedLines.Add(line);
        }

        MemoText = MergeDistinctMemoLines(memo, correctedLines);
        await ApplyQuickRequest();
    }

    public string BuildMemoLearningHistoryText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("[学習済みキーワード]");
        var learned = Settings.MemoLearnedSynonyms ?? new Dictionary<string, List<string>>();
        if (learned.Count == 0)
        {
            builder.AppendLine("(なし)");
        }
        else
        {
            foreach (var pair in learned.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var term = pair.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                var codes = (pair.Value ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (codes.Length == 0)
                {
                    continue;
                }

                builder.AppendLine($"{term} => {string.Join(", ", codes)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[未解決履歴]");
        if (MemoUnresolvedHistoryItems.Count == 0)
        {
            builder.AppendLine("(なし)");
        }
        else
        {
            foreach (var term in MemoUnresolvedHistoryItems)
            {
                builder.AppendLine(term);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void ApplyMemoParseResult(MemoParseResult result)
    {
        UnresolvedTerms.Clear();
        foreach (var term in result.UnresolvedTerms)
        {
            UnresolvedTerms.Add(term);
        }

        UnresolvedMemoText = string.Join(Environment.NewLine, result.UnresolvedTerms);
        SaveUnresolvedTermsToHistory(result.UnresolvedTerms);

        AmbiguousTerms.Clear();
        foreach (var match in result.AmbiguousMatches)
        {
            var vm = new AmbiguousMatchViewModel(match.Term, match.Candidates);
            vm.SelectedCodeChanged += (_, code) =>
            {
                LearnMemoSynonym(match.Term, code);
                SelectByCode(code, null);
                UpdateBasket();
            };
            AmbiguousTerms.Add(vm);
        }
    }

    private List<string> GetKnownMemoCodes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in GetKnownModuleCodes())
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            set.Add(code);
            foreach (var candidate in GetModuleCodeCandidates(code))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    set.Add(candidate);
                }
            }
        }

        foreach (var bundleCode in HelixQacBundleCodes)
        {
            set.Add(bundleCode);
        }

        return set.ToList();
    }

    private void RestoreMemoLearnedSynonymsFromSettings()
    {
        var source = Settings.MemoLearnedSynonyms ?? new Dictionary<string, List<string>>();
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var term = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            var codes = (pair.Value ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codes.Count == 0)
            {
                continue;
            }

            normalized[term] = codes;
            foreach (var code in codes)
            {
                AddSynonymAlias(_synonyms, term, code);
            }
        }

        Settings.MemoLearnedSynonyms = normalized;
    }

    private void LoadMemoUnresolvedHistoryFromSettings()
    {
        var history = (Settings.MemoUnresolvedHistory ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MemoUnresolvedHistoryLimit)
            .ToList();

        Settings.MemoUnresolvedHistory = history;
        MemoUnresolvedHistoryItems.Clear();
        foreach (var item in history)
        {
            MemoUnresolvedHistoryItems.Add(item);
        }
    }

    private void SaveUnresolvedTermsToHistory(IEnumerable<string> terms)
    {
        var merged = (Settings.MemoUnresolvedHistory ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        var original = merged.ToList();
        var changed = false;

        foreach (var term in terms
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim()))
        {
            merged.RemoveAll(value => value.Equals(term, StringComparison.OrdinalIgnoreCase));
            merged.Insert(0, term);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        if (merged.Count > MemoUnresolvedHistoryLimit)
        {
            merged.RemoveRange(MemoUnresolvedHistoryLimit, merged.Count - MemoUnresolvedHistoryLimit);
        }

        if (!changed ||
            (original.Count == merged.Count &&
             original.SequenceEqual(merged, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        Settings.MemoUnresolvedHistory = merged;
        _settingsService.Save(Settings);
        LoadMemoUnresolvedHistoryFromSettings();
    }

    private void LearnMemoSynonym(string term, string code)
    {
        var normalizedTerm = (term ?? string.Empty).Trim();
        var normalizedCode = NormalizeModuleCode(code ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedTerm) || string.IsNullOrWhiteSpace(normalizedCode))
        {
            return;
        }

        AddSynonymAlias(_synonyms, normalizedTerm, normalizedCode);

        Settings.MemoLearnedSynonyms ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Settings.MemoLearnedSynonyms.TryGetValue(normalizedTerm, out var list))
        {
            list = new List<string>();
            Settings.MemoLearnedSynonyms[normalizedTerm] = list;
        }

        if (list.Any(existing => existing.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        list.Add(normalizedCode);
        _settingsService.Save(Settings);
    }

    private bool TryApplyCompanyNameOverrideFromCorrectionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        var forceOverride = ContainsCompanyOverrideHint(trimmed);

        var labelMatch = CompanyOverrideRegex.Match(trimmed);
        if (labelMatch.Success &&
            TrySetCompanyNameFromCorrectionCandidate(labelMatch.Groups["name"].Value, force: true))
        {
            return true;
        }

        if (TrySplitMappingLine(trimmed, out var left, out var right))
        {
            if (TrySetCompanyNameFromCorrectionCandidate(right, force: forceOverride))
            {
                return true;
            }

            if (TrySetCompanyNameFromCorrectionCandidate(left, force: forceOverride))
            {
                return true;
            }
        }

        return forceOverride && TrySetCompanyNameFromCorrectionCandidate(trimmed, force: true);
    }

    private bool TrySetCompanyNameFromCorrectionCandidate(string source, bool force)
    {
        var candidate = ExtractCompanyNameFromOverrideSource(source);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var current = CompanyName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var canReplace = force || _isCompanyNameAutoFilled || ShouldReplaceExistingCompanyName(current, candidate);
            if (!canReplace || current.Equals(candidate, StringComparison.Ordinal))
            {
                return false;
            }
        }

        _isSettingCompanyNameFromMemo = true;
        try
        {
            CompanyName = candidate;
            _isCompanyNameAutoFilled = true;
            return true;
        }
        finally
        {
            _isSettingCompanyNameFromMemo = false;
        }
    }

    private static string ExtractCompanyNameFromOverrideSource(string source)
    {
        var trimmed = (source ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var inline = TryExtractCompanyFromLine(trimmed, strict: false);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline;
        }

        var cleaned = CleanCompanyNameCandidate(trimmed);
        return ContainsCompanyMarker(cleaned) ? cleaned : string.Empty;
    }

    private static bool ContainsCompanyOverrideHint(string line)
    {
        return line.Contains("会社名として優先", StringComparison.Ordinal)
               || line.Contains("会社名優先", StringComparison.Ordinal)
               || line.Contains("として優先", StringComparison.Ordinal)
               || line.Contains("を優先", StringComparison.Ordinal);
    }

    private static bool TrySplitMappingLine(string line, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var separators = new[] { "=>", "->", "→", "＝", "=" };
        foreach (var separator in separators)
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            left = line[..index].Trim();
            right = line[(index + separator.Length)..].Trim();
            return left.Length > 0 && right.Length > 0;
        }

        return false;
    }

    private void TryAutoFillCompanyNameFromMemo(string? text)
    {
        var candidate = ExtractCompanyNameFromMemo(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var current = CompanyName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var canReplace = _isCompanyNameAutoFilled || ShouldReplaceExistingCompanyName(current, candidate);
            if (!canReplace || current.Equals(candidate, StringComparison.Ordinal))
            {
                return;
            }
        }

        _isSettingCompanyNameFromMemo = true;
        try
        {
            CompanyName = candidate;
            _isCompanyNameAutoFilled = true;
        }
        finally
        {
            _isSettingCompanyNameFromMemo = false;
        }
    }

    private bool TryApplyCompanyNameFromLlm(string? candidate)
    {
        var cleaned = CleanCompanyNameCandidate(candidate ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var current = CompanyName?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var canReplace = _isCompanyNameAutoFilled || ShouldReplaceExistingCompanyName(current, cleaned);
            if (!canReplace || current.Equals(cleaned, StringComparison.Ordinal))
            {
                return false;
            }
        }

        _isSettingCompanyNameFromMemo = true;
        try
        {
            CompanyName = cleaned;
            _isCompanyNameAutoFilled = true;
            return true;
        }
        finally
        {
            _isSettingCompanyNameFromMemo = false;
        }
    }

    private static string ExtractCompanyNameFromMemo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = GetMemoPrimarySegmentForAutoParse(text);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .ToList();
        var addresseeCompanies = ExtractAddresseeCompanyCandidates(lines);

        var signatureCandidate = ExtractCompanyFromSignature(lines, addresseeCompanies);
        if (!string.IsNullOrWhiteSpace(signatureCandidate))
        {
            return signatureCandidate;
        }

        var selfIntroCandidate = ExtractCompanyFromSelfIntroduction(lines, addresseeCompanies);
        if (!string.IsNullOrWhiteSpace(selfIntroCandidate))
        {
            return selfIntroCandidate;
        }

        foreach (var trimmed in lines.Where(line => line.Length > 0))
        {
            var labelMatch = CompanyLabelRegex.Match(trimmed);
            if (labelMatch.Success)
            {
                var value = CleanCompanyNameCandidate(labelMatch.Groups["name"].Value);
                if (!string.IsNullOrWhiteSpace(value) && !ContainsCompanyCandidate(addresseeCompanies, value))
                {
                    return value;
                }
            }

            var honorificMatch = CompanyHonorificRegex.Match(trimmed);
            if (honorificMatch.Success)
            {
                var value = CleanCompanyNameCandidate(honorificMatch.Groups["name"].Value);
                if (!string.IsNullOrWhiteSpace(value) &&
                    ContainsCompanyMarker(value) &&
                    !ContainsCompanyCandidate(addresseeCompanies, value))
                {
                    return value;
                }
            }

            var keywordMatch = CompanyKeywordRegex.Match(trimmed);
            if (keywordMatch.Success)
            {
                var value = CleanCompanyNameCandidate(keywordMatch.Groups["name"].Value);
                if (!string.IsNullOrWhiteSpace(value) &&
                    ContainsCompanyMarker(value) &&
                    !ContainsCompanyCandidate(addresseeCompanies, value))
                {
                    return value;
                }
            }

            var inlineValue = TryExtractCompanyFromLine(trimmed, strict: true);
            if (!string.IsNullOrWhiteSpace(inlineValue) && !ContainsCompanyCandidate(addresseeCompanies, inlineValue))
            {
                return inlineValue;
            }
        }

        return string.Empty;
    }

    private static string ExtractCompanyFromSignature(
        IReadOnlyList<string> lines,
        ISet<string> addresseeCompanies)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var signatureStart = -1;
        var nonEmptyBefore = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i]?.Trim() ?? string.Empty;
            if (line.Length == 0)
            {
                continue;
            }

            nonEmptyBefore++;
            if (SignatureSeparatorRegex.IsMatch(line))
            {
                // Prefer the first separator in the latest (top) message block.
                if (nonEmptyBefore >= 3)
                {
                    signatureStart = i + 1;
                    break;
                }
            }

            if (IsQuotedMessageStartLine(line) && nonEmptyBefore > 3)
            {
                break;
            }
        }

        IEnumerable<string> targetLines;
        if (signatureStart >= 0 && signatureStart < lines.Count)
        {
            var signatureEnd = lines.Count;
            for (var i = signatureStart; i < lines.Count; i++)
            {
                var line = lines[i]?.Trim() ?? string.Empty;
                if (line.Length == 0)
                {
                    continue;
                }

                if (SignatureSeparatorRegex.IsMatch(line))
                {
                    signatureEnd = i;
                    break;
                }

                if (IsQuotedMessageStartLine(line) && i > signatureStart + 1)
                {
                    signatureEnd = i;
                    break;
                }
            }

            targetLines = lines.Skip(signatureStart).Take(Math.Max(0, signatureEnd - signatureStart));
        }
        else
        {
            targetLines = lines.Skip(Math.Max(0, lines.Count - 12));
        }

        foreach (var line in targetLines)
        {
            var value = TryExtractCompanyFromLine(line, strict: false);
            if (!string.IsNullOrWhiteSpace(value) && !ContainsCompanyCandidate(addresseeCompanies, value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static HashSet<string> ExtractAddresseeCompanyCandidates(IReadOnlyList<string> lines)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (lines.Count < 2)
        {
            return result;
        }

        for (var i = 0; i < lines.Count - 1; i++)
        {
            var current = (lines[i] ?? string.Empty).Trim();
            var next = (lines[i + 1] ?? string.Empty).Trim();
            if (current.Length == 0 || next.Length == 0)
            {
                continue;
            }

            if (!AddresseeLineRegex.IsMatch(next))
            {
                continue;
            }

            var company = CleanCompanyNameCandidate(current);
            if (company.Length == 0)
            {
                continue;
            }

            if (ContainsCompanyMarker(company) || company.Length <= 40)
            {
                result.Add(company);
            }
        }

        return result;
    }

    private static string ExtractCompanyFromSelfIntroduction(
        IReadOnlyList<string> lines,
        ISet<string> addresseeCompanies)
    {
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var candidate = TryExtractCompanyFromSelfIntroductionLine(line);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (ContainsCompanyCandidate(addresseeCompanies, candidate))
            {
                continue;
            }

            return candidate;
        }

        return string.Empty;
    }

    private static string TryExtractCompanyFromSelfIntroductionLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!SelfIntroPhraseRegex.IsMatch(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("@", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var withNoSuffix = IntroSuffixRegex.Replace(trimmed, string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(withNoSuffix) && ContainsCompanyMarker(withNoSuffix))
        {
            return CleanCompanyNameCandidate(withNoSuffix);
        }

        var selfIntroMatch = SelfIntroCompanyRegex.Match(trimmed);
        if (selfIntroMatch.Success)
        {
            var candidate = CleanCompanyNameCandidate(selfIntroMatch.Groups["name"].Value);
            if (IsLikelyCompanyToken(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string TryExtractCompanyFromLine(string line, bool strict)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("@", StringComparison.Ordinal) ||
            trimmed.Contains("TEL", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("電話", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var match = CompanyInlineRegex.Match(trimmed);
        if (!match.Success)
        {
            return string.Empty;
        }

        var candidate = CleanCompanyNameCandidate(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(candidate) || !ContainsCompanyMarker(candidate))
        {
            return string.Empty;
        }

        if (strict && IsLikelyPersonSentence(trimmed))
        {
            return string.Empty;
        }

        return candidate;
    }

    private static bool IsLikelyCompanyToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length < 2 || value.Length > 40)
        {
            return false;
        }

        if (value.Contains("様", StringComparison.Ordinal) ||
            value.Contains("御中", StringComparison.Ordinal) ||
            value.Contains("部", StringComparison.Ordinal) ||
            value.Contains("課", StringComparison.Ordinal) ||
            value.Contains("担当", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsLikelyPersonSentence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("です", StringComparison.Ordinal) ||
               value.Contains("ます", StringComparison.Ordinal) ||
               value.Contains("と申します", StringComparison.Ordinal) ||
               value.Contains("担当", StringComparison.Ordinal);
    }

    private static bool ShouldReplaceExistingCompanyName(string current, string candidate)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!ContainsCompanyMarker(current) && ContainsCompanyMarker(candidate))
        {
            return true;
        }

        if (IsLikelyPersonSentence(current) && !IsLikelyPersonSentence(candidate))
        {
            return true;
        }

        if (current.Contains(candidate, StringComparison.Ordinal) && current.Length > candidate.Length)
        {
            return true;
        }

        return false;
    }

    private static bool ContainsCompanyCandidate(ISet<string> candidates, string value)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (IsSameCompanyName(candidate, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameCompanyName(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var normalizedLeft = NormalizeCompanyComparisonKey(left);
        var normalizedRight = NormalizeCompanyComparisonKey(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return false;
        }

        return normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCompanyComparisonKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = CleanCompanyNameCandidate(value);
        normalized = normalized
            .Replace("（株）", "株式会社", StringComparison.Ordinal)
            .Replace("(株)", "株式会社", StringComparison.OrdinalIgnoreCase)
            .Replace("㈱", "株式会社", StringComparison.Ordinal)
            .Replace("（有）", "有限会社", StringComparison.Ordinal)
            .Replace("(有)", "有限会社", StringComparison.OrdinalIgnoreCase)
            .Replace("㈲", "有限会社", StringComparison.Ordinal)
            .Replace("（同）", "合同会社", StringComparison.Ordinal)
            .Replace("(同)", "合同会社", StringComparison.OrdinalIgnoreCase)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        normalized = normalized
            .Replace("株式会社", string.Empty, StringComparison.Ordinal)
            .Replace("有限会社", string.Empty, StringComparison.Ordinal)
            .Replace("合同会社", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("・", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Replace("。", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("：", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized;
    }

    private static bool ContainsCompanyMarker(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("株式会社", StringComparison.Ordinal) ||
               value.Contains("有限会社", StringComparison.Ordinal) ||
               value.Contains("合同会社", StringComparison.Ordinal);
    }

    private static string CleanCompanyNameCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("御中", string.Empty, StringComparison.Ordinal)
            .Replace("様", string.Empty, StringComparison.Ordinal)
            .Replace("（株）", "株式会社", StringComparison.Ordinal)
            .Replace("(株)", "株式会社", StringComparison.OrdinalIgnoreCase)
            .Replace("㈱", "株式会社", StringComparison.Ordinal)
            .Replace("（有）", "有限会社", StringComparison.Ordinal)
            .Replace("(有)", "有限会社", StringComparison.OrdinalIgnoreCase)
            .Replace("㈲", "有限会社", StringComparison.Ordinal)
            .Replace("（同）", "合同会社", StringComparison.Ordinal)
            .Replace("(同)", "合同会社", StringComparison.OrdinalIgnoreCase)
            .Replace("　", " ", StringComparison.Ordinal)
            .Trim();
        normalized = IntroSuffixRegex.Replace(normalized, string.Empty);
        normalized = normalized.Trim('\"', '\'', '「', '」', '【', '】', '[', ']', '(', ')');
        normalized = normalized.TrimEnd('。', '、', ',', '.', '・', ':', '：', ';');
        if (normalized.Length > 80)
        {
            normalized = normalized[..80].Trim();
        }

        return normalized;
    }

    private static string MergeDistinctMemoLines(string baseText, IReadOnlyCollection<string> additionalLines)
    {
        if (additionalLines.Count == 0)
        {
            return baseText;
        }

        var existing = new HashSet<string>(
            (baseText ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(baseText ?? string.Empty);
        foreach (var line in additionalLines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !existing.Add(trimmed))
            {
                continue;
            }

            if (builder.Length > 0 &&
                builder[^1] != '\n' &&
                builder[^1] != '\r')
            {
                builder.AppendLine();
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
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
    private void ResetSelectionState()
    {
        var result = WpfMessageBox.Show(
            "選定タブの選択状態と入力内容をリセットします。よろしいですか？",
            "リセット確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        ResetSelectionStateCore();
    }

    [RelayCommand]
    private void ApplySelectionStateHistory()
    {
        if (SelectedSelectionStateHistoryEntry == null)
        {
            WpfMessageBox.Show("呼び出す履歴を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplySelectionStateHistoryCore(SelectedSelectionStateHistoryEntry);
    }

    [RelayCommand]
    private void AddCustomTab()
    {
        var requestedName = string.IsNullOrWhiteSpace(NewCustomTabName)
            ? "カスタム"
            : NewCustomTabName.Trim();
        var uniqueName = GetUniqueCustomTabName(requestedName);

        var columns = ParseCustomColumnNames(NewCustomTabColumns);
        var tab = new CustomTabViewModel(uniqueName, columns);
        tab.Changed += OnCustomTabChanged;
        CustomTabs.Add(tab);
        SelectedCustomTab = tab;
        UpdateBasket();
        PersistCustomState();
    }

    [RelayCommand]
    private void RemoveSelectedCustomTab()
    {
        if (SelectedCustomTab == null)
        {
            return;
        }

        var removedTabName = SelectedCustomTab.Name;
        SelectedCustomTab.Changed -= OnCustomTabChanged;
        var removeTarget = SelectedCustomTab;
        CustomTabs.Remove(removeTarget);
        _customZipPlans.RemoveAll(plan => plan.TabName.Equals(removedTabName, StringComparison.OrdinalIgnoreCase));
        SelectedCustomTab = CustomTabs.LastOrDefault();
        UpdateBasket();
        PersistCustomState();
    }

    [RelayCommand]
    private void EditSelectedCustomTab()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("編集するカスタムタブを選択してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var requestedName = string.IsNullOrWhiteSpace(NewCustomTabName)
            ? SelectedCustomTab.Name
            : NewCustomTabName.Trim();

        if (!requestedName.Equals(SelectedCustomTab.Name, StringComparison.OrdinalIgnoreCase) &&
            CustomTabs.Any(tab => !ReferenceEquals(tab, SelectedCustomTab) &&
                                  tab.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)))
        {
            WpfMessageBox.Show("同名のカスタムタブが既に存在します。別のタブ名にしてください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousName = SelectedCustomTab.Name;
        SelectedCustomTab.Name = requestedName;
        SelectedCustomTab.ColumnsInput = NewCustomTabColumns ?? string.Empty;
        SelectedCustomTab.ApplyColumnsFromInput();
        if (!previousName.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 0; index < _customZipPlans.Count; index++)
            {
                var plan = _customZipPlans[index];
                if (plan.TabName.Equals(previousName, StringComparison.OrdinalIgnoreCase))
                {
                    _customZipPlans[index] = plan with { TabName = requestedName };
                }
            }
        }
        UpdateBasket();
        PersistCustomState();
    }

    [RelayCommand]
    private void ApplyCustomTabColumns()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("カスタムタブを選択してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedCustomTab.ApplyColumnsFromInput();
        UpdateBasket();
        PersistCustomState();
    }

    [RelayCommand]
    private void BrowseCustomFiles()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("先にカスタムタブを追加してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Win32.OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddCustomFilesToSelectedTab(dialog.FileNames, selectByDefault: true);
    }

    [RelayCommand]
    private void AddCustomFileByPath()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("先にカスタムタブを追加してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paths = ParseCustomFilePaths(SelectedCustomTab.NewFilePath);
        if (paths.Count == 0)
        {
            WpfMessageBox.Show("ファイルパスを入力してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AddCustomFilesToSelectedTab(paths, selectByDefault: true);
    }

    [RelayCommand]
    private void BrowseCustomDirectory()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("先にカスタムタブを追加してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "追加したいファイルがあるフォルダを選択してください。",
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrWhiteSpace(SelectedCustomTab.NewDirectoryPath) &&
            Directory.Exists(SelectedCustomTab.NewDirectoryPath))
        {
            dialog.SelectedPath = SelectedCustomTab.NewDirectoryPath;
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        SelectedCustomTab.NewDirectoryPath = dialog.SelectedPath;
        PersistCustomState();
    }

    [RelayCommand]
    private void ScanCustomDirectory()
    {
        if (SelectedCustomTab == null)
        {
            WpfMessageBox.Show("先にカスタムタブを追加してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var root = SelectedCustomTab.NewDirectoryPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            WpfMessageBox.Show("フォルダを指定してください。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(root))
        {
            WpfMessageBox.Show($"指定フォルダが見つかりません: {root}", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = EnumerateFilesRecursiveSafe(root);
        if (files.Count == 0)
        {
            WpfMessageBox.Show("配下に追加可能なファイルが見つかりませんでした。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AddCustomFilesToSelectedTab(files, selectByDefault: false);
    }

    private void AddCustomFilesToSelectedTab(IEnumerable<string> paths, bool selectByDefault)
    {
        if (SelectedCustomTab == null)
        {
            return;
        }

        var addedCount = 0;
        var missing = new List<string>();
        var skippedAuxiliary = 0;
        foreach (var path in paths)
        {
            var trimmed = path.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (ShouldExcludeFromCustomSelection(trimmed))
            {
                skippedAuxiliary++;
                continue;
            }

            if (!File.Exists(trimmed))
            {
                missing.Add(trimmed);
                continue;
            }

            if (SelectedCustomTab.AddFile(trimmed, selectByDefault))
            {
                addedCount++;
            }
        }

        SelectedCustomTab.NewFilePath = string.Empty;
        UpdateBasket();
        PersistCustomState();

        if (missing.Count > 0 || skippedAuxiliary > 0)
        {
            var lines = new List<string>();
            if (missing.Count > 0)
            {
                var preview = string.Join(Environment.NewLine, missing.Take(3));
                var suffix = missing.Count > 3 ? Environment.NewLine + "..." : string.Empty;
                lines.Add($"存在しないパスが {missing.Count} 件あります。");
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    lines.Add(preview + suffix);
                }
            }

            if (skippedAuxiliary > 0)
            {
                lines.Add($"補助ファイルを {skippedAuxiliary} 件除外しました。");
                lines.Add("除外対象: Thumbs.db / desktop.ini / 隠し・システム属性ファイル");
            }

            WpfMessageBox.Show(
                string.Join(Environment.NewLine, lines),
                "追加結果",
                MessageBoxButton.OK,
                missing.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        else if (addedCount == 0)
        {
            WpfMessageBox.Show("追加対象がありませんでした。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static IReadOnlyList<string> EnumerateFilesRecursiveSafe(string root)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (ShouldExcludeFromCustomSelection(file))
                    {
                        continue;
                    }

                    results.Add(file);
                }
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (ShouldSkipDirectoryTraversal(child))
                    {
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return results;
    }

    private static bool ShouldExcludeFromCustomSelection(string path)
    {
        if (ShouldIgnoreAuxiliaryFileByName(path))
        {
            return true;
        }

        return HasHiddenOrSystemAttributes(path);
    }

    private static bool ShouldIgnoreAuxiliaryFileByName(string path)
    {
        var fileName = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(fileName) && IgnoredAuxiliaryFileNames.Contains(fileName);
    }

    private static bool HasHiddenOrSystemAttributes(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool ShouldSkipDirectoryTraversal(string directory)
    {
        if (HasSiblingZipArchive(directory))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(directory);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasSiblingZipArchive(string directory)
    {
        try
        {
            var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(trimmed);
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var siblingZipPath = Path.Combine(parent, $"{name}.zip");
            return File.Exists(siblingZipPath);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
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

        if (TryRemoveCustomTabSelection(item))
        {
            return;
        }

        if (TryRemoveCustomZipSummary(item))
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

    public bool ToggleRedownloadForBasketItem(BasketItemViewModel? item)
    {
        if (item == null || item.IsMissing)
        {
            return false;
        }

        var destinationPaths = ResolveDestinationPathsForBasketItem(item)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (destinationPaths.Count == 0)
        {
            return false;
        }

        var shouldUnlock = destinationPaths.Any(path => !_redownloadUnlockedDestinationPaths.Contains(path));
        foreach (var destinationPath in destinationPaths)
        {
            if (shouldUnlock)
            {
                _redownloadUnlockedDestinationPaths.Add(destinationPath);
            }
            else
            {
                _redownloadUnlockedDestinationPaths.Remove(destinationPath);
            }
        }

        UpdateBasket();
        return true;
    }

    public bool ToggleRedownloadForModule(ModuleRowViewModel? module)
    {
        if (module == null || SelectedVersion == null)
        {
            return false;
        }

        var helixLabel = NormalizeHelixVersionLabel(SelectedVersion.Version);
        var destinationPaths = GetDestinationPathsForModuleSelection(helixLabel, module)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (destinationPaths.Count == 0)
        {
            return false;
        }

        var shouldUnlock = destinationPaths.Any(path => !_redownloadUnlockedDestinationPaths.Contains(path));
        foreach (var destinationPath in destinationPaths)
        {
            if (shouldUnlock)
            {
                _redownloadUnlockedDestinationPaths.Add(destinationPath);
            }
            else
            {
                _redownloadUnlockedDestinationPaths.Remove(destinationPath);
            }
        }

        UpdateBasket();
        return true;
    }

    [RelayCommand]
    private async Task QueueAddAsync()
    {
        var hasScanItems = ScanSelectionItems.Count > 0;
        var hasCustomSelections = CustomTabs.Any(tab => tab.GetSelectedFiles().Count > 0);
        if (SelectedVersion == null && !hasScanItems && !hasCustomSelections)
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

        var selectedTransferItems = BasketItems
            .Where(b => !b.IsMissing && !IsCustomZipSummaryItem(b) && !b.IsAlreadyDownloaded)
            .ToList();
        var alreadyDownloaded = BasketItems
            .Where(b => !b.IsMissing && !IsCustomZipSummaryItem(b) && b.IsAlreadyDownloaded)
            .ToList();
        var missing = BasketItems.Where(b => b.IsMissing && !IsCustomZipSummaryItem(b)).ToList();
        var hasZipPlans = _customZipPlans.Count > 0;
        if (missing.Count > 0)
        {
            WpfMessageBox.Show($"{missing.Count} 件の配布物が未検出です。", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (alreadyDownloaded.Count > 0)
        {
            WpfMessageBox.Show(
                $"{alreadyDownloaded.Count} 件は既にダウンロード済みのため、今回のキューから除外します。\n再ダウンロードしたい場合は、選定タブで対象行をダブルクリックして解除してください。",
                "既存ダウンロードを除外",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        if (selectedTransferItems.Count == 0 && !hasZipPlans)
        {
            WpfMessageBox.Show("転送対象がありません。", "情報不足", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmMessage = BuildQueueConfirmMessage(selectedTransferItems, outputRoot);
        var confirmResult = WpfMessageBox.Show(
            confirmMessage,
            "キュー追加確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes)
        {
            return;
        }

        var batches = selectedTransferItems
            .GroupBy(item => NormalizeHelixVersionLabel(item.HelixVersion))
            .ToList();
        var createdBatchIds = new List<long>();

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
            createdBatchIds.Add(batch.Id);

            foreach (var basketItem in group)
            {
                var asset = FindAssetBySourcePath(basketItem.SourcePath)
                            ?? FindLogicalAsset(basketItem.Code, basketItem.ModuleVersion, out _)
                            ?? CreateAssetFromBasketItem(basketItem);
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
                RegisterTransferItem(vm);
                TransferItems.Add(vm);
                await TransferManager.StartAsync(vm);
            }
        }

        var customZipBatchId = 0L;
        if (hasZipPlans)
        {
            customZipBatchId = createdBatchIds.FirstOrDefault();
            if (customZipBatchId == 0)
            {
                var customZipBatch = new TransferBatch
                {
                    TimestampUtc = DateTime.UtcNow,
                    Company = CompanyName,
                    HelixVersion = "カスタムZIP",
                    OutputRoot = outputRoot,
                    Memo = MemoText ?? string.Empty,
                    SelectedLogicalItemsJson = JsonSerializer.Serialize(
                        GetCustomZipPlans().Select(plan => new
                        {
                            Code = CustomZipSummaryCode,
                            plan.TabName,
                            plan.ArchiveBaseName,
                            ItemCount = plan.Items.Count
                        }))
                };

                customZipBatchId = await _databaseService.InsertBatchAsync(customZipBatch);
            }
        }

        await ExecuteCustomZipPlansAsync(outputRoot, customZipBatchId);
        SaveCurrentSelectionStateToHistory();

        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
        SelectedMainTabIndex = TransferTabIndex;
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private void ReflectSelectionToShipmentHistory()
    {
        RefreshShipmentHistoryItems(keepCurrentSelection: true, resetShipmentDate: true);
        if (ShipmentHistoryItems.Count == 0)
        {
            WpfMessageBox.Show(
                "送付履歴へ反映できる選定項目がありません。選定タブで対象を選んでください。",
                "情報",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void RefreshShipmentHistoryView()
    {
        RefreshShipmentHistoryViewInternal(showValidationError: true);
    }

    [RelayCommand]
    private void ClearShipmentHistoryViewFilters()
    {
        _isApplyingShipmentHistoryViewFilters = true;
        try
        {
            ShipmentHistoryPeriod = ShipmentHistoryPeriodOptionsList[0];
            ShipmentHistoryFromDate = null;
            ShipmentHistoryToDate = null;
            ShipmentHistoryCompanyFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryPersonFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryCategoryFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryHelixFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryCodeFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryNameFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryCompatibilityVersionFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistorySelectedOsFilter = ShipmentHistoryFilterAllOption;
            ShipmentHistoryInstallerFilter = ShipmentHistoryFilterAllOption;
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryCompanyMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryPersonMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryCategoryMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryHelixMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryCodeMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryNameMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryCompatibilityVersionMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistorySelectedOsMultiFilterOptions, true, applyImmediately: false);
            SetShipmentHistoryMultiFilterSelection(ShipmentHistoryInstallerMultiFilterOptions, true, applyImmediately: false);
        }
        finally
        {
            _isApplyingShipmentHistoryViewFilters = false;
        }

        RefreshShipmentHistoryMultiFilterDisplayProperties();
        ApplyShipmentHistoryViewFilters();
    }

    [RelayCommand]
    private void SetShipmentHistoryMultiFilterSelection(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return;
        }

        var parts = parameter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return;
        }

        var shouldSelect = string.Equals(parts[1], "All", StringComparison.OrdinalIgnoreCase);
        var options = ResolveShipmentHistoryMultiFilterOptions(parts[0]);
        if (options == null)
        {
            return;
        }

        SetShipmentHistoryMultiFilterSelection(options, shouldSelect, applyImmediately: true);
    }

    private void RefreshShipmentHistoryViewInternal(bool showValidationError)
    {
        var excelPath = (Settings.ShipmentHistoryExcelPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(excelPath))
        {
            _shipmentHistoryAllRecords.Clear();
            ShipmentHistoryViewItems.Clear();
            ShipmentHistoryViewSummary = "送付履歴Excelが未設定です。";
            if (showValidationError)
            {
                WpfMessageBox.Show("設定画面で『送付履歴Excel』を設定してください。", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (!excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _shipmentHistoryAllRecords.Clear();
            ShipmentHistoryViewItems.Clear();
            ShipmentHistoryViewSummary = "送付履歴Excelが .xlsx ではありません。";
            if (showValidationError)
            {
                WpfMessageBox.Show("送付履歴Excelは .xlsx ファイルを指定してください。", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (!File.Exists(excelPath))
        {
            _shipmentHistoryAllRecords.Clear();
            ShipmentHistoryViewItems.Clear();
            ShipmentHistoryViewSummary = "送付履歴Excelが見つかりません。";
            if (showValidationError)
            {
                WpfMessageBox.Show($"送付履歴Excelが見つかりません: {excelPath}", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        try
        {
            var rows = _shipmentHistoryExcelService.ReadAll(excelPath)
                .OrderByDescending(row => row.ShipmentDate)
                .ToList();

            _shipmentHistoryAllRecords.Clear();
            _shipmentHistoryAllRecords.AddRange(rows);

            RebuildShipmentHistoryViewFilterOptions();
            ApplyShipmentHistoryViewFilters();
        }
        catch (Exception ex)
        {
            _shipmentHistoryAllRecords.Clear();
            ShipmentHistoryViewItems.Clear();
            ShipmentHistoryViewSummary = "送付履歴の読込に失敗しました。";
            if (showValidationError)
            {
                WpfMessageBox.Show(
                    $"送付履歴Excelの読込に失敗しました。\n{ex.Message}",
                    "読込エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void RebuildShipmentHistoryViewFilterOptions()
    {
        _isApplyingShipmentHistoryViewFilters = true;
        try
        {
            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryCompanyMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.CompanyName));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryPersonMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.PersonName));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryCategoryMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.Category));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryHelixMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.HelixVersion));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryCodeMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.Code));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryNameMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.Name));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryCompatibilityVersionMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.CompatibilityVersion));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistorySelectedOsMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.SelectedOs));

            UpdateShipmentHistoryMultiFilterOptions(
                ShipmentHistoryInstallerMultiFilterOptions,
                _shipmentHistoryAllRecords.Select(row => row.InstallerName));
        }
        finally
        {
            _isApplyingShipmentHistoryViewFilters = false;
        }

        RefreshShipmentHistoryMultiFilterDisplayProperties();
    }

    private void UpdateShipmentHistoryMultiFilterOptions(
        ObservableCollection<FilterCheckItemViewModel> target,
        IEnumerable<string> values)
    {
        var previousStates = target.ToDictionary(
            item => item.Value,
            item => item.IsChecked,
            StringComparer.OrdinalIgnoreCase);

        var hadItems = target.Count > 0;
        var hadAllSelected = hadItems && target.All(item => item.IsChecked);

        foreach (var existing in target)
        {
            existing.PropertyChanged -= OnShipmentHistoryMultiFilterOptionPropertyChanged;
        }

        target.Clear();

        var distinctValues = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var value in distinctValues)
        {
            var isChecked = !hadItems
                || hadAllSelected
                || (previousStates.TryGetValue(value, out var previousChecked) && previousChecked);

            var option = new FilterCheckItemViewModel(value, isChecked);
            option.PropertyChanged += OnShipmentHistoryMultiFilterOptionPropertyChanged;
            target.Add(option);
        }
    }

    private void UpdateShipmentHistoryFilterOptions(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        Action<string> setSelected,
        string currentSelection)
    {
        var distinctValues = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        target.Clear();
        target.Add(ShipmentHistoryFilterAllOption);
        foreach (var value in distinctValues)
        {
            target.Add(value);
        }

        var resolvedSelection = distinctValues.FirstOrDefault(value =>
            string.Equals(value, currentSelection, StringComparison.OrdinalIgnoreCase));
        setSelected(resolvedSelection ?? ShipmentHistoryFilterAllOption);
    }

    private void OnShipmentHistoryMultiFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(FilterCheckItemViewModel.IsChecked), StringComparison.Ordinal))
        {
            return;
        }

        if (_isApplyingShipmentHistoryViewFilters)
        {
            return;
        }

        RefreshShipmentHistoryMultiFilterDisplayProperties();
        ApplyShipmentHistoryViewFilters();
    }

    private void RefreshShipmentHistoryMultiFilterDisplayProperties()
    {
        OnPropertyChanged(nameof(ShipmentHistoryCompanyFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryPersonFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryCategoryFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryHelixFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryCodeFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryNameFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryCompatibilityVersionFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistorySelectedOsFilterDisplay));
        OnPropertyChanged(nameof(ShipmentHistoryInstallerFilterDisplay));
    }

    private static string BuildShipmentHistoryMultiFilterDisplay(IReadOnlyCollection<FilterCheckItemViewModel> options)
    {
        if (options.Count == 0)
        {
            return ShipmentHistoryFilterAllOption;
        }

        var selected = options.Where(item => item.IsChecked).Select(item => item.Value).ToList();
        if (selected.Count == 0)
        {
            return ShipmentHistoryFilterNoneOption;
        }

        if (selected.Count == options.Count)
        {
            return ShipmentHistoryFilterAllOption;
        }

        if (selected.Count == 1)
        {
            return selected[0];
        }

        return $"{selected.Count}件選択";
    }

    private ObservableCollection<FilterCheckItemViewModel>? ResolveShipmentHistoryMultiFilterOptions(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "company" => ShipmentHistoryCompanyMultiFilterOptions,
            "person" => ShipmentHistoryPersonMultiFilterOptions,
            "category" => ShipmentHistoryCategoryMultiFilterOptions,
            "helix" => ShipmentHistoryHelixMultiFilterOptions,
            "code" => ShipmentHistoryCodeMultiFilterOptions,
            "name" => ShipmentHistoryNameMultiFilterOptions,
            "version" => ShipmentHistoryCompatibilityVersionMultiFilterOptions,
            "os" => ShipmentHistorySelectedOsMultiFilterOptions,
            "installer" => ShipmentHistoryInstallerMultiFilterOptions,
            _ => null
        };
    }

    private void SetShipmentHistoryMultiFilterSelection(
        ObservableCollection<FilterCheckItemViewModel> options,
        bool isChecked,
        bool applyImmediately)
    {
        _isApplyingShipmentHistoryViewFilters = true;
        try
        {
            foreach (var option in options)
            {
                option.IsChecked = isChecked;
            }
        }
        finally
        {
            _isApplyingShipmentHistoryViewFilters = false;
        }

        RefreshShipmentHistoryMultiFilterDisplayProperties();
        if (applyImmediately)
        {
            ApplyShipmentHistoryViewFilters();
        }
    }

    private void ApplyShipmentHistoryViewFilters()
    {
        if (_isApplyingShipmentHistoryViewFilters)
        {
            return;
        }

        IEnumerable<ShipmentHistoryRecord> filtered = _shipmentHistoryAllRecords;

        var (periodFrom, periodTo) = GetShipmentHistoryPeriodRange(ShipmentHistoryPeriod);
        DateTime? effectiveFrom = periodFrom;
        DateTime? effectiveTo = periodTo;

        if (ShipmentHistoryFromDate.HasValue)
        {
            var from = ShipmentHistoryFromDate.Value.Date;
            effectiveFrom = effectiveFrom.HasValue
                ? (from > effectiveFrom.Value ? from : effectiveFrom.Value)
                : from;
        }

        if (ShipmentHistoryToDate.HasValue)
        {
            var to = ShipmentHistoryToDate.Value.Date;
            effectiveTo = effectiveTo.HasValue
                ? (to < effectiveTo.Value ? to : effectiveTo.Value)
                : to;
        }

        if (effectiveFrom.HasValue)
        {
            filtered = filtered.Where(row => row.ShipmentDate != DateTime.MinValue && row.ShipmentDate.Date >= effectiveFrom.Value);
        }

        if (effectiveTo.HasValue)
        {
            filtered = filtered.Where(row => row.ShipmentDate != DateTime.MinValue && row.ShipmentDate.Date <= effectiveTo.Value);
        }

        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryCompanyMultiFilterOptions, row => row.CompanyName);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryPersonMultiFilterOptions, row => row.PersonName);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryCategoryMultiFilterOptions, row => row.Category);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryHelixMultiFilterOptions, row => row.HelixVersion);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryCodeMultiFilterOptions, row => row.Code);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryNameMultiFilterOptions, row => row.Name);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryCompatibilityVersionMultiFilterOptions, row => row.CompatibilityVersion);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistorySelectedOsMultiFilterOptions, row => row.SelectedOs);
        filtered = ApplyShipmentHistoryMultiFilter(filtered, ShipmentHistoryInstallerMultiFilterOptions, row => row.InstallerName);

        var filteredList = filtered
            .OrderByDescending(row => row.ShipmentDate)
            .ThenBy(row => row.CompanyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ShipmentHistoryViewItems.Clear();
        foreach (var row in filteredList)
        {
            ShipmentHistoryViewItems.Add(row);
        }

        ShipmentHistoryViewSummary = $"表示: {filteredList.Count} / 全{_shipmentHistoryAllRecords.Count}";
    }

    private static IEnumerable<ShipmentHistoryRecord> ApplyShipmentHistoryFilter(
        IEnumerable<ShipmentHistoryRecord> source,
        string filterValue,
        Func<ShipmentHistoryRecord, string> selector)
    {
        if (string.IsNullOrWhiteSpace(filterValue)
            || string.Equals(filterValue, ShipmentHistoryFilterAllOption, StringComparison.Ordinal))
        {
            return source;
        }

        return source.Where(row =>
            string.Equals(
                (selector(row) ?? string.Empty).Trim(),
                filterValue,
                StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ShipmentHistoryRecord> ApplyShipmentHistoryMultiFilter(
        IEnumerable<ShipmentHistoryRecord> source,
        IReadOnlyCollection<FilterCheckItemViewModel> options,
        Func<ShipmentHistoryRecord, string> selector)
    {
        if (options.Count == 0)
        {
            return source;
        }

        var selected = options
            .Where(option => option.IsChecked)
            .Select(option => option.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selected.Count == options.Count)
        {
            return source;
        }

        if (selected.Count == 0)
        {
            return Array.Empty<ShipmentHistoryRecord>();
        }

        return source.Where(row => selected.Contains((selector(row) ?? string.Empty).Trim()));
    }

    private static (DateTime? From, DateTime? To) GetShipmentHistoryPeriodRange(string period)
    {
        var today = DateTime.Today;
        return period switch
        {
            "1週間" => (today.AddDays(-6), today),
            "1か月" => (today.AddMonths(-1), today),
            "1年" => (today.AddYears(-1), today),
            _ => (null, null)
        };
    }

    [RelayCommand]
    private void WriteShipmentHistory()
    {
        if (!TryBuildShipmentHistoryRecords(out var records, out var errorMessage))
        {
            WpfMessageBox.Show(errorMessage, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _shipmentHistoryExcelService.AppendMany(Settings.ShipmentHistoryExcelPath, records);
            WpfMessageBox.Show($"送付履歴に {records.Count} 件記録しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            ShipmentPersonName = string.Empty;
            RefreshShipmentHistoryItems(keepCurrentSelection: true);
            RefreshShipmentHistoryViewInternal(showValidationError: false);
        }
        catch (IOException ex)
        {
            WpfMessageBox.Show(
                $"送付履歴Excelに書き込めません。Excelが開かれていないか確認してください。\n{ex.Message}",
                "書込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            WpfMessageBox.Show(
                $"送付履歴Excelへのアクセスが拒否されました。\n{ex.Message}",
                "アクセスエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"送付履歴への記録に失敗しました。\n{ex.Message}",
                "書込エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool TryBuildShipmentHistoryRecords(out List<ShipmentHistoryRecord> records, out string errorMessage)
    {
        records = [];
        errorMessage = string.Empty;

        if (ShipmentDate == default)
        {
            errorMessage = "送付日を入力してください。";
            return false;
        }

        var company = (ShipmentCompanyName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(company))
        {
            errorMessage = "会社名が未入力です。選定内容を確認してください。";
            return false;
        }

        var person = (ShipmentPersonName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(person))
        {
            errorMessage = "個人名を入力してください。";
            return false;
        }

        var category = (ShipmentCategory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            errorMessage = "区分を選択してください。";
            return false;
        }

        if (ShipmentHistoryItems.Count == 0)
        {
            errorMessage = "選定内容が未反映です。『選定内容を再読込』を実行してください。";
            return false;
        }

        var excelPath = (Settings.ShipmentHistoryExcelPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(excelPath))
        {
            errorMessage = "設定画面で『送付履歴Excel』を設定してください。";
            return false;
        }

        if (!excelPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "送付履歴Excelは .xlsx ファイルを指定してください。";
            return false;
        }

        if (!File.Exists(excelPath))
        {
            errorMessage = $"送付履歴Excelが見つかりません: {excelPath}";
            return false;
        }

        var targetItems = ShipmentHistoryItems
            .Where(item => !item.IsMissing && item.IsShipmentIncluded)
            .ToList();

        if (targetItems.Count == 0)
        {
            errorMessage = "記入対象が選択されていません。記入する行にチェックを入れてください。";
            return false;
        }

        var invalidItems = new List<string>();
        foreach (var item in targetItems)
        {
            if (string.IsNullOrWhiteSpace(item.HelixVersion)
                || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.Name)
                || string.IsNullOrWhiteSpace(item.ModuleVersion)
                || string.IsNullOrWhiteSpace(item.Os)
                || string.IsNullOrWhiteSpace(item.AssetFileName))
            {
                invalidItems.Add(item.Code);
                continue;
            }

            records.Add(new ShipmentHistoryRecord
            {
                ShipmentDate = ShipmentDate.Date,
                CompanyName = company,
                PersonName = person,
                Category = category,
                HelixVersion = item.HelixVersion,
                Code = item.Code,
                Name = item.Name,
                CompatibilityVersion = item.ModuleVersion,
                SelectedOs = item.Os,
                InstallerName = item.AssetFileName
            });
        }

        if (records.Count == 0)
        {
            errorMessage = "選定内容の必須項目が不足しています。『選定内容を再読込』を実行してください。";
            return false;
        }

        if (invalidItems.Count > 0)
        {
            errorMessage = $"選定内容の必須項目が不足しています。対象: {string.Join(", ", invalidItems)}";
            return false;
        }

        return true;
    }

    private void RefreshShipmentHistoryItems(bool keepCurrentSelection = true, bool resetShipmentDate = false)
    {
        var previousKey = keepCurrentSelection && SelectedShipmentHistoryItem != null
            ? GetShipmentHistoryItemKey(SelectedShipmentHistoryItem)
            : string.Empty;
        var includeStates = keepCurrentSelection
            ? ShipmentHistoryItems.ToDictionary(
                GetShipmentHistoryItemKey,
                item => item.IsShipmentIncluded,
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        ShipmentHistoryItems.Clear();
        foreach (var item in BasketItems.Where(item => !item.IsMissing))
        {
            var key = GetShipmentHistoryItemKey(item);
            item.IsShipmentIncluded = includeStates.TryGetValue(key, out var include) ? include : true;
            ShipmentHistoryItems.Add(item);
        }

        if (resetShipmentDate || ShipmentDate == default)
        {
            ShipmentDate = DateTime.Today;
        }

        ShipmentCompanyName = CompanyName?.Trim() ?? string.Empty;

        BasketItemViewModel? selected = null;
        if (!string.IsNullOrWhiteSpace(previousKey))
        {
            selected = ShipmentHistoryItems.FirstOrDefault(item =>
                GetShipmentHistoryItemKey(item).Equals(previousKey, StringComparison.OrdinalIgnoreCase));
        }

        SelectedShipmentHistoryItem = selected ?? ShipmentHistoryItems.FirstOrDefault();
        ApplyShipmentHistorySelection(SelectedShipmentHistoryItem);
    }

    private void ApplyShipmentHistorySelection(BasketItemViewModel? item)
    {
        if (item == null)
        {
            ShipmentHelixVersion = string.Empty;
            ShipmentCode = string.Empty;
            ShipmentName = string.Empty;
            ShipmentCompatibilityVersion = string.Empty;
            ShipmentSelectedOs = string.Empty;
            ShipmentInstallerName = string.Empty;
            return;
        }

        ShipmentHelixVersion = item.HelixVersion;
        ShipmentCode = item.Code;
        ShipmentName = item.Name;
        ShipmentCompatibilityVersion = item.ModuleVersion;
        ShipmentSelectedOs = item.Os;
        ShipmentInstallerName = item.AssetFileName;
    }

    private static string GetShipmentHistoryItemKey(BasketItemViewModel item)
    {
        return $"{item.HelixVersion}|{item.Code}|{item.AssetFileName}|{item.SourcePath}";
    }

    private string BuildQueueConfirmMessage(IReadOnlyList<BasketItemViewModel> selectedItems, string outputRoot)
    {
        var lines = new List<string>
        {
            "以下の内容でキュー追加しますか？",
            $"会社名: {CompanyName}",
            $"出力先: {outputRoot}",
            $"件数: {selectedItems.Count}",
            string.Empty,
            "対象ファイル:"
        };

        if (_customZipPlans.Count > 0)
        {
            lines.Insert(5, $"圧縮ZIP: {_customZipPlans.Count} 件");
        }

        foreach (var item in selectedItems.Take(20))
        {
            var helix = string.IsNullOrWhiteSpace(item.HelixVersion) ? "-" : item.HelixVersion;
            lines.Add($"・[{helix}] {item.AssetFileName}");
        }

        if (selectedItems.Count > 20)
        {
            lines.Add($"... 他 {selectedItems.Count - 20} 件");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task ExecuteCustomZipPlansAsync(string outputRoot, long customZipBatchId)
    {
        var plans = GetCustomZipPlans();
        if (plans.Count == 0)
        {
            return;
        }

        var created = 0;
        var skipped = 0;
        var skippedAlreadyDownloaded = 0;
        var errors = new List<string>();
        var usedArchiveBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            var rows = plan.Items
                .Where(item => File.Exists(item.SourcePath))
                .ToList();
            if (rows.Count == 0)
            {
                skipped++;
                continue;
            }

            TransferItemRecord? zipRecord = null;
            TransferItemViewModel? zipVm = null;

            try
            {
                var requestedArchiveName = GetSafeArchiveBaseName(plan.ArchiveBaseName, plan.TabName);
                var safeArchiveName = GetUniqueArchiveBaseName(requestedArchiveName, usedArchiveBaseNames);
                var zipPath = Path.Combine(outputRoot, $"{safeArchiveName}.zip");

                if (IsDestinationAlreadyDownloaded(zipPath))
                {
                    skippedAlreadyDownloaded++;
                    continue;
                }

                if (customZipBatchId > 0)
                {
                    long sourceTotalBytes = 0;
                    var latestSourceLwt = DateTime.UtcNow;
                    var hasLwt = false;
                    foreach (var row in rows)
                    {
                        if (string.IsNullOrWhiteSpace(row.SourcePath))
                        {
                            continue;
                        }

                        try
                        {
                            var info = new FileInfo(row.SourcePath);
                            if (!info.Exists)
                            {
                                continue;
                            }

                            sourceTotalBytes += Math.Max(0, info.Length);
                            if (!hasLwt || info.LastWriteTimeUtc > latestSourceLwt)
                            {
                                latestSourceLwt = info.LastWriteTimeUtc;
                                hasLwt = true;
                            }
                        }
                        catch
                        {
                            // ignore per-item stat failures; zip creation itself will validate existence.
                        }
                    }

                    zipRecord = new TransferItemRecord
                    {
                        BatchId = customZipBatchId,
                        Company = CompanyName,
                        LogicalKey = $"{CustomZipSummaryCode}|{plan.TabName}|{safeArchiveName}",
                        AssetSourcePath = BuildCustomZipPlanSourcePath(plan.TabName, safeArchiveName),
                        DestPath = zipPath,
                        Size = sourceTotalBytes,
                        Status = TransferStatus.Queued,
                        SourceLastWriteTimeUtc = hasLwt ? latestSourceLwt : DateTime.UtcNow
                    };
                    zipRecord.Id = await _databaseService.InsertTransferItemAsync(zipRecord);

                    zipVm = new TransferItemViewModel(zipRecord, TransferManager);
                    RegisterTransferItem(zipVm);
                    TransferItems.Add(zipVm);

                    zipVm.PrepareForStart();
                    zipVm.SetStatus(TransferStatus.Downloading);
                    await _databaseService.UpdateTransferItemAsync(zipRecord);
                }

                await Task.Run(() => CreateCustomZipArchive(
                    zipPath,
                    rows,
                    zipVm == null
                        ? null
                        : (copied, total) =>
                        {
                            var effectiveTotal = total <= 0 ? Math.Max(1, copied) : total;
                            var safeCopied = Math.Min(copied, effectiveTotal);
                            zipVm.ReportProgress(safeCopied, effectiveTotal, 0, 0);
                        }));

                if (zipRecord != null && zipVm != null)
                {
                    long zipSize = 0;
                    try
                    {
                        var zipInfo = new FileInfo(zipPath);
                        if (zipInfo.Exists)
                        {
                            zipSize = Math.Max(0, zipInfo.Length);
                        }
                    }
                    catch
                    {
                        // keep best-effort progress values
                    }

                    zipRecord.BytesCopied = zipSize;
                    zipRecord.Size = zipSize;
                    zipVm.ReportProgress(zipSize, zipSize, 0, 0);
                    zipVm.MarkCompleted();
                    await _databaseService.UpdateTransferItemAsync(zipRecord);
                }

                created++;
            }
            catch (Exception ex)
            {
                if (zipRecord != null && zipVm != null)
                {
                    zipVm.MarkFailed($"ZipCreateFailed:{ex.Message}");
                    await _databaseService.UpdateTransferItemAsync(zipRecord);
                }

                errors.Add($"{plan.TabName}: {ex.Message}");
            }
        }

        if (created == 0 && skipped == 0 && errors.Count == 0)
        {
            return;
        }

        var lines = new List<string>
        {
            $"キュー追加時の自動圧縮: 完了 {created} 件"
        };

        if (skipped > 0)
        {
            lines.Add($"スキップ(存在ファイルなし): {skipped} 件");
        }

        if (skippedAlreadyDownloaded > 0)
        {
            lines.Add($"スキップ(既にDL済み): {skippedAlreadyDownloaded} 件");
        }

        if (errors.Count > 0)
        {
            lines.Add($"失敗: {errors.Count} 件");
            lines.AddRange(errors.Take(3));
            if (errors.Count > 3)
            {
                lines.Add("...");
            }
        }

        var icon = errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning;
        WpfMessageBox.Show(string.Join(Environment.NewLine, lines), "圧縮結果", MessageBoxButton.OK, icon);
    }

    private static string GetSafeArchiveBaseName(string archiveBaseName, string fallbackTabName)
    {
        var candidate = string.IsNullOrWhiteSpace(archiveBaseName) ? fallbackTabName : archiveBaseName;
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "custom" : sanitized;
    }

    private static string GetUniqueArchiveBaseName(string requestedName, ISet<string> usedNames)
    {
        if (usedNames.Add(requestedName))
        {
            return requestedName;
        }

        var index = 2;
        while (true)
        {
            var candidate = $"{requestedName} ({index})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static void CreateCustomZipArchive(
        string zipPath,
        IReadOnlyList<CustomZipPlanItem> rows,
        Action<long, long>? progress = null)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var validRows = rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SourcePath) &&
                File.Exists(row.SourcePath) &&
                !ShouldExcludeFromCustomSelection(row.SourcePath))
            .ToList();
        long totalBytes = 0;
        foreach (var row in validRows)
        {
            try
            {
                totalBytes += Math.Max(0, new FileInfo(row.SourcePath).Length);
            }
            catch
            {
                // ignore per-item stat failures
            }
        }

        var copiedBytes = 0L;
        progress?.Invoke(0, totalBytes);

        using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in validRows)
        {
            var entryName = row.IncludeFolderInArchive &&
                            !string.IsNullOrWhiteSpace(row.FolderName) &&
                            !string.Equals(row.FolderName, "-", StringComparison.Ordinal)
                ? $"{row.FolderName}/{row.FileName}"
                : row.FileName;

            entryName = GetUniqueEntryName(entryName, usedEntryNames);

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var sourceStream = new FileStream(row.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[128 * 1024];
            int read;
            while ((read = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                entryStream.Write(buffer, 0, read);
                copiedBytes += read;
                progress?.Invoke(copiedBytes, totalBytes);
            }
        }

        progress?.Invoke(totalBytes, totalBytes);
    }

    private static string GetUniqueEntryName(string entryName, ISet<string> usedEntryNames)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        if (usedEntryNames.Add(normalized))
        {
            return normalized;
        }

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var extension = Path.GetExtension(normalized);
        var index = 2;

        while (true)
        {
            var candidateFileName = $"{fileName} ({index}){extension}";
            var candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateFileName
                : $"{directory}/{candidateFileName}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }

            index++;
        }
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
    private void SelectAllHistory()
    {
        foreach (var item in HistoryItems)
        {
            item.IsSelected = true;
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

    [RelayCommand]
    private void OpenHistoryFolder(HistoryItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var path = item.OutputRoot;
        if (string.IsNullOrWhiteSpace(path))
        {
            WpfMessageBox.Show("出力先が設定されていません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(path))
        {
            WpfMessageBox.Show($"フォルダが見つかりません: {path}", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void DeleteHistoryFolder(HistoryItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var path = item.OutputRoot;
        if (string.IsNullOrWhiteSpace(path))
        {
            WpfMessageBox.Show("出力先が設定されていません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            WpfMessageBox.Show($"対象が見つかりません: {path}", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = WpfMessageBox.Show($"ディレクトリを削除しますか？\n{path}", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            DeletePathRecursively(path);
            RemoveEmptyParentDirectories(path, OutputBaseFolder);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"削除に失敗しました: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void DeletePathRecursively(string path)
    {
        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            NormalizeAttributes(directory);
            directory.Delete(true);
            return;
        }

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }
    }

    private static void RemoveEmptyParentDirectories(string targetPath, string boundaryRoot)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(boundaryRoot))
        {
            return;
        }

        var boundaryFullPath = Path.GetFullPath(boundaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var currentPath = File.Exists(targetPath)
            ? Path.GetDirectoryName(targetPath)
            : targetPath;

        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            var fullPath = Path.GetFullPath(currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!fullPath.StartsWith(boundaryFullPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (fullPath.Equals(boundaryFullPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (Directory.Exists(fullPath))
            {
                if (Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    break;
                }

                var directoryInfo = new DirectoryInfo(fullPath)
                {
                    Attributes = FileAttributes.Normal
                };
                directoryInfo.Delete();
            }

            currentPath = Directory.GetParent(fullPath)?.FullName;
        }
    }

    private static void NormalizeAttributes(DirectoryInfo directory)
    {
        directory.Attributes = FileAttributes.Normal;
        foreach (var entry in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
        {
            entry.Attributes = FileAttributes.Normal;
        }
    }

    [RelayCommand]
    private async Task ClearTransferHistoryAsync()
    {
        var removable = TransferItems.Where(item =>
            item.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Canceled).ToList();
        if (removable.Count == 0)
        {
            WpfMessageBox.Show("削除できる転送履歴がありません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = WpfMessageBox.Show("完了/失敗/キャンセル済みの転送履歴を削除しますか？",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _databaseService.ClearTransferHistoryAsync();
        foreach (var item in removable)
        {
            UnregisterTransferItem(item);
            TransferItems.Remove(item);
        }

        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var selected = HistoryItems.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            WpfMessageBox.Show("削除する履歴を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = WpfMessageBox.Show($"選択した {selected.Count} 件の履歴を削除しますか？",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _databaseService.DeleteHistoryBatchesAsync(selected.Select(item => item.BatchId).ToList());
        foreach (var item in selected)
        {
            HistoryItems.Remove(item);
        }
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
                    : GetModuleSupportInfo(helix, code);
                var supported = supportInfo?.IsSupported ?? IsDefaultSupportedWhenMissing(code);
                var moduleVersion = supportInfo?.ModuleVersion ?? string.Empty;
                var moduleVersionDisplay = moduleVersion;
                if (code.Equals(HelixQacCode, StringComparison.OrdinalIgnoreCase))
                {
                    moduleVersion = helix.Version;
                    moduleVersionDisplay = BuildHelixQacModuleVersionDisplay(helix, moduleVersion);
                }
                else if (code.Equals("DASHBOARD", StringComparison.OrdinalIgnoreCase)
                         && string.IsNullOrWhiteSpace(moduleVersion))
                {
                    var defaultDashboardVersion = GetDefaultDashboardModuleVersion(helix.Version);
                    if (!string.IsNullOrWhiteSpace(defaultDashboardVersion))
                    {
                        // 対応表に明示がない場合の既定表示
                        moduleVersion = defaultDashboardVersion;
                        moduleVersionDisplay = moduleVersion;
                    }
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
                    isSelectionLeader,
                    moduleVersionDisplay);
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

    private void OnCustomTabChanged(object? sender, EventArgs e)
    {
        if (_isApplyingSelectionHistory)
        {
            return;
        }

        UpdateBasket();
        PersistCustomState();
    }

    private string GetUniqueCustomTabName(string requested)
    {
        if (!CustomTabs.Any(tab => tab.Name.Equals(requested, StringComparison.OrdinalIgnoreCase)))
        {
            return requested;
        }

        var index = 2;
        while (true)
        {
            var candidate = $"{requested}{index}";
            if (!CustomTabs.Any(tab => tab.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            index++;
        }
    }

    private static IReadOnlyList<string> ParseCustomColumnNames(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { ',', '、', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParseCustomFilePaths(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { '\r', '\n', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private BulkSelectionWorkbookModel BuildBulkSelectionWorkbookModel()
    {
        var selectedHelixVersions = HelixVersions
            .Where(helix => helix.Modules.Any(module => module.IsSelected))
            .Select(helix => helix.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedHelixVersions.Count == 0 && !string.IsNullOrWhiteSpace(SelectedVersion?.Version))
        {
            selectedHelixVersions.Add(SelectedVersion.Version);
        }

        var includedCustomTabNames = CustomTabs
            .Where(tab => tab.GetSelectedFiles().Count > 0)
            .Select(tab => tab.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (includedCustomTabNames.Count == 0 && !string.IsNullOrWhiteSpace(SelectedCustomTab?.Name))
        {
            includedCustomTabNames.Add(SelectedCustomTab.Name);
        }

        return new BulkSelectionWorkbookModel
        {
            TemplateVersion = "1.0",
            CompanyName = CompanyName?.Trim() ?? string.Empty,
            SelectedHelixVersion = SelectedVersion?.Version ?? string.Empty,
            SelectedHelixVersions = selectedHelixVersions,
            SearchText = SearchText ?? string.Empty,
            MemoText = MemoText ?? string.Empty,
            OutputBaseFolder = OutputBaseFolder?.Trim() ?? string.Empty,
            MaxConcurrentTransfers = MaxConcurrentTransfers,
            SelectedCustomTabName = SelectedCustomTab?.Name ?? string.Empty,
            IncludedCustomTabNames = includedCustomTabNames,
            ModuleSelections = HelixVersions
                .SelectMany(helix => helix.Modules.Select(module => new BulkModuleSelectionRow
                {
                    IsSelected = module.IsSelected,
                    HelixVersion = helix.Version,
                    Code = module.Code,
                    Name = module.Name,
                    CompatibilityVersion = module.ModuleVersionDisplay,
                    SupportedOsDisplay = module.OsDisplay,
                    OsSelection = module.OsSelection,
                    SupportStatus = module.SupportText,
                    SelectedInstallerVersion = module.SelectedInstallerVersion,
                    InstallerVersionOptions = module.InstallerVersionOptions
                        .Where(version => !string.IsNullOrWhiteSpace(version))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }))
                .ToList(),
            ScanSelections = ScanSelectionItems
                .Select(item => new BulkScanSelectionRow
                {
                    IsSelected = item.IsSelected,
                    SourcePath = item.SourcePath,
                    Code = item.Code,
                    Version = item.Version,
                    Os = item.Os.ToString()
                })
                .ToList(),
            CustomTabStates = CloneCustomTabStates(BuildCustomTabStates()),
            CustomZipPlans = CloneCustomZipPlans(GetCustomZipPlans())
        };
    }

    private void ApplyImportedBulkSelection(BulkSelectionWorkbookModel imported)
    {
        _isApplyingSelectionHistory = true;
        try
        {
            if (imported.HasBasicInfoSection)
            {
                CompanyName = imported.CompanyName ?? string.Empty;
                SearchText = imported.SearchText ?? string.Empty;
                MemoText = imported.MemoText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(imported.OutputBaseFolder))
                {
                    OutputBaseFolder = imported.OutputBaseFolder.Trim();
                }

                if (imported.MaxConcurrentTransfers > 0)
                {
                    MaxConcurrentTransfers = imported.MaxConcurrentTransfers;
                }
            }

            if (imported.HasModuleSelectionSection)
            {
                ClearModulesForImport();
            }

            if (imported.HasScanSelectionSection)
            {
                ClearScanItemsForImport();
            }

            if (imported.HasCustomTabsSection)
            {
                Settings.CustomTabStates = CloneCustomTabStates(imported.CustomTabStates ?? new List<CustomTabState>());
                Settings.SelectedCustomTabName = imported.SelectedCustomTabName?.Trim() ?? string.Empty;
                if (imported.HasCustomZipPlansSection)
                {
                    Settings.CustomZipPlans = CloneCustomZipPlans(imported.CustomZipPlans ?? new List<CustomZipPlan>());
                }
                else
                {
                    Settings.CustomZipPlans = new List<CustomZipPlan>();
                }

                RestoreCustomStateFromSettings();
            }
            else if (imported.HasCustomZipPlansSection)
            {
                SetCustomZipPlans(CloneCustomZipPlans(imported.CustomZipPlans ?? new List<CustomZipPlan>()));
            }

            var missingModules = 0;
            if (imported.HasModuleSelectionSection)
            {
                missingModules = ApplyImportedModuleSelections(imported.ModuleSelections ?? new List<BulkModuleSelectionRow>());
            }

            var missingScanItems = 0;
            if (imported.HasScanSelectionSection)
            {
                missingScanItems = ApplyImportedScanSelections(imported.ScanSelections ?? new List<BulkScanSelectionRow>());
            }

            var preferredHelix = imported.SelectedHelixVersions
                .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
                ?? imported.SelectedHelixVersion;
            if (!string.IsNullOrWhiteSpace(preferredHelix))
            {
                SelectedVersion = HelixVersions.FirstOrDefault(helix =>
                                     helix.Version.Equals(preferredHelix, StringComparison.OrdinalIgnoreCase))
                                 ?? HelixVersions.FirstOrDefault(helix =>
                                     NormalizeHelixVersionLabel(helix.Version)
                                         .Equals(NormalizeHelixVersionLabel(preferredHelix), StringComparison.OrdinalIgnoreCase))
                                 ?? SelectedVersion;
            }

            _uploadListUserEdited = false;
            QuickRequestResult = string.Empty;
            UnresolvedTerms.Clear();
            AmbiguousTerms.Clear();
            UnresolvedMemoText = string.Empty;
            UpdateBasket();
            PersistCustomState();
            _settingsService.Save(Settings);

            if (missingModules > 0 || missingScanItems > 0)
            {
                WpfMessageBox.Show(
                    $"取込は完了しましたが、一部が未反映です。モジュール未反映: {missingModules} 件 / スキャン未反映: {missingScanItems} 件",
                    "Import情報",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            _isApplyingSelectionHistory = false;
        }
    }

    private int ApplyImportedModuleSelections(IEnumerable<BulkModuleSelectionRow> rows)
    {
        var missing = 0;
        var selectedRows = rows.Where(row => row.IsSelected).ToList();
        foreach (var row in selectedRows)
        {
            var helix = HelixVersions.FirstOrDefault(item =>
                NormalizeHelixVersionLabel(item.Version)
                    .Equals(NormalizeHelixVersionLabel(row.HelixVersion), StringComparison.OrdinalIgnoreCase));
            if (helix == null)
            {
                missing++;
                continue;
            }

            var module = helix.Modules.FirstOrDefault(item =>
                item.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
            if (module == null || !module.IsEnabled)
            {
                missing++;
                continue;
            }

            module.SetOsSelectionSilently(NormalizeOsSelectionForRestore(row.OsSelection, module));
            if (!string.IsNullOrWhiteSpace(row.SelectedInstallerVersion) &&
                module.InstallerVersionOptions.Any(version =>
                    version.Equals(row.SelectedInstallerVersion, StringComparison.OrdinalIgnoreCase)))
            {
                module.SetSelectedInstallerVersionSilently(row.SelectedInstallerVersion);
            }

            module.SetSelectedSilently(true);
        }

        return missing;
    }

    private int ApplyImportedScanSelections(IEnumerable<BulkScanSelectionRow> rows)
    {
        var selectedStates = rows
            .Where(row => row.IsSelected)
            .Select(row => new SelectionScanState
            {
                SourcePath = row.SourcePath ?? string.Empty,
                Code = row.Code ?? string.Empty,
                Version = row.Version ?? string.Empty,
                Os = row.Os ?? string.Empty
            })
            .ToList();
        var selected = ResolveSelectedScanItems(selectedStates);
        WithSelectionSyncSuppressed(() =>
        {
            foreach (var item in ScanSelectionItems)
            {
                item.IsSelected = selected.Contains(item);
            }
        });

        return Math.Max(0, selectedStates.Count - selected.Count);
    }

    private void ClearModulesForImport()
    {
        WithSelectionSyncSuppressed(() =>
        {
            foreach (var helix in HelixVersions)
            {
                foreach (var module in helix.Modules)
                {
                    module.SetSelectedSilently(false);
                }
            }
        });

        ClearManualPicks();
    }

    private void ClearScanItemsForImport()
    {
        WithSelectionSyncSuppressed(() =>
        {
            foreach (var item in ScanSelectionItems)
            {
                item.IsSelected = false;
            }
        });
    }

    private static List<CustomTabState> CloneCustomTabStates(IEnumerable<CustomTabState> states)
    {
        var result = new List<CustomTabState>();
        foreach (var state in states)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Name))
            {
                continue;
            }

            var clonedRows = new List<CustomTabRowState>();
            foreach (var row in state.Rows ?? new List<CustomTabRowState>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.SourcePath))
                {
                    continue;
                }

                clonedRows.Add(new CustomTabRowState
                {
                    IsSelected = row.IsSelected,
                    Folder = row.Folder ?? string.Empty,
                    FileName = row.FileName ?? string.Empty,
                    SourcePath = row.SourcePath ?? string.Empty,
                    ColumnValues = new Dictionary<string, string>(
                        row.ColumnValues ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase)
                });
            }

            result.Add(new CustomTabState
            {
                Name = state.Name.Trim(),
                ColumnsInput = state.ColumnsInput ?? string.Empty,
                NewDirectoryPath = state.NewDirectoryPath ?? string.Empty,
                Rows = clonedRows
            });
        }

        return result;
    }

    private static List<CustomZipPlan> CloneCustomZipPlans(IEnumerable<CustomZipPlan> plans)
    {
        return plans
            .Where(plan => !string.IsNullOrWhiteSpace(plan.TabName) && !string.IsNullOrWhiteSpace(plan.ArchiveBaseName))
            .Select(plan => new CustomZipPlan(
                plan.TabName.Trim(),
                plan.ArchiveBaseName.Trim(),
                plan.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath) && !string.IsNullOrWhiteSpace(item.FileName))
                    .Select(item => new CustomZipPlanItem(
                        item.SourcePath,
                        item.FolderName,
                        item.FileName,
                        item.IncludeFolderInArchive))
                    .ToList()))
            .Where(plan => plan.Items.Count > 0)
            .ToList();
    }

    private void LoadSelectionStateHistoryFromSettings()
    {
        var normalized = (Settings.SelectionStateHistory ?? new List<SelectionStateHistoryEntry>())
            .Where(entry => entry != null)
            .OrderByDescending(entry => entry.SavedAtUtc)
            .Take(SelectionHistoryLimit)
            .ToList();

        foreach (var entry in normalized)
        {
            entry.DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? BuildSelectionStateDisplayName(entry)
                : entry.DisplayName;
            entry.SelectedModules ??= new List<SelectionModuleState>();
            entry.SelectedScanItems ??= new List<SelectionScanState>();
            entry.SelectedCustomTabs ??= new List<SelectionCustomTabState>();
            entry.CustomZipPlans ??= new List<CustomZipPlan>();
        }

        Settings.SelectionStateHistory = normalized;

        SelectionStateHistoryEntries.Clear();
        foreach (var entry in normalized)
        {
            SelectionStateHistoryEntries.Add(entry);
        }

        if (SelectedSelectionStateHistoryEntry != null)
        {
            var restored = SelectionStateHistoryEntries.FirstOrDefault(item =>
                item.SavedAtUtc == SelectedSelectionStateHistoryEntry.SavedAtUtc &&
                item.DisplayName.Equals(SelectedSelectionStateHistoryEntry.DisplayName, StringComparison.Ordinal));
            if (restored != null)
            {
                SelectedSelectionStateHistoryEntry = restored;
                return;
            }
        }

        SelectedSelectionStateHistoryEntry = SelectionStateHistoryEntries.FirstOrDefault();
    }

    private void SaveCurrentSelectionStateToHistory()
    {
        var snapshot = CreateCurrentSelectionStateHistoryEntry();
        var history = Settings.SelectionStateHistory ?? new List<SelectionStateHistoryEntry>();
        history.Insert(0, snapshot);
        if (history.Count > SelectionHistoryLimit)
        {
            history.RemoveRange(SelectionHistoryLimit, history.Count - SelectionHistoryLimit);
        }

        Settings.SelectionStateHistory = history;
        _settingsService.Save(Settings);
        LoadSelectionStateHistoryFromSettings();
    }

    private SelectionStateHistoryEntry CreateCurrentSelectionStateHistoryEntry()
    {
        var selectedModules = HelixVersions
            .SelectMany(helix => helix.Modules
                .Where(module => module.IsSelected)
                .Select(module => new SelectionModuleState
                {
                    HelixVersion = helix.Version,
                    Code = module.Code,
                    OsSelection = module.OsSelection,
                    SelectedInstallerVersion = module.SelectedInstallerVersion
                }))
            .ToList();

        var selectedScanItems = ScanSelectionItems
            .Where(item => item.IsSelected)
            .Select(item => new SelectionScanState
            {
                SourcePath = item.SourcePath,
                Code = item.Code,
                Version = item.Version,
                Os = item.Os.ToString()
            })
            .ToList();

        var selectedCustomTabs = CustomTabs
            .Select(tab => new SelectionCustomTabState
            {
                TabName = tab.Name,
                SelectedSourcePaths = tab.GetSelectedFiles()
                    .Select(file => file.SourcePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(tab => tab.SelectedSourcePaths.Count > 0)
            .ToList();

        var snapshot = new SelectionStateHistoryEntry
        {
            SavedAtUtc = DateTime.UtcNow,
            CompanyName = CompanyName?.Trim() ?? string.Empty,
            MemoText = MemoText ?? string.Empty,
            SearchText = SearchText ?? string.Empty,
            SelectedVersion = SelectedVersion?.Version ?? string.Empty,
            SelectedModules = selectedModules,
            SelectedScanItems = selectedScanItems,
            SelectedCustomTabs = selectedCustomTabs,
            CustomZipPlans = GetCustomZipPlans().ToList()
        };
        snapshot.DisplayName = BuildSelectionStateDisplayName(snapshot);
        return snapshot;
    }

    private static string BuildSelectionStateDisplayName(SelectionStateHistoryEntry entry)
    {
        var localTime = entry.SavedAtUtc == default
            ? DateTime.Now
            : entry.SavedAtUtc.ToLocalTime();
        var company = string.IsNullOrWhiteSpace(entry.CompanyName) ? "会社名未設定" : entry.CompanyName;
        var version = string.IsNullOrWhiteSpace(entry.SelectedVersion) ? "バージョン未選択" : entry.SelectedVersion;
        return $"{localTime:MM/dd HH:mm} | {company} | {version}";
    }

    private void ApplySelectionStateHistoryCore(SelectionStateHistoryEntry entry)
    {
        _isApplyingSelectionHistory = true;
        try
        {
            ResetSelectionStateCore(clearContextInputs: false);

            SearchText = entry.SearchText ?? string.Empty;
            CompanyName = entry.CompanyName ?? string.Empty;
            MemoText = entry.MemoText ?? string.Empty;
            QuickRequestResult = string.Empty;
            UnresolvedTerms.Clear();
            AmbiguousTerms.Clear();
            UnresolvedMemoText = string.Empty;

            if (!string.IsNullOrWhiteSpace(entry.SelectedVersion))
            {
                SelectedVersion = HelixVersions.FirstOrDefault(helix =>
                                     helix.Version.Equals(entry.SelectedVersion, StringComparison.OrdinalIgnoreCase))
                                 ?? HelixVersions.FirstOrDefault(helix =>
                                     NormalizeHelixVersionLabel(helix.Version)
                                         .Equals(NormalizeHelixVersionLabel(entry.SelectedVersion), StringComparison.OrdinalIgnoreCase));
            }

            foreach (var state in entry.SelectedModules ?? new List<SelectionModuleState>())
            {
                var helix = HelixVersions.FirstOrDefault(item =>
                    NormalizeHelixVersionLabel(item.Version)
                        .Equals(NormalizeHelixVersionLabel(state.HelixVersion), StringComparison.OrdinalIgnoreCase));
                if (helix == null)
                {
                    continue;
                }

                var module = helix.Modules.FirstOrDefault(item =>
                    item.Code.Equals(state.Code, StringComparison.OrdinalIgnoreCase));
                if (module == null || !module.IsEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(state.SelectedInstallerVersion) &&
                    module.InstallerVersionOptions.Any(version =>
                        version.Equals(state.SelectedInstallerVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    module.SetSelectedInstallerVersionSilently(state.SelectedInstallerVersion);
                }

                module.SetOsSelectionSilently(NormalizeOsSelectionForRestore(state.OsSelection, module));
                module.SetSelectedSilently(true);
            }

            var selectedScanItems = ResolveSelectedScanItems(entry.SelectedScanItems ?? new List<SelectionScanState>());
            WithSelectionSyncSuppressed(() =>
            {
                foreach (var item in ScanSelectionItems)
                {
                    item.IsSelected = selectedScanItems.Contains(item);
                }
            });

            var selectedPathsByTab = (entry.SelectedCustomTabs ?? new List<SelectionCustomTabState>())
                .Where(tab => !string.IsNullOrWhiteSpace(tab.TabName))
                .ToDictionary(
                    tab => tab.TabName,
                    tab => tab.SelectedSourcePaths ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var tab in CustomTabs)
            {
                if (selectedPathsByTab.TryGetValue(tab.Name, out var selectedPaths))
                {
                    tab.SetSelectedByPaths(selectedPaths);
                }
                else
                {
                    tab.ClearSelection();
                }
            }

            SetCustomZipPlans(entry.CustomZipPlans ?? new List<CustomZipPlan>());
            _uploadListUserEdited = false;
            UpdateBasket();
            PersistCustomState();
        }
        finally
        {
            _isApplyingSelectionHistory = false;
        }
    }

    private void ResetSelectionStateCore(bool clearContextInputs = true)
    {
        _isApplyingSelectionHistory = true;
        try
        {
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

            foreach (var tab in CustomTabs)
            {
                tab.ClearSelection();
            }

            _redownloadUnlockedDestinationPaths.Clear();
            _customZipPlans.Clear();
            ClearManualPicks();
            UnresolvedTerms.Clear();
            AmbiguousTerms.Clear();
            UnresolvedMemoText = string.Empty;
            QuickRequestResult = string.Empty;

            if (clearContextInputs)
            {
                SearchText = string.Empty;
                MemoText = string.Empty;
                CompanyName = string.Empty;
            }

            _uploadListUserEdited = false;
            UploadListText = string.Empty;
            UpdateBasket();
            PersistCustomState();
        }
        finally
        {
            _isApplyingSelectionHistory = false;
        }
    }

    private static string NormalizeOsSelectionForRestore(string osSelection, ModuleRowViewModel module)
    {
        if (osSelection.Equals(ModuleRowViewModel.OsSelectionWindows, StringComparison.OrdinalIgnoreCase))
        {
            return ModuleRowViewModel.OsSelectionWindows;
        }

        if (osSelection.Equals(ModuleRowViewModel.OsSelectionLinux, StringComparison.OrdinalIgnoreCase))
        {
            return ModuleRowViewModel.OsSelectionLinux;
        }

        if (osSelection.Equals(ModuleRowViewModel.OsSelectionBoth, StringComparison.OrdinalIgnoreCase))
        {
            return ModuleRowViewModel.OsSelectionBoth;
        }

        return GetDefaultOsSelection(GetAvailableOsTypesFromDisplay(module.OsDisplay));
    }

    private HashSet<ScanSelectionItemViewModel> ResolveSelectedScanItems(IEnumerable<SelectionScanState> states)
    {
        var selected = new HashSet<ScanSelectionItemViewModel>();
        foreach (var state in states)
        {
            var match = !string.IsNullOrWhiteSpace(state.SourcePath)
                ? ScanSelectionItems.FirstOrDefault(item =>
                    item.SourcePath.Equals(state.SourcePath, StringComparison.OrdinalIgnoreCase))
                : null;

            match ??= ScanSelectionItems.FirstOrDefault(item =>
                item.Code.Equals(state.Code, StringComparison.OrdinalIgnoreCase) &&
                item.Version.Equals(state.Version, StringComparison.OrdinalIgnoreCase) &&
                item.Os.ToString().Equals(state.Os, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                selected.Add(match);
            }
        }

        return selected;
    }

    private void RestoreCustomStateFromSettings()
    {
        _isRestoringCustomState = true;
        try
        {
            foreach (var existing in CustomTabs)
            {
                existing.Changed -= OnCustomTabChanged;
            }

            CustomTabs.Clear();
            _customZipPlans.Clear();

            var customTabStates = Settings.CustomTabStates ?? new List<CustomTabState>();
            foreach (var state in customTabStates)
            {
                if (string.IsNullOrWhiteSpace(state.Name))
                {
                    continue;
                }

                var tab = new CustomTabViewModel(state.Name.Trim(), ParseCustomColumnNames(state.ColumnsInput))
                {
                    NewDirectoryPath = state.NewDirectoryPath ?? string.Empty
                };

                RestoreCustomTabRows(tab, state.Rows ?? new List<CustomTabRowState>());
                tab.Changed += OnCustomTabChanged;
                CustomTabs.Add(tab);
            }

            var customZipPlans = Settings.CustomZipPlans ?? new List<CustomZipPlan>();
            if (customZipPlans.Count > 0)
            {
                SetCustomZipPlans(customZipPlans);
            }

            SelectedCustomTab = CustomTabs
                .FirstOrDefault(tab => tab.Name.Equals(Settings.SelectedCustomTabName, StringComparison.OrdinalIgnoreCase))
                ?? CustomTabs.FirstOrDefault();
        }
        finally
        {
            _isRestoringCustomState = false;
        }
    }

    private void PersistCustomState()
    {
        if (_isRestoringCustomState)
        {
            return;
        }

        try
        {
            Settings.SelectedCustomTabName = SelectedCustomTab?.Name ?? string.Empty;
            Settings.CustomTabStates = BuildCustomTabStates();
            Settings.CustomZipPlans = GetCustomZipPlans().ToList();
            _settingsService.Save(Settings);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("カスタムタブ設定の保存に失敗しました。", ex);
        }
    }

    private List<CustomTabState> BuildCustomTabStates()
    {
        var states = new List<CustomTabState>();
        foreach (var tab in CustomTabs)
        {
            states.Add(new CustomTabState
            {
                Name = tab.Name,
                ColumnsInput = tab.ColumnsInput,
                NewDirectoryPath = tab.NewDirectoryPath,
                Rows = BuildCustomTabRowStates(tab)
            });
        }

        return states;
    }

    private static List<CustomTabRowState> BuildCustomTabRowStates(CustomTabViewModel tab)
    {
        var rows = new List<CustomTabRowState>();
        var table = tab.RowsView.Table;
        if (table == null)
        {
            return rows;
        }

        foreach (DataRow row in table.Rows)
        {
            var sourcePath = ToStringValue(row[CustomTabViewModel.SourcePathColumnName]);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            rows.Add(new CustomTabRowState
            {
                IsSelected = ToBoolValue(row[CustomTabViewModel.SelectColumnName]),
                Folder = ToStringValue(row[CustomTabViewModel.FolderColumnName]),
                FileName = ToStringValue(row[CustomTabViewModel.FileNameColumnName]),
                SourcePath = sourcePath,
                ColumnValues = GetCustomColumnValues(row)
            });
        }

        return rows;
    }

    private static void RestoreCustomTabRows(CustomTabViewModel tab, IEnumerable<CustomTabRowState> rows)
    {
        foreach (var state in rows)
        {
            if (string.IsNullOrWhiteSpace(state.SourcePath))
            {
                continue;
            }

            tab.AddFile(state.SourcePath, false);

            var table = tab.RowsView.Table;
            var row = FindCustomTabRowBySourcePath(table, state.SourcePath);
            if (row == null)
            {
                continue;
            }

            row[CustomTabViewModel.SelectColumnName] = state.IsSelected;
            row[CustomTabViewModel.FolderColumnName] = string.IsNullOrWhiteSpace(state.Folder)
                ? GetNearestFolderName(state.SourcePath)
                : state.Folder;
            row[CustomTabViewModel.FileNameColumnName] = string.IsNullOrWhiteSpace(state.FileName)
                ? Path.GetFileName(state.SourcePath)
                : state.FileName;

            foreach (var pair in state.ColumnValues ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !row.Table.Columns.Contains(pair.Key))
                {
                    continue;
                }

                row[pair.Key] = pair.Value ?? string.Empty;
            }
        }
    }

    private static DataRow? FindCustomTabRowBySourcePath(DataTable? table, string sourcePath)
    {
        if (table == null)
        {
            return null;
        }

        foreach (DataRow row in table.Rows)
        {
            var value = ToStringValue(row[CustomTabViewModel.SourcePathColumnName]);
            if (value.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private static Dictionary<string, string> GetCustomColumnValues(DataRow row)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in row.Table.Columns)
        {
            if (IsCustomTabReservedColumn(column.ColumnName))
            {
                continue;
            }

            result[column.ColumnName] = ToStringValue(row[column]);
        }

        return result;
    }

    private static bool IsCustomTabReservedColumn(string columnName)
    {
        return columnName.Equals(CustomTabViewModel.SelectColumnName, StringComparison.OrdinalIgnoreCase)
               || columnName.Equals(CustomTabViewModel.FolderColumnName, StringComparison.OrdinalIgnoreCase)
               || columnName.Equals(CustomTabViewModel.FileNameColumnName, StringComparison.OrdinalIgnoreCase)
               || columnName.Equals(CustomTabViewModel.SourcePathColumnName, StringComparison.OrdinalIgnoreCase)
               || columnName.Equals(CustomTabViewModel.SelectionEnabledColumnName, StringComparison.OrdinalIgnoreCase)
               || columnName.Equals(CustomTabViewModel.SelectionLockReasonColumnName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToStringValue(object? value)
    {
        return value is null or DBNull ? string.Empty : value.ToString() ?? string.Empty;
    }

    private static bool ToBoolValue(object? value)
    {
        return value is bool b && b;
    }

    private static string GetNearestFolderName(string path)
    {
        var directory = Path.GetDirectoryName(path);
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

    private sealed class ModuleDownloadSelectionState
    {
        public int ResolvedAssetCount { get; set; }
        public int AlreadyDownloadedCount { get; set; }
    }

    private void UpdateBasket()
    {
        BasketItems.Clear();
        var selectedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleDownloadStates = new Dictionary<ModuleRowViewModel, ModuleDownloadSelectionState>();
        var customTabLockedPaths = new Dictionary<CustomTabViewModel, HashSet<string>>();
        ResetModuleDownloadLocks();

        if (HelixVersions.Count > 0)
        {
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var helix in HelixVersions)
            {
                var helixLabel = NormalizeHelixVersionLabel(helix.Version);
                foreach (var module in helix.Modules.Where(m => m.IsSelected))
                {
                    AddBasketItemsForModule(helixLabel, module, existingKeys, missingKeys, moduleDownloadStates);
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

                var destinationPath = TryBuildBasketDestinationPath(helixLabel, pick.Asset.FileName);
                var isAlreadyDownloaded = IsDestinationAlreadyDownloaded(destinationPath);
                var manualReason = string.IsNullOrWhiteSpace(pick.Reason)
                    ? string.Empty
                    : pick.Reason;
                if (isAlreadyDownloaded)
                {
                    manualReason = string.IsNullOrWhiteSpace(manualReason)
                        ? AlreadyDownloadedReason
                        : $"{manualReason} / {AlreadyDownloadedReason}";
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
                    manualReason,
                    true,
                    isAlreadyDownloaded,
                    destinationPath));
            }
        }
        else if (ScanSelectionItems.Count > 0)
        {
            foreach (var item in ScanSelectionItems.Where(item => item.IsSelected))
            {
                var destinationPath = TryBuildBasketDestinationPath(ScanOnlyVersionLabel, item.AssetFileName);
                var isAlreadyDownloaded = IsDestinationAlreadyDownloaded(destinationPath);
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
                    isAlreadyDownloaded ? AlreadyDownloadedReason : string.Empty,
                    false,
                    isAlreadyDownloaded,
                    destinationPath));
            }
        }

        foreach (var basketItem in BasketItems)
        {
            if (!string.IsNullOrWhiteSpace(basketItem.SourcePath))
            {
                selectedSourcePaths.Add(basketItem.SourcePath);
            }
        }

        AddCustomTabBasketItems(selectedSourcePaths, customTabLockedPaths);
        ApplyModuleDownloadLockStates(moduleDownloadStates);
        ApplyCustomTabDownloadLockStates(customTabLockedPaths);
        UpdateUploadListText();
        RefreshShipmentHistoryItems(keepCurrentSelection: true);
    }

    private void ResetModuleDownloadLocks()
    {
        foreach (var module in HelixVersions.SelectMany(helix => helix.Modules))
        {
            module.SetDownloadState(false, false, string.Empty);
        }
    }

    private void ApplyModuleDownloadLockStates(
        IReadOnlyDictionary<ModuleRowViewModel, ModuleDownloadSelectionState> moduleDownloadStates)
    {
        foreach (var module in HelixVersions.SelectMany(helix => helix.Modules))
        {
            if (!module.IsSelected)
            {
                module.SetDownloadState(false, false, string.Empty);
                continue;
            }

            if (!moduleDownloadStates.TryGetValue(module, out var state) || state.ResolvedAssetCount == 0)
            {
                module.SetDownloadState(false, false, string.Empty);
                continue;
            }

            var hasDownloaded = state.AlreadyDownloadedCount > 0;
            var shouldLock = state.AlreadyDownloadedCount >= state.ResolvedAssetCount;
            var reason = shouldLock
                ? AlreadyDownloadedReason
                : hasDownloaded
                    ? "一部が既にダウンロード済み"
                    : string.Empty;
            module.SetDownloadState(hasDownloaded, shouldLock, reason);
        }
    }

    private void AddCustomTabBasketItems(
        HashSet<string> selectedSourcePaths,
        IDictionary<CustomTabViewModel, HashSet<string>> customTabLockedPaths)
    {
        var rawZipPlans = GetCustomZipPlans()
            .Where(plan => !string.IsNullOrWhiteSpace(plan.TabName) && !string.IsNullOrWhiteSpace(plan.ArchiveBaseName))
            .ToList();
        var effectiveZipPlans = new List<CustomZipPlan>();
        var zippedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tab in CustomTabs)
        {
            if (!customTabLockedPaths.TryGetValue(tab, out var tabLockedPaths))
            {
                tabLockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                customTabLockedPaths[tab] = tabLockedPaths;
            }

            var selectedFiles = tab.GetSelectedFiles()
                .Where(file => !string.IsNullOrWhiteSpace(file.SourcePath))
                .ToList();
            var selectedFilePaths = selectedFiles
                .Select(file => file.SourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rawZipPlansForTab = rawZipPlans
                .Where(plan => plan.TabName.Equals(tab.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var rawZipPlan in rawZipPlansForTab)
            {
                var planItems = rawZipPlan.Items
                    .Where(item => selectedFilePaths.Contains(item.SourcePath))
                    .ToList();

                if (planItems.Count > 0)
                {
                    var zipPlan = rawZipPlan with { Items = planItems };
                    effectiveZipPlans.Add(zipPlan);
                    foreach (var item in planItems)
                    {
                        zippedSourcePaths.Add(item.SourcePath);
                    }

                    var zipFileName = $"{zipPlan.ArchiveBaseName}.zip";
                    var zipDestinationPath = string.IsNullOrWhiteSpace(OutputFolderPreview)
                        ? string.Empty
                        : Path.Combine(OutputFolderPreview, zipFileName);
                    var zipAlreadyDownloaded = IsDestinationAlreadyDownloaded(zipDestinationPath);
                    if (zipAlreadyDownloaded)
                    {
                        foreach (var item in planItems)
                        {
                            tabLockedPaths.Add(item.SourcePath);
                        }
                    }

                    var summarySourcePath = BuildCustomZipPlanSourcePath(zipPlan.TabName, zipPlan.ArchiveBaseName);
                    BasketItems.Add(new BasketItemViewModel(
                        $"{CustomTabLabelPrefix}{tab.Name}",
                        CustomZipSummaryCode,
                        $"圧縮フォルダ名:{zipPlan.ArchiveBaseName}",
                        "-",
                        "-",
                        "-",
                        zipFileName,
                        summarySourcePath,
                        false,
                        zipAlreadyDownloaded ? AlreadyDownloadedReason : "キュー追加時に自動圧縮",
                        true,
                        zipAlreadyDownloaded,
                        zipDestinationPath));
                }
            }

            foreach (var file in selectedFiles)
            {
                if (zippedSourcePaths.Contains(file.SourcePath))
                {
                    continue;
                }

                if (!selectedSourcePaths.Add(file.SourcePath))
                {
                    continue;
                }

                var exists = File.Exists(file.SourcePath);
                var customHelixLabel = $"{CustomTabLabelPrefix}{tab.Name}";
                var destinationPath = TryBuildBasketDestinationPath(customHelixLabel, file.FileName);
                var isAlreadyDownloaded = exists && IsDestinationAlreadyDownloaded(destinationPath);
                if (isAlreadyDownloaded)
                {
                    tabLockedPaths.Add(file.SourcePath);
                }

                var os = GuessOsFromPath(file.SourcePath).ToString();
                var details = string.Join(" / ", file.ColumnValues
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                    .Select(pair => $"{pair.Key}={pair.Value}"));
                var reason = exists ? details : "ファイルが存在しません";
                if (isAlreadyDownloaded)
                {
                    reason = string.IsNullOrWhiteSpace(reason)
                        ? AlreadyDownloadedReason
                        : $"{reason} / {AlreadyDownloadedReason}";
                }

                BasketItems.Add(new BasketItemViewModel(
                    customHelixLabel,
                    "CUSTOM",
                    file.FileName,
                    "-",
                    "-",
                    os,
                    file.FileName,
                    file.SourcePath,
                    !exists,
                    reason,
                    true,
                    isAlreadyDownloaded,
                    destinationPath));
            }
        }

        SetCustomZipPlans(effectiveZipPlans);
    }

    private void ApplyCustomTabDownloadLockStates(IDictionary<CustomTabViewModel, HashSet<string>> customTabLockedPaths)
    {
        foreach (var tab in CustomTabs)
        {
            if (customTabLockedPaths.TryGetValue(tab, out var lockedPaths))
            {
                tab.SetDownloadLockByPaths(lockedPaths, AlreadyDownloadedReason);
            }
            else
            {
                tab.SetDownloadLockByPaths(Array.Empty<string>(), string.Empty);
            }
        }
    }

    private void AddBasketItemsForModule(
        string helixVersion,
        ModuleRowViewModel module,
        HashSet<string> existingKeys,
        HashSet<string> missingKeys,
        IDictionary<ModuleRowViewModel, ModuleDownloadSelectionState> moduleDownloadStates)
    {
        var requestedVersion = GetRequestedVersion(module);
        var selectedOsTypes = GetSelectedOsTypes(module.OsSelection);
        if (selectedOsTypes.Count == 0)
        {
            selectedOsTypes = new List<OsType> { OsType.Windows, OsType.Linux };
        }

        var moduleState = GetOrCreateModuleDownloadSelectionState(moduleDownloadStates, module);

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

            var destinationPath = TryBuildBasketDestinationPath(helixVersion, asset.FileName);
            var isAlreadyDownloaded = IsDestinationAlreadyDownloaded(destinationPath);
            moduleState.ResolvedAssetCount++;
            if (isAlreadyDownloaded)
            {
                moduleState.AlreadyDownloadedCount++;
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
                isAlreadyDownloaded ? AlreadyDownloadedReason : string.Empty,
                false,
                isAlreadyDownloaded,
                destinationPath));
        }
    }

    private static ModuleDownloadSelectionState GetOrCreateModuleDownloadSelectionState(
        IDictionary<ModuleRowViewModel, ModuleDownloadSelectionState> states,
        ModuleRowViewModel module)
    {
        if (states.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new ModuleDownloadSelectionState();
        states[module] = state;
        return state;
    }

    private IEnumerable<string> ResolveDestinationPathsForBasketItem(BasketItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.DestinationPath))
        {
            yield return item.DestinationPath;
            yield break;
        }

        if (IsCustomZipSummaryItem(item))
        {
            if (!string.IsNullOrWhiteSpace(OutputFolderPreview) &&
                !string.IsNullOrWhiteSpace(item.AssetFileName))
            {
                yield return Path.Combine(OutputFolderPreview, item.AssetFileName);
            }

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(item.AssetFileName))
        {
            var path = TryBuildBasketDestinationPath(item.HelixVersion, item.AssetFileName);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> GetDestinationPathsForModuleSelection(string helixVersion, ModuleRowViewModel module)
    {
        var requestedVersion = GetRequestedVersion(module);
        var selectedOsTypes = GetSelectedOsTypes(module.OsSelection);
        if (selectedOsTypes.Count == 0)
        {
            selectedOsTypes = new List<OsType> { OsType.Windows, OsType.Linux };
        }

        foreach (var osType in selectedOsTypes)
        {
            var asset = FindLogicalAsset(module.Code, requestedVersion, osType, out _);
            if (asset == null)
            {
                continue;
            }

            var destinationPath = TryBuildBasketDestinationPath(helixVersion, asset.FileName);
            if (!string.IsNullOrWhiteSpace(destinationPath))
            {
                yield return destinationPath;
            }
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

    private bool TryRemoveCustomTabSelection(BasketItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return false;
        }

        foreach (var tab in CustomTabs)
        {
            if (!tab.HasFile(item.SourcePath))
            {
                continue;
            }

            if (tab.UnselectByPath(item.SourcePath))
            {
                UpdateBasket();
            }

            return true;
        }

        return false;
    }

    private bool TryRemoveCustomZipSummary(BasketItemViewModel item)
    {
        if (!IsCustomZipSummaryItem(item))
        {
            return false;
        }

        var removed = 0;
        if (TryParseCustomZipPlanSourcePath(item.SourcePath, out var tabName, out var archiveBaseName))
        {
            removed = _customZipPlans.RemoveAll(plan =>
                plan.TabName.Equals(tabName, StringComparison.OrdinalIgnoreCase) &&
                plan.ArchiveBaseName.Equals(archiveBaseName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            tabName = TryExtractCustomTabName(item.HelixVersion);
            if (!string.IsNullOrWhiteSpace(tabName))
            {
                removed = _customZipPlans.RemoveAll(plan =>
                    plan.TabName.Equals(tabName, StringComparison.OrdinalIgnoreCase) &&
                    plan.ArchiveBaseName.Equals(
                        Path.GetFileNameWithoutExtension(item.AssetFileName ?? string.Empty),
                        StringComparison.OrdinalIgnoreCase));
            }
        }

        if (removed > 0)
        {
            UpdateBasket();
            PersistCustomState();
            return true;
        }

        return false;
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

            if (moduleCodes.Any(existing =>
                    NormalizeModuleCode(existing).Equals(NormalizeModuleCode(code), StringComparison.OrdinalIgnoreCase)))
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

    private static bool RequiresStrictVersionMatch(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return IsHelixQacBundleCode(NormalizeModuleCode(code));
    }

    private static string GetSelectionTargetCode(string code)
    {
        var normalized = NormalizeModuleCode(code);
        return IsHelixQacBundleCode(normalized) ? HelixQacCode : normalized;
    }

    private static List<string> NormalizeModuleCodes(IEnumerable<string> codes)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedHelixQac = false;
        foreach (var rawCode in codes)
        {
            var code = NormalizeModuleCode(rawCode);
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

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

    private static string NormalizeModuleCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var trimmed = code.Trim();
        if (CanonicalModuleCodeMap.TryGetValue(trimmed, out var canonical))
        {
            return canonical;
        }

        return trimmed;
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

    private static string BuildHelixQacModuleVersionDisplay(HelixVersionData helix, string fallbackVersion)
    {
        var qacVersion = TryGetModuleVersion(helix, "QAC");
        var qacppVersion = TryGetModuleVersion(helix, "QAC++", "QACPP");
        var parts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(qacVersion))
        {
            parts.Add($"QAC({qacVersion})");
        }

        if (!string.IsNullOrWhiteSpace(qacppVersion))
        {
            parts.Add($"QAC++({qacppVersion})");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : fallbackVersion;
    }

    private static string? TryGetModuleVersion(HelixVersionData helix, params string[] codes)
    {
        foreach (var code in codes)
        {
            if (helix.ModuleSupport.TryGetValue(code, out var info) &&
                !string.IsNullOrWhiteSpace(info.ModuleVersion))
            {
                return info.ModuleVersion;
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
        return false;
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

    private static bool IsCustomZipSummaryItem(BasketItemViewModel item)
    {
        return item.Code.Equals(CustomZipSummaryCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCustomZipPlanStorageKey(string tabName, string archiveBaseName)
    {
        return $"{tabName.Trim()}\u001F{archiveBaseName.Trim()}";
    }

    private static bool TryParseCustomZipPlanStorageKey(string key, out string tabName, out string archiveBaseName)
    {
        tabName = string.Empty;
        archiveBaseName = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('\u001F');
        if (parts.Length != 2)
        {
            return false;
        }

        tabName = parts[0];
        archiveBaseName = parts[1];
        return !string.IsNullOrWhiteSpace(tabName) && !string.IsNullOrWhiteSpace(archiveBaseName);
    }

    private static string BuildCustomZipPlanSourcePath(string tabName, string archiveBaseName)
    {
        return $"customzip://{Uri.EscapeDataString(tabName.Trim())}/{Uri.EscapeDataString(archiveBaseName.Trim())}";
    }

    private static bool TryParseCustomZipPlanSourcePath(string? sourcePath, out string tabName, out string archiveBaseName)
    {
        tabName = string.Empty;
        archiveBaseName = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        if (!Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("customzip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var unescapedHost = Uri.UnescapeDataString(uri.Host ?? string.Empty);
        var path = uri.AbsolutePath.Trim('/');
        var unescapedPath = Uri.UnescapeDataString(path);
        if (string.IsNullOrWhiteSpace(unescapedHost) || string.IsNullOrWhiteSpace(unescapedPath))
        {
            return false;
        }

        tabName = unescapedHost;
        archiveBaseName = unescapedPath;
        return true;
    }

    private static string? TryExtractCustomTabName(string helixLabel)
    {
        if (string.IsNullOrWhiteSpace(helixLabel))
        {
            return null;
        }

        if (!helixLabel.StartsWith(CustomTabLabelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = helixLabel[CustomTabLabelPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string GetSafeVersionFolderName(string version)
    {
        var label = NormalizeHelixVersionLabel(version);
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(label.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? ScanOnlyVersionLabel : sanitized;
    }

    private string TryBuildBasketDestinationPath(string helixVersion, string fileName)
    {
        if (string.IsNullOrWhiteSpace(OutputFolderPreview) || string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var versionFolder = GetSafeVersionFolderName(helixVersion);
        return Path.Combine(OutputFolderPreview, versionFolder, fileName);
    }

    private bool IsDestinationAlreadyDownloaded(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return false;
        }

        if (_redownloadUnlockedDestinationPaths.Contains(destinationPath))
        {
            return false;
        }

        return File.Exists(destinationPath);
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

        return NormalizeModuleCodes(codes);
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
        if (!IsInstallerVersionSelectable(module.Code) || !module.IsSupported)
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

        if (!string.IsNullOrWhiteSpace(module.ModuleVersion) &&
            !versions.Contains(module.ModuleVersion, StringComparer.OrdinalIgnoreCase))
        {
            versions.Add(module.ModuleVersion);
        }

        if (versions.Count == 0)
        {
            module.SetInstallerVersionOptions(Array.Empty<string>());
            return;
        }

        versions.Sort((left, right) => VersionUtil.CompareVersionLike(right, left));
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
        {
            var preferredIndex = versions.FindIndex(version =>
                version.Equals(module.ModuleVersion, StringComparison.OrdinalIgnoreCase));
            if (preferredIndex > 0)
            {
                var preferred = versions[preferredIndex];
                versions.RemoveAt(preferredIndex);
                versions.Insert(0, preferred);
            }
        }

        module.SetInstallerVersionOptions(versions);
    }

    private static string GetDefaultDashboardModuleVersion(string helixVersion)
    {
        if (VersionUtil.IsAtLeast(helixVersion, "2023.3"))
        {
            return "2023.3-J";
        }

        if (VersionUtil.IsAtLeast(helixVersion, "2023.2"))
        {
            return "2023.2-J";
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetModuleCodeCandidates(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string TrimCode(string value) => value.Trim();

        void AddCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = TrimCode(candidate);
            if (seen.Add(trimmed))
            {
                // local function can't yield directly
            }
        }

        var rawCode = TrimCode(code);
        AddCandidate(rawCode);
        var normalizedCode = NormalizeModuleCode(rawCode);
        AddCandidate(normalizedCode);

        if (ModuleCodeAliases.TryGetValue(rawCode, out var rawAliases))
        {
            foreach (var alias in rawAliases)
            {
                AddCandidate(alias);
                AddCandidate(NormalizeModuleCode(alias));
            }
        }

        if (!rawCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase) &&
            ModuleCodeAliases.TryGetValue(normalizedCode, out var normalizedAliases))
        {
            foreach (var alias in normalizedAliases)
            {
                AddCandidate(alias);
                AddCandidate(NormalizeModuleCode(alias));
            }
        }

        foreach (var candidate in seen)
        {
            yield return candidate;
        }
    }

    private static ModuleSupportInfo? GetModuleSupportInfo(HelixVersionData helix, string code)
    {
        foreach (var candidate in GetModuleCodeCandidates(code))
        {
            if (helix.ModuleSupport.TryGetValue(candidate, out var info))
            {
                return info;
            }
        }

        return null;
    }

    private static bool IsDefaultSupportedWhenMissing(string code)
    {
        if (code.Equals("DASHBOARD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (code.Equals("VALIDATE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ExtraModuleCodes.Contains(code);
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

        if (_suppressSelectionSync)
        {
            return;
        }

        if (SelectedVersion == null)
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

    private InstallerAsset? CreateAssetFromBasketItem(BasketItemViewModel basketItem)
    {
        if (string.IsNullOrWhiteSpace(basketItem.SourcePath))
        {
            return null;
        }

        if (!File.Exists(basketItem.SourcePath))
        {
            return null;
        }

        var info = new FileInfo(basketItem.SourcePath);
        var version = !string.IsNullOrWhiteSpace(basketItem.InstallerVersion) && basketItem.InstallerVersion != "-"
            ? basketItem.InstallerVersion
            : !string.IsNullOrWhiteSpace(basketItem.ModuleVersion) && basketItem.ModuleVersion != "-"
                ? basketItem.ModuleVersion
                : string.Empty;
        var os = TryParseOsType(basketItem.Os, out var parsedOs)
            ? parsedOs
            : GuessOsFromPath(basketItem.SourcePath);

        return new InstallerAsset
        {
            SourcePath = basketItem.SourcePath,
            Size = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Os = os,
            IsZip = Path.GetExtension(info.Name).Equals(".zip", StringComparison.OrdinalIgnoreCase),
            Code = string.IsNullOrWhiteSpace(basketItem.Code) ? "CUSTOM" : basketItem.Code,
            Version = version
        };
    }

    private static OsType GuessOsFromPath(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".run", StringComparison.OrdinalIgnoreCase))
        {
            return OsType.Linux;
        }

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return OsType.Windows;
        }

        return OsType.Unknown;
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

        var targets = BuildVersionRequestTargets(knownCodes);
        if (targets.Count == 0)
        {
            return requests;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            foreach (var target in targets)
            {
                var searchStart = 0;
                while (searchStart < normalizedLine.Length)
                {
                    var index = normalizedLine.IndexOf(target.Token, searchStart, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        break;
                    }

                    var versionStart = index + target.Token.Length;
                    var allowsAttachedVersion =
                        versionStart < normalizedLine.Length &&
                        char.IsDigit(normalizedLine[versionStart]);
                    if (!IsVersionTargetBoundary(normalizedLine, index, target.Token.Length) && !allowsAttachedVersion)
                    {
                        searchStart = index + target.Token.Length;
                        continue;
                    }

                    var match = VersionRegex.Match(normalizedLine, versionStart);
                    if (match.Success)
                    {
                        var version = match.Value;
                        var requestCode = NormalizeVersionRequestCode(
                            normalizedLine,
                            target.CanonicalCode,
                            index,
                            target.Token.Length,
                            version);
                        var key = $"{requestCode}|{version}";
                        if (!seen.Add(key))
                        {
                            if (requestIndex.TryGetValue(key, out var existingIndex))
                            {
                                var existing = requests[existingIndex];
                                var merged = MergeRequestedOs(existing.OsSelection, requestedOs);
                                requests[existingIndex] = existing with { OsSelection = merged };
                            }
                        }
                        else
                        {
                            requestIndex[key] = requests.Count;
                            requests.Add(new VersionedRequest(requestCode, version, requestedOs));
                        }
                    }

                    searchStart = index + target.Token.Length;
                }
            }
        }

        return requests;
    }

    private static List<VersionRequestTarget> BuildVersionRequestTargets(IReadOnlyCollection<string> knownCodes)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static void AddTarget(IDictionary<string, string> map, string token, string canonicalCode)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(canonicalCode))
            {
                return;
            }

            var trimmedToken = token.Trim();
            var trimmedCode = canonicalCode.Trim();
            if (trimmedToken.Length == 0 || trimmedCode.Length == 0)
            {
                return;
            }

            map[trimmedToken] = trimmedCode;
        }

        foreach (var code in knownCodes.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalizedCode = NormalizeModuleCode(code);
            AddTarget(targets, code, normalizedCode);
            foreach (var candidate in GetModuleCodeCandidates(code))
            {
                AddTarget(targets, candidate, normalizedCode);
            }

            if (!IsHelixQacBundleCode(normalizedCode))
            {
                continue;
            }

            AddTarget(targets, HelixQacCode, HelixQacCode);
            AddTarget(targets, "Helix", "Helix");
            AddTarget(targets, "QAC", "QAC");
            AddTarget(targets, "QAC++", "QAC++");
            AddTarget(targets, "QACPP", "QACPP");
        }

        return targets
            .Select(pair => new VersionRequestTarget(pair.Key, pair.Value))
            .OrderByDescending(item => item.Token.Length)
            .ToList();
    }

    private static bool IsVersionTargetBoundary(string text, int startIndex, int length)
    {
        if (startIndex > 0)
        {
            var before = text[startIndex - 1];
            if (IsAsciiModuleTokenChar(before))
            {
                return false;
            }
        }

        var afterIndex = startIndex + length;
        if (afterIndex < text.Length)
        {
            var after = text[afterIndex];
            if (IsAsciiModuleTokenChar(after))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiModuleTokenChar(char ch)
    {
        return (ch >= 'A' && ch <= 'Z')
               || (ch >= 'a' && ch <= 'z')
               || (ch >= '0' && ch <= '9')
               || ch == '+'
               || ch == '_';
    }

    private static string NormalizeVersionRequestCode(
        string line,
        string canonicalCode,
        int tokenStartIndex,
        int tokenLength,
        string version)
    {
        // "Helix QAC 2021.3" のような表現は Helix版数指定として統一する。
        if (IsLikelyHelixVersionToken(version) &&
            HasHelixKeywordNearToken(line, tokenStartIndex, tokenLength) &&
            IsHelixQacBundleCode(canonicalCode))
        {
            return "Helix";
        }

        return canonicalCode;
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

    private bool IsLocalLlmDecisionEnabled()
    {
        return string.Equals(Settings.AiDecisionMode, AiModeLocalLlm, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRunLocalLlm(
        IReadOnlyCollection<VersionedRequest> versionedRequests,
        MemoParseResult parseResult,
        RequestedOs defaultRequestedOs,
        int osEvidenceCount,
        string? companyName)
    {
        if (!IsLocalLlmDecisionEnabled())
        {
            return false;
        }

        var hasCompany = !string.IsNullOrWhiteSpace(companyName);
        var hasVersionedRequests = versionedRequests.Count > 0;
        var hasMatchedCodes = parseResult.MatchedCodes.Count > 0;
        var hasAmbiguousTerms = parseResult.AmbiguousMatches.Count > 0;
        var unresolvedCount = parseResult.UnresolvedTerms.Count;

        // Strong baseline: when company + version requests are already extracted, prefer deterministic rules.
        if (hasCompany &&
            hasVersionedRequests &&
            (defaultRequestedOs != RequestedOs.Unspecified || osEvidenceCount == 0))
        {
            return false;
        }

        var noStructuredExtraction = !hasVersionedRequests && !hasMatchedCodes;
        var missingEssentials = !hasCompany || noStructuredExtraction;
        var hasHeavyUnresolved = unresolvedCount >= 3;
        var hasVersionLikeUnresolved = parseResult.UnresolvedTerms.Any(line => VersionRegex.IsMatch(line));
        var hasOsEvidenceButUnspecified = defaultRequestedOs == RequestedOs.Unspecified && osEvidenceCount > 0;
        var hasActionableAmbiguity = hasAmbiguousTerms && !hasVersionedRequests;
        var hasActionableUnresolved = (hasHeavyUnresolved || hasVersionLikeUnresolved) && !hasVersionedRequests;

        return hasActionableAmbiguity
               || hasActionableUnresolved
               || hasOsEvidenceButUnspecified
               || missingEssentials;
    }

    private static RequestedOs ParseRequestedOsToken(string? os)
    {
        var value = (os ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return RequestedOs.Unspecified;
        }

        if (value.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Windows版", StringComparison.OrdinalIgnoreCase))
        {
            return RequestedOs.Windows;
        }

        if (value.Equals("Linux", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Lin", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Linux版", StringComparison.OrdinalIgnoreCase))
        {
            return RequestedOs.Linux;
        }

        if (value.Equals("Both", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("両方", StringComparison.OrdinalIgnoreCase))
        {
            return RequestedOs.Both;
        }

        return RequestedOs.Unspecified;
    }

    private static string ResolveKnownCodeCandidate(string? rawCode, IReadOnlyCollection<string> knownCodes)
    {
        var source = (rawCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalizedSource = NormalizeCodeLookupKey(source);
        if (normalizedSource.Length == 0)
        {
            return string.Empty;
        }

        string? bestCode = null;
        var bestLength = 0;
        foreach (var candidate in knownCodes.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalizedCandidate = NormalizeCodeLookupKey(candidate);
            if (normalizedCandidate.Length == 0)
            {
                continue;
            }

            if (normalizedSource.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeModuleCode(candidate);
            }

            if (normalizedSource.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedCandidate.Length > bestLength)
                {
                    bestCode = candidate;
                    bestLength = normalizedCandidate.Length;
                }
            }
        }

        return bestCode == null ? string.Empty : NormalizeModuleCode(bestCode);
    }

    private static string NormalizeCodeLookupKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || ch == '+')
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static List<string> ExtractRequestedVersionTokens(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC);
        var versions = VersionRegex.Matches(normalized)
            .Select(match => match.Value.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return versions;
    }

    private static RequestedOs GetRequestedOsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RequestedOs.Unspecified;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var lines = normalized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var windowsScore = 0;
        var linuxScore = 0;
        var bothScore = 0;
        var lastSingleOsHint = RequestedOs.Unspecified;
        var hasExplicitWindowsRequest = false;
        var hasExplicitLinuxRequest = false;

        foreach (var rawLine in lines)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsIgnorableOsHeaderLine(line))
            {
                continue;
            }

            var hasWindows = WindowsTokenRegex.IsMatch(line);
            var hasLinux = LinuxTokenRegex.IsMatch(line);
            if (!hasWindows && !hasLinux)
            {
                continue;
            }

            var isQuestionLike = IsQuestionLikeOsLine(line);
            var isRequestLike = IsOsRequestLine(line);
            if (hasWindows && hasLinux)
            {
                if (isQuestionLike)
                {
                    // "Windows/Linux どちらでしょうか" は指定ではなく質問として扱う
                    continue;
                }

                if (IsBothOsIntentLine(line))
                {
                    bothScore += 3;
                }
                else
                {
                    bothScore += 1;
                }

                continue;
            }

            // "Windows版でしょうか" のような問い合わせ文は指定として扱わない。
            if (isQuestionLike && !isRequestLike)
            {
                continue;
            }

            if (hasWindows)
            {
                windowsScore += isRequestLike ? 4 : 2;
                lastSingleOsHint = RequestedOs.Windows;
                if (isRequestLike)
                {
                    hasExplicitWindowsRequest = true;
                }
            }
            else if (hasLinux)
            {
                linuxScore += isRequestLike ? 4 : 2;
                lastSingleOsHint = RequestedOs.Linux;
                if (isRequestLike)
                {
                    hasExplicitLinuxRequest = true;
                }
            }
        }

        if (hasExplicitWindowsRequest && !hasExplicitLinuxRequest)
        {
            return RequestedOs.Windows;
        }

        if (hasExplicitLinuxRequest && !hasExplicitWindowsRequest)
        {
            return RequestedOs.Linux;
        }

        if (bothScore > 0 && windowsScore == 0 && linuxScore == 0)
        {
            return RequestedOs.Both;
        }

        if (windowsScore > linuxScore)
        {
            return RequestedOs.Windows;
        }

        if (linuxScore > windowsScore)
        {
            return RequestedOs.Linux;
        }

        if (windowsScore > 0 && windowsScore == linuxScore)
        {
            if (lastSingleOsHint != RequestedOs.Unspecified)
            {
                return lastSingleOsHint;
            }

            if (bothScore > 0)
            {
                return RequestedOs.Both;
            }
        }

        if (bothScore > 0)
        {
            return RequestedOs.Both;
        }

        return RequestedOs.Unspecified;
    }

    private static RequestedOs GetDefaultRequestedOsFromMemo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RequestedOs.Unspecified;
        }

        var primary = ExtractPrimaryMemoSegment(text);
        var fromPrimary = GetRequestedOsFromText(primary);
        if (fromPrimary != RequestedOs.Unspecified)
        {
            return fromPrimary;
        }

        return GetRequestedOsFromText(text);
    }

    private static string GetMemoPrimarySegmentForAutoParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var primary = ExtractPrimaryMemoSegment(text);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return text.Trim();
    }

    private static string ExtractPrimaryMemoSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var builder = new StringBuilder();
        var seenContent = false;
        var nonEmptyCount = 0;

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;
            var trimmed = line.Trim();
            if (!seenContent)
            {
                if (trimmed.Length == 0)
                {
                    continue;
                }

                seenContent = true;
            }

            if (trimmed.Length > 0)
            {
                nonEmptyCount++;
            }

            if (nonEmptyCount > 3 && IsQuotedMessageStartLine(trimmed))
            {
                break;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }

    private static bool IsQuotedMessageStartLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (QuotedMessageStartRegex.IsMatch(line))
        {
            return true;
        }

        if (ReplyHeaderStartRegex.IsMatch(line))
        {
            return true;
        }

        if (QuotedLineRegex.IsMatch(line))
        {
            return true;
        }

        if (line.Contains("Original Message", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.StartsWith("--------------- Original Message", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsBothOsIntentLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (OsBothIntentRegex.IsMatch(line))
        {
            return true;
        }

        return line.Contains("Windows/Linux", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Windows版/Linux版", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuestionLikeOsLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return OsQuestionRegex.IsMatch(line);
    }

    private static bool IsOsRequestLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return OsRequestRegex.IsMatch(line);
    }

    private static bool IsIgnorableOsHeaderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        if (IsQuotedMessageStartLine(line))
        {
            return true;
        }

        return line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("件名:", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("件名：", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("[thread::", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("thread::", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> CollectOsEvidenceLines(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0 || IsIgnorableOsHeaderLine(line))
            {
                continue;
            }

            if (WindowsTokenRegex.IsMatch(line) || LinuxTokenRegex.IsMatch(line))
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static string BuildPreviewText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..Math.Max(1, maxLength)] + "...";
    }

    private static string FormatRequestedOs(RequestedOs requestedOs)
    {
        return requestedOs switch
        {
            RequestedOs.Windows => "Windows",
            RequestedOs.Linux => "Linux",
            RequestedOs.Both => "両方",
            _ => "未指定"
        };
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

    private void ApplyQuickRequestOsSelection(
        Dictionary<ModuleRowViewModel, RequestedOs> osRequests,
        RequestedOs defaultRequestedOs)
    {
        if (HelixVersions.Count == 0)
        {
            return;
        }

        foreach (var module in HelixVersions.SelectMany(h => h.Modules).Where(m => m.IsSelected))
        {
            var requested = osRequests.TryGetValue(module, out var value)
                ? value
                : defaultRequestedOs;
            if (requested == RequestedOs.Unspecified)
            {
                requested = defaultRequestedOs;
            }

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
            var looksLikeHelixVersion = IsLikelyHelixVersionToken(token);
            if (!looksLikeHelixVersion && !HasHelixKeywordNearToken(normalized, match.Index, match.Length))
            {
                continue;
            }

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
        var trimmed = token?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        var exact = HelixVersions.FirstOrDefault(v => string.Equals(v.Version, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        if (TryBuildHelixVersionKey(trimmed, out var requestKey))
        {
            foreach (var helix in HelixVersions)
            {
                if (TryBuildHelixVersionKey(helix.Version, out var helixKey) &&
                    helixKey.Equals(requestKey, StringComparison.OrdinalIgnoreCase))
                {
                    return helix;
                }
            }
        }

        return HelixVersions.FirstOrDefault(v => v.Version.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
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

    private static bool TryBuildHelixVersionKey(string version, out string key)
    {
        key = string.Empty;
        var numbers = ExtractVersionNumbers(version);
        if (numbers.Count < 2 || numbers[0] < 2000)
        {
            return false;
        }

        key = $"{numbers[0]}.{numbers[1]}";
        return true;
    }

    private static bool HasHelixKeywordNearToken(string text, int tokenStartIndex, int tokenLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var start = Math.Max(0, tokenStartIndex - 24);
        var end = Math.Min(text.Length, tokenStartIndex + tokenLength + 24);
        if (end <= start)
        {
            return false;
        }

        var context = text[start..end];
        return context.Contains("helix", StringComparison.OrdinalIgnoreCase);
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

    private sealed record VersionRequestTarget(string Token, string CanonicalCode);

    private sealed record VersionedRequest(string Code, string Version, RequestedOs OsSelection);

    private sealed record ManualPickEntry(
        string HelixVersion,
        string Code,
        string RequestedVersion,
        InstallerAsset? Asset,
        string Reason);

    private void RegisterTransferItem(TransferItemViewModel item)
    {
        _transferStatusLookup[item.Record.Id] = item.Status;
        item.ProgressChanged += OnTransferItemProgressChanged;
        item.PropertyChanged += OnTransferItemPropertyChanged;
    }

    private void UnregisterTransferItem(TransferItemViewModel item)
    {
        item.ProgressChanged -= OnTransferItemProgressChanged;
        item.PropertyChanged -= OnTransferItemPropertyChanged;
        _transferStatusLookup.Remove(item.Record.Id);
    }

    private void OnTransferItemProgressChanged(object? sender, EventArgs e)
    {
        TransferSummary.Update(TransferItems, MaxConcurrentTransfers);
    }

    private void OnTransferItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TransferItemViewModel item)
        {
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(TransferItemViewModel.Status), StringComparison.Ordinal))
        {
            return;
        }

        var previous = _transferStatusLookup.TryGetValue(item.Record.Id, out var cached)
            ? cached
            : item.Status;

        if (previous != TransferStatus.Completed && item.Status == TransferStatus.Completed)
        {
            NotifyTransferCompleted(item);
            UpdateBasket();
        }

        _transferStatusLookup[item.Record.Id] = item.Status;
    }

    private void NotifyTransferCompleted(TransferItemViewModel item)
    {
        var title = "ダウンロード完了";
        var message = string.IsNullOrWhiteSpace(item.Company)
            ? item.FileName
            : $"{item.Company}\n{item.FileName}";
        RequestNotification?.Invoke(title, message);
    }

    private async Task LoadTransferItemsAsync()
    {
        var items = await _databaseService.LoadTransferItemsAsync();
        foreach (var record in items)
        {
            if (record.Status is TransferStatus.Queued
                or TransferStatus.HashingSource
                or TransferStatus.Downloading
                or TransferStatus.Verifying)
            {
                record.Status = TransferStatus.Paused;
                await _databaseService.UpdateTransferItemAsync(record);
            }

            var vm = new TransferItemViewModel(record, TransferManager);
            RegisterTransferItem(vm);
            TransferItems.Add(vm);
        }
    }

    partial void OnMemoTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(CompanyName))
        {
            TryAutoFillCompanyNameFromMemo(GetMemoPrimarySegmentForAutoParse(value));
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

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        if (value == ShipmentInputTabIndex)
        {
            RefreshShipmentHistoryItems(keepCurrentSelection: true, resetShipmentDate: true);
            return;
        }

        if (value == ShipmentHistoryViewTabIndex)
        {
            RefreshShipmentHistoryViewInternal(showValidationError: false);
        }
    }

    partial void OnSelectedShipmentHistoryItemChanged(BasketItemViewModel? value)
    {
        ApplyShipmentHistorySelection(value);
    }

    partial void OnCompanyNameChanged(string value)
    {
        if (!_isSettingCompanyNameFromMemo)
        {
            _isCompanyNameAutoFilled = false;
        }

        _redownloadUnlockedDestinationPaths.Clear();
        OnPropertyChanged(nameof(OutputFolderPreview));
        ShipmentCompanyName = value?.Trim() ?? string.Empty;
        if (!_isApplyingSelectionHistory && !_isRestoringCustomState)
        {
            UpdateBasket();
        }
    }

    partial void OnSettingsChanged(SettingsModel value)
    {
        _redownloadUnlockedDestinationPaths.Clear();
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(OutputFolderPreview));
        OnPropertyChanged(nameof(ShipmentHistoryExcelPath));
        OnPropertyChanged(nameof(IsShipmentHistoryExcelConfigured));
        MaxConcurrentTransfers = value.MaxConcurrentTransfers;
        MaxConcurrentTransfersInput = value.MaxConcurrentTransfers.ToString();
        if (!_isApplyingSelectionHistory && !_isRestoringCustomState)
        {
            UpdateBasket();
        }
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

    partial void OnShipmentHistoryPeriodChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryFromDateChanged(DateTime? value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryToDateChanged(DateTime? value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryCompanyFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryPersonFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryCategoryFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryHelixFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryCodeFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryNameFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryCompatibilityVersionFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistorySelectedOsFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnShipmentHistoryInstallerFilterChanged(string value)
    {
        ApplyShipmentHistoryViewFilters();
    }

    partial void OnSelectedCustomTabChanged(CustomTabViewModel? value)
    {
        if (!_isRestoringCustomState && !_isApplyingSelectionHistory)
        {
            Settings.SelectedCustomTabName = value?.Name ?? string.Empty;
            PersistCustomState();
        }

        if (value == null)
        {
            return;
        }

        NewCustomTabName = value.Name;
        NewCustomTabColumns = value.ColumnsInput;
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
        var lines = new List<string> { "■アップロード済みファイル" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in BasketItems.Where(b => !b.IsMissing && b.IsAlreadyDownloaded))
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


