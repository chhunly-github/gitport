using System.Windows;
using GitPort.Services;

namespace GitPort.Dialogs;

public partial class CredentialsDialog : Window
{
    public string RemoteUrl { get; }
    public string Username => TxtUsername.Text.Trim();
    public string Password => _isPasswordVisible ? TxtPasswordVisible.Text : TxtPassword.Password;
    public bool RememberCredentials => ChkRemember.IsChecked == true;
    public bool DisableSslVerify => ChkDisableSslVerify.IsChecked == true;

    private bool _isPasswordVisible;

    public CredentialsDialog(string remoteUrl, string? defaultUsername = null, bool isSslError = false, string? customErrorMessage = null)
    {
        InitializeComponent();
        RemoteUrl = remoteUrl;
        TxtRemoteUrl.Text = string.IsNullOrWhiteSpace(remoteUrl) ? "(No remote URL configured)" : remoteUrl;

        var info = GitService.DetectGitProvider(remoteUrl);
        TxtProviderName.Text = info.Name;
        TxtProviderHint.Text = info.Hint;
        TxtProviderIcon.Text = info.Icon;
        if (System.Uri.TryCreate(remoteUrl, System.UriKind.Absolute, out var uri) && uri.Scheme == "http")
        {
            TxtProviderHint.Text += "\nHTTP connection: credentials travel without encryption. No SSL bypass is needed.";
            ChkDisableSslVerify.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(defaultUsername))
        {
            TxtUsername.Text = defaultUsername;
            TxtPassword.Focus();
        }
        else
        {
            TxtUsername.Focus();
        }

        if (isSslError)
        {
            BannerSslAlert.Visibility = Visibility.Visible;
            ChkDisableSslVerify.IsChecked = false;
            ChkDisableSslVerify.Foreground = System.Windows.Media.Brushes.White;
        }

        if (!string.IsNullOrWhiteSpace(customErrorMessage))
        {
            TxtValidationMessage.Text = customErrorMessage;
        }
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        if (_isPasswordVisible)
        {
            TxtPasswordVisible.Text = TxtPassword.Password;
            TxtPassword.Visibility = Visibility.Collapsed;
            TxtPasswordVisible.Visibility = Visibility.Visible;
            TxtPasswordVisible.Focus();
            TxtPasswordVisible.Select(TxtPasswordVisible.Text.Length, 0);
        }
        else
        {
            TxtPassword.Password = TxtPasswordVisible.Text;
            TxtPasswordVisible.Visibility = Visibility.Collapsed;
            TxtPassword.Visibility = Visibility.Visible;
            TxtPassword.Focus();
        }
    }

    private void OnLoginClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            TxtValidationMessage.Text = "Please enter your username or account.";
            TxtUsername.Focus();
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            TxtValidationMessage.Text = "Please enter your password or access token.";
            if (_isPasswordVisible) TxtPasswordVisible.Focus();
            else TxtPassword.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
