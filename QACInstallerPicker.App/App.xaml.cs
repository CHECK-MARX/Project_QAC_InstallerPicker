using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using QACInstallerPicker.App.Helpers;
using Wpf = System.Windows;

namespace QACInstallerPicker.App;

public partial class App : Wpf.Application
{
    protected override void OnStartup(Wpf.StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.LogError("DispatcherUnhandledException", e.Exception);
        Wpf.MessageBox.Show($"Unexpected error. See log: {AppPaths.LogPath}", "Error",
            Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.LogError("UnhandledException", ex);
        }
        else
        {
            AppLogger.LogInfo($"UnhandledException: {e.ExceptionObject}");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.LogError("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
