using System.Windows;
using GitPort.Services;

namespace GitPort.Dialogs;

internal static class ConnectionAuthentication
{
    public static async Task<bool> PromptAsync(Window owner, GitService git, string url, string error)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(owner, "SSH authentication failed. Select a private key in Profiles and check server access and known_hosts.\n\n" + error,
                "SSH authentication", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var dialog = new CredentialsDialog(url, git.ActiveProfile?.Username, GitService.IsSslFailure(error)) { Owner = owner };
        if (dialog.ShowDialog() != true) return false;
        try
        {
            if (dialog.DisableSslVerify && !await git.SetSslVerifyAsync(false, url))
                throw new InvalidOperationException("Could not save the certificate setting for this remote.");
            await git.SetCredentialsAsync(url, dialog.Username, dialog.Password, dialog.RememberCredentials);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Could not configure connection", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
