using System.Threading.Tasks;
using System.Windows;
using QACInstallerPicker.App.Services;
using QACInstallerPicker.App.ViewModels;
using QACInstallerPicker.App.Views;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QACInstallerPicker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _notifyIconHideTimer;
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
        menu.Items.Add("開く", null, (_, _) => RestoreFromTray());
        menu.Items.Add("終了", null, (_, _) => Close());

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "QAC インストーラ選定ツール",
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
}
