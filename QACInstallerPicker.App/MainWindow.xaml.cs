using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QACInstallerPicker.App.Services;
using QACInstallerPicker.App.ViewModels;
using QACInstallerPicker.App.Views;
using Forms = System.Windows.Forms;

namespace QACInstallerPicker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _notifyIconHideTimer;
    private CustomTabPreviewWindow? _customTabPopupWindow;
    private bool _isInTray;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
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
        Loaded += MainWindow_Loaded;
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

        OpenCustomTabPopupWindow();
        e.Handled = true;
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
        if (_customTabPopupWindow == null)
        {
            _customTabPopupWindow = new CustomTabPreviewWindow(_viewModel)
            {
                Owner = this
            };
            _customTabPopupWindow.Closed += (_, _) => _customTabPopupWindow = null;
            _customTabPopupWindow.Show();
            _customTabPopupWindow.Activate();
            return;
        }

        _customTabPopupWindow.RefreshPreview();
        if (!_customTabPopupWindow.IsVisible)
        {
            _customTabPopupWindow.Show();
        }

        _customTabPopupWindow.Activate();
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
