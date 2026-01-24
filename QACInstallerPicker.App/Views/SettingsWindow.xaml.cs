using System.Windows;
using QACInstallerPicker.App.ViewModels;

namespace QACInstallerPicker.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
    }
}
