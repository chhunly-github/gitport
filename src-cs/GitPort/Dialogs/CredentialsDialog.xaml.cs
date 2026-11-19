using System.Windows;
using GitPort.Services;

namespace GitPort.Dialogs;

public partial class CredentialsDialog : Window
{
    public string RemoteUrl => GitConnectionLinks.CleanUrl(TxtRemoteUrl.Text.Trim());
    public string Username => TxtUsername.Text.Trim();
    public string Password => _isPasswordVisible ? TxtPasswordVisible.Text : TxtPassword.Password;
    public bool RememberCredentials => ChkRemember.IsChecked == true;
    public bool DisableSslVerify => ChkDisableSslVerify.IsChecked == true;

    private bool _isPasswordVisible;
    private readonly GitService? _git;
    private readonly string _originalUrl;
    private bool _ready;
    private CancellationTokenSource? _browserLogin;
    public GitProfile? SelectedProfile => (SavedAccounts.SelectedItem as SavedGitAccount)?.Profile;

    public CredentialsDialog(string remoteUrl, string? defaultUsername = null, bool isSslError = false, string? customErrorMessage = null, GitService? git = null)
    {
        InitializeComponent();
        _git = git;
        TxtRemoteUrl.Text = GitConnectionLinks.CleanUrl(remoteUrl);
        _originalUrl = RemoteUrl;

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
            TxtConnectionError.Text = PushOutput.Diagnostics(customErrorMessage);
            TxtConnectionError.Visibility = Visibility.Visible;
        }
        TxtRecoveryHint.Text = "A private repository can appear as 'not found' when the account lacks access. Check the clone URL, sign in with the right account, and ensure its token allows repository access. For push, it needs write permission.";
        _ready = true;
        RefreshConnectionOptions();
        Closing += (_, e) =>
        {
            if (_browserLogin != null) { _browserLogin.Cancel(); e.Cancel = true; }
        };
    }

    private void OnRemoteUrlChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_ready) return;
        TxtPassword.Password = "";
        TxtPasswordVisible.Text = "";
        ChkDisableSslVerify.IsChecked = false;
        var info = GitService.DetectGitProvider(TxtRemoteUrl.Text);
        TxtProviderName.Text = info.Name;
        TxtProviderHint.Text = info.Hint;
        TxtProviderIcon.Text = info.Icon;
        RefreshConnectionOptions();
    }

    private void RefreshConnectionOptions()
    {
        SavedAccounts.Items.Clear();
        SavedAccountPanel.Visibility = Visibility.Collapsed;
        try
        {
            var accounts = _git?.GetSavedAccounts(RemoteUrl);
            if (accounts?.Count > 0)
            {
                SavedAccounts.Items.Add("Enter a different account");
                foreach (var account in accounts) SavedAccounts.Items.Add(account);
                SavedAccounts.SelectedIndex = 0;
                SavedAccountPanel.Visibility = Visibility.Visible;
            }
            bool browser = GitConnectionLinks.SupportsBrowser(RemoteUrl) && _git != null;
            BtnBrowserLogin.IsEnabled = browser;
            TxtBrowserHint.Text = browser
                ? "Authenticate in your browser, then return here and choose Save & Retry. Requires Git Credential Manager."
                : "Use Open repository to sign in on the website, then paste a password or token here. Website sign-in alone does not authenticate Git on this server.";
            ChkDisableSslVerify.Visibility = new Uri(RemoteUrl).Scheme == "https" ? Visibility.Visible : Visibility.Collapsed;
            if (ChkDisableSslVerify.Visibility != Visibility.Visible) ChkDisableSslVerify.IsChecked = false;
        }
        catch (ArgumentException) { BtnBrowserLogin.IsEnabled = false; }
    }

    private void OpenLink(Func<string, string> link)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link(RemoteUrl)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { TxtValidationMessage.Text = "Could not open the link. Check the clone URL or use Copy link."; }
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => OpenLink(GitConnectionLinks.RepositoryPage);
    private void OnOpenTokenPage(object sender, RoutedEventArgs e) => OpenLink(GitConnectionLinks.TokenPage);

    private void OnCopyLink(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(RemoteUrl); TxtValidationMessage.Text = "Repository link copied."; }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException)
        { TxtValidationMessage.Text = "Could not copy the link. Check the URL and try again."; }
    }

    private void OnPastePassword(object sender, RoutedEventArgs e)
    {
        try
        {
            string value = Clipboard.GetText().TrimEnd('\r', '\n');
            TxtPassword.Password = value;
            TxtPasswordVisible.Text = value;
        }
        catch (System.Runtime.InteropServices.ExternalException) { TxtValidationMessage.Text = "Clipboard is busy. Try pasting again."; }
    }

    private async void OnBrowserLogin(object sender, RoutedEventArgs e)
    {
        if (_git == null || _browserLogin != null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _browserLogin = cancellation;
        CredentialInputs.IsEnabled = false;
        TxtRemoteUrl.IsEnabled = false;
        SavedAccounts.IsEnabled = false;
        BtnLogin.IsEnabled = false;
        TxtValidationMessage.Text = "Complete sign-in in your browser. Cancel stops waiting.";
        try
        {
            var login = await _git.BrowserSignInAsync(RemoteUrl, Username, cancellation.Token);
            TxtUsername.Text = login.Username;
            TxtPassword.Password = login.Password;
            TxtPasswordVisible.Text = login.Password;
            TxtValidationMessage.Text = "Browser sign-in completed. Choose Save & Retry to use this account.";
        }
        catch (OperationCanceledException) { TxtValidationMessage.Text = "Browser sign-in cancelled or timed out. Retry or paste a token/password."; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.IO.IOException)
        { TxtValidationMessage.Text = ex is System.ComponentModel.Win32Exception or System.IO.IOException ? "Git Credential Manager could not complete sign-in. Use password/token entry instead." : ex.Message; }
        finally
        {
            _browserLogin = null;
            CredentialInputs.IsEnabled = true;
            TxtRemoteUrl.IsEnabled = true;
            SavedAccounts.IsEnabled = true;
            BtnLogin.IsEnabled = true;
        }
    }

    private void OnSavedAccountChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        TxtValidationMessage.Text = "";
        TxtPassword.Password = "";
        TxtPasswordVisible.Text = "";
        if (SavedAccounts.SelectedItem is not SavedGitAccount account || _git == null) return;
        try
        {
            var login = _git.ReadSavedAccount(account, RemoteUrl);
            TxtUsername.Text = login.Username;
            TxtPassword.Password = login.Password;
            TxtPasswordVisible.Text = login.Password;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FormatException || ex is ArgumentException || ex is KeyNotFoundException)
        {
            SavedAccounts.SelectedIndex = 0;
            TxtValidationMessage.Text = "This saved login cannot be read. Enter your password or token again.";
        }
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        BtnTogglePassword.Content = _isPasswordVisible ? "Hide" : "Show";
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
        try { _ = RemoteUrl; }
        catch (ArgumentException ex) { TxtValidationMessage.Text = ex.Message; TxtRemoteUrl.Focus(); return; }
        // A corrected URL may be sufficient for a public repository.
        if (RemoteUrl != _originalUrl && string.IsNullOrEmpty(Username) && string.IsNullOrEmpty(Password))
        { DialogResult = true; Close(); return; }
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
        if (_browserLogin != null) { _browserLogin.Cancel(); return; }
        DialogResult = false;
        Close();
    }
}
