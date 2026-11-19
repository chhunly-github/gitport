using System.Windows;
using GitPort.Services;

namespace GitPort.Dialogs;

internal static class ConnectionAuthentication
{
    public static async Task<bool> PromptAsync(Window owner, GitService git, string url, string error,
        string? remoteName = null, bool forPush = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(owner, "SSH authentication failed. Select a private key in Profiles and check server access and known_hosts.\n\n" + error,
                "SSH authentication", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var dialog = new CredentialsDialog(url, git.ActiveProfile?.Username, GitService.IsSslFailure(error),
            customErrorMessage: error, git: git) { Owner = owner };
        if (dialog.ShowDialog() != true) return false;
        try
        {
            string correctedUrl = dialog.RemoteUrl;
            if (correctedUrl != url)
                await git.SetConnectionUrlAsync(remoteName ?? await git.GetDefaultRemoteAsync(), correctedUrl, forPush);
            if (dialog.DisableSslVerify && !await git.SetSslVerifyAsync(false, correctedUrl))
                throw new InvalidOperationException("Could not save the certificate setting for this remote.");
            if (!string.IsNullOrEmpty(dialog.Username) && !string.IsNullOrEmpty(dialog.Password))
                await git.SetCredentialsAsync(correctedUrl, dialog.Username, dialog.Password, dialog.RememberCredentials, dialog.SelectedProfile);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Could not configure connection", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
