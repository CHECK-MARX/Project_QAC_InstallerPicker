using System;
using System.ComponentModel;
using System.Drawing;
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
        var settingsViewModel = new SettingsViewModel(_viewModel.Settings, new SettingsService());
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
