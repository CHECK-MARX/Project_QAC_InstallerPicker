using System;
using System.Windows;

namespace QACInstallerPicker.App.Views;

public partial class LargeTextEditorWindow : Window
{
    public LargeTextEditorWindow(
        string title,
        string header,
        string text,
        bool isReadOnly = false,
        string applyButtonText = "適用")
    {
        InitializeComponent();

        Title = title;
        HeaderTextBlock.Text = header;
        EditorTextBox.Text = text ?? string.Empty;
        EditorTextBox.IsReadOnly = isReadOnly;

        if (isReadOnly)
        {
            ApplyButton.Visibility = Visibility.Collapsed;
            CloseButton.Content = "閉じる";
        }
        else
        {
            ApplyButton.Content = string.IsNullOrWhiteSpace(applyButtonText) ? "適用" : applyButtonText;
        }
    }

    public string EditedText => EditorTextBox.Text ?? string.Empty;

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
