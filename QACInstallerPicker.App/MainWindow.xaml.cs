using System.Threading.Tasks;
using System.Windows;
using QACInstallerPicker.App.Services;
using QACInstallerPicker.App.ViewModels;
using QACInstallerPicker.App.Views;

namespace QACInstallerPicker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.RequestOpenSettings += (_, _) => OpenSettingsDialog();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
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
