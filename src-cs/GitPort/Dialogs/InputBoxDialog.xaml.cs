using System.Windows;

namespace GitPort.Dialogs;

public partial class InputBoxDialog : Window
{
    public string InputValue { get; private set; } = "";

    public InputBoxDialog(string title, string prompt, string defaultVal = "")
    {
        InitializeComponent();
        Title = title;
        TxtPrompt.Text = prompt;
        TxtInput.Text = defaultVal;
        TxtInput.SelectAll();
        Loaded += (_, _) => TxtInput.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        InputValue = TxtInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
