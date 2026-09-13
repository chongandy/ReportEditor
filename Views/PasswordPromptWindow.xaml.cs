using System.Windows;

namespace ReportEditor.Views;

public partial class PasswordPromptWindow : Window
{
    private PasswordPromptWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string EnteredPassword => PasswordInput.Password;

    /// <summary>
    /// Shows a modal password prompt. Returns the entered password, or
    /// <see langword="null"/> if the user cancelled.
    /// </summary>
    public static string? Prompt(Window? owner, string message)
    {
        var dialog = new PasswordPromptWindow(message);
        if (owner is not null)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return dialog.ShowDialog() == true ? dialog.EnteredPassword : null;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) =>
        OkButton.IsEnabled = PasswordInput.Password.Length > 0;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
