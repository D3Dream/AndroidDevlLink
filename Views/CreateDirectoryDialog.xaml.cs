using System.Windows;
using AndroidDevLink.Services;

namespace AndroidDevLink.Views;

public partial class CreateDirectoryDialog : Window
{
    public CreateDirectoryDialog(
        string title = "新建文件夹",
        string prompt = "文件夹名称",
        string confirmButtonText = "创建",
        string initialName = "")
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        CreateButton.Content = confirmButtonText;
        DirectoryNameTextBox.Text = initialName;
        Loaded += (_, _) =>
        {
            DirectoryNameTextBox.Focus();
            DirectoryNameTextBox.SelectAll();
        };
    }

    public string DirectoryName { get; private set; } = string.Empty;

    private void DirectoryNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool isValid = TryValidateName(out _);
        CreateButton.IsEnabled = isValid;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateName(out string directoryName))
        {
            return;
        }

        DirectoryName = directoryName;
        DialogResult = true;
    }

    private bool TryValidateName(out string directoryName)
    {
        try
        {
            directoryName = AndroidFileService.ValidateEntryName(DirectoryNameTextBox.Text);
            ValidationTextBlock.Text = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            directoryName = string.Empty;
            ValidationTextBlock.Text = exception.Message.Split(" (Parameter", StringSplitOptions.None)[0];
            return false;
        }
    }
}
