using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Services;
using QACInstallerPicker.App.ViewModels;
using QACInstallerPicker.App.Views;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace QACInstallerPicker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _notifyIconHideTimer;
    private CustomTabPreviewWindow? _customTabPopupWindow;
    private bool _isInTray;

    public MainWindow() : this(new MainViewModel(), initializeOnLoaded: true)
    {
    }

    public MainWindow(MainViewModel viewModel, bool initializeOnLoaded)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.RequestOpenSettings += (_, _) => OpenSettingsDialog();
        _viewModel.RequestNotification += ShowTransferNotification;
        _notifyIcon = CreateNotifyIcon();
        _notifyIconHideTimer = CreateNotifyIconHideTimer();
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, args) =>
        {
            args.Handled = true;
            HideToTray();
        }));
        if (initializeOnLoaded)
        {
            Loaded += MainWindow_Loaded;
        }
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("\u958b\u304f", null, (_, _) => RestoreFromTray());
        menu.Items.Add("\u7d42\u4e86", null, (_, _) => Close());

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "QAC \u30a4\u30f3\u30b9\u30c8\u30fc\u30e9\u9078\u5b9a\u30c4\u30fc\u30eb",
            ContextMenuStrip = menu,
            Visible = false
        };
        notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        notifyIcon.BalloonTipClicked += (_, _) => RestoreFromTray();
        return notifyIcon;
    }

    private DispatcherTimer CreateNotifyIconHideTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_isInTray)
            {
                _notifyIcon.Visible = false;
            }
        };
        return timer;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        _isInTray = true;
        ShowInTaskbar = false;
        Hide();
        _notifyIcon.Visible = true;
    }

    private void RestoreFromTray()
    {
        _isInTray = false;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (!_notifyIconHideTimer.IsEnabled)
        {
            _notifyIcon.Visible = false;
        }
    }

    private void ShowTransferNotification(string title, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowTransferNotification(title, message));
            return;
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!_notifyIcon.Visible)
        {
            _notifyIcon.Visible = true;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);

        if (!_isInTray)
        {
            _notifyIconHideTimer.Stop();
            _notifyIconHideTimer.Start();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_customTabPopupWindow != null)
        {
            _customTabPopupWindow.Close();
            _customTabPopupWindow = null;
        }

        _notifyIconHideTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private async void OpenSettingsDialog()
    {
        var helixVersions = _viewModel.HelixVersions
            .Select(item => item.Version)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var customTabs = _viewModel.CustomTabs
            .Select(item => item.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var settingsViewModel = new SettingsViewModel(
            _viewModel.Settings,
            new SettingsService(),
            helixVersions,
            customTabs);
        var window = new SettingsWindow(settingsViewModel)
        {
            Owner = this
        };
        var result = window.ShowDialog();
        if (result == true)
        {
            await _viewModel.ApplySettingsAndReloadAsync();
        }
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

    private void CustomTabDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.DataContext is not CustomTabViewModel)
        {
            return;
        }

        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) == null)
        {
            return;
        }

        OpenCustomTabPopupWindow(dataGrid);
        e.Handled = true;
    }

    private void OpenCustomTabPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        OpenCustomTabPopupWindow();
    }

    private void ApplyCustomPathRepairButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedCustomTab;
        var grid = selected == null ? null : FindCustomTabGrid(this, selected);
        if (grid != null &&
            (!grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !grid.CommitEdit(DataGridEditingUnit.Row, true)))
        {
            WpfMessageBox.Show(this, "編集中の値を確定してからパスを修復してください。", "入力の確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _viewModel.ApplyCustomPathRepairCommand.Execute(null);
    }

    private void OpenBuildInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var buildInfoPath = Path.Combine(AppContext.BaseDirectory, "BUILD_INFO.txt");
        var details = $"アプリ: {_viewModel.AppTitle}";
        try
        {
            details += File.Exists(buildInfoPath)
                ? Environment.NewLine + File.ReadAllText(buildInfoPath)
                : Environment.NewLine + "ビルド情報ファイルはありません。";
        }
        catch (IOException ex)
        {
            AppLogger.LogError("ReadBuildInfo", ex);
            details += Environment.NewLine + "ビルド情報を読み込めませんでした。";
        }
        WpfMessageBox.Show(this, details, "版情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "テキスト (*.txt)|*.txt",
            FileName = $"QAC診断情報_{DateTime.Now:yyyyMMdd_HHmm}.txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            var report = DiagnosticsService.BuildReport(
                _viewModel.AppTitle,
                Path.Combine(AppContext.BaseDirectory, "BUILD_INFO.txt"),
                AppPaths.LogPath);
            DiagnosticsService.Save(dialog.FileName, report);
            WpfMessageBox.Show(this, "診断情報を出力しました。", "診断情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("ExportDiagnostics", ex);
            WpfMessageBox.Show(this, ex.Message, "診断情報の出力に失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyCustomTabFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CustomTabViewModel tab })
        {
            return;
        }
        var grid = FindCustomTabGrid(this, tab);
        if (grid != null &&
            (!grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !grid.CommitEdit(DataGridEditingUnit.Row, true)))
        {
            WpfMessageBox.Show(this, "編集中の値を確定してから検索してください。", "入力の確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        tab.ApplyFilter();
    }

    private void ModulesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ModuleRowViewModel module)
        {
            return;
        }

        if (!module.IsDownloadLocked)
        {
            return;
        }

        if (_viewModel.ToggleRedownloadForModule(module))
        {
            e.Handled = true;
        }
    }

    private void BasketDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (FindVisualParent<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not BasketItemViewModel item || !item.IsAlreadyDownloaded)
        {
            return;
        }

        if (_viewModel.ToggleRedownloadForBasketItem(item))
        {
            e.Handled = true;
        }
    }

    private void CustomTabTabControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedCustomTab == null)
        {
            return;
        }

        if (FindVisualParent<TabItem>(e.OriginalSource as DependencyObject) == null)
        {
            return;
        }

        OpenCustomTabPopupWindow();
        e.Handled = true;
    }

    private void OpenCustomTabPopupWindow()
    {
        OpenCustomTabPopupWindow(null);
    }

    private void OpenCustomTabPopupWindow(DataGrid? sourceGrid)
    {
        if (_viewModel.SelectedCustomTab == null)
        {
            return;
        }

        // The preview binds to the same DataView. Its ItemsSource cannot attach while
        // the main grid still has an active row edit transaction.
        sourceGrid ??= FindCustomTabGrid(this, _viewModel.SelectedCustomTab);
        if (sourceGrid != null &&
            (!sourceGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !sourceGrid.CommitEdit(DataGridEditingUnit.Row, true)))
        {
            WpfMessageBox.Show(this, "編集中の値を確定してから拡大表示を開いてください。", "入力の確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_customTabPopupWindow == null)
        {
            CustomTabPreviewWindow? window = null;
            try
            {
                window = new CustomTabPreviewWindow(_viewModel) { Owner = this };
                window.Closed += CustomTabPopupWindow_Closed;
                window.Show();
                _customTabPopupWindow = window;
                window.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OpenCustomTabPreview", ex);
                if (window != null)
                {
                    window.Closed -= CustomTabPopupWindow_Closed;
                    window.ReleaseHandlers();
                    window.Close();
                }
                _customTabPopupWindow = null;
                WpfMessageBox.Show(this, "拡大表示を開けませんでした。編集中の値を確定して、もう一度お試しください。",
                    "拡大表示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        try
        {
            _customTabPopupWindow.RefreshPreview();
            if (_customTabPopupWindow.WindowState == WindowState.Minimized)
            {
                _customTabPopupWindow.WindowState = WindowState.Normal;
            }
            _customTabPopupWindow.Activate();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("RefreshCustomTabPreview", ex);
            WpfMessageBox.Show(this, "拡大表示の更新に失敗しました。編集中の値を確定して、もう一度お試しください。",
                "拡大表示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CustomTabPopupWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is CustomTabPreviewWindow window)
        {
            window.Closed -= CustomTabPopupWindow_Closed;
            if (ReferenceEquals(_customTabPopupWindow, window))
            {
                _customTabPopupWindow = null;
            }
        }
    }

    private static DataGrid? FindCustomTabGrid(DependencyObject parent, CustomTabViewModel tab)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is DataGrid grid && ReferenceEquals(grid.DataContext, tab))
            {
                return grid;
            }
            var found = FindCustomTabGrid(child, tab);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private void OpenMemoEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LargeTextEditorWindow(
            "メール/メモ 拡大編集",
            "メール/メモ",
            _viewModel.MemoText,
            isReadOnly: false,
            applyButtonText: "反映")
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _viewModel.MemoText = window.EditedText;
            if (_viewModel.ApplyQuickRequestCommand.CanExecute(null))
            {
                _viewModel.ApplyQuickRequestCommand.Execute(null);
            }
        }
    }

    private void OpenDecisionLogButton_Click(object sender, RoutedEventArgs e)
    {
        var logText = _viewModel.QuickRequestDecisionLog;
        if (string.IsNullOrWhiteSpace(logText))
        {
            logText = _viewModel.QuickRequestResult;
        }

        var window = new LargeTextEditorWindow(
            "Decision Log",
            "Parsing and selection reasons",
            logText,
            isReadOnly: true)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OpenUnresolvedEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LargeTextEditorWindow(
            "未解決内容 拡大編集",
            "未解決内容（`キーワード => コード` の形式で学習登録可能）",
            _viewModel.UnresolvedMemoText,
            isReadOnly: false,
            applyButtonText: "反映")
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _viewModel.UnresolvedMemoText = window.EditedText;
        }
    }

    private void OpenMemoHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LargeTextEditorWindow(
            "メモ解析 履歴",
            "学習済みキーワード / 未解決履歴",
            _viewModel.BuildMemoLearningHistoryText(),
            isReadOnly: true)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T hit)
            {
                return hit;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
