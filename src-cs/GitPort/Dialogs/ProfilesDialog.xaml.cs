using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GitPort.Services;

namespace GitPort.Dialogs;

public partial class ProfilesDialog : Window
{
    private readonly ProfileService _store;
    private readonly string? _repository;
    private readonly List<GitProfile> _drafts;
    private GitProfile? _editing;
    public bool CredentialsForgotten { get; private set; }

    public ProfilesDialog(ProfileService store, string? repository, string remoteUrl)
    {
        InitializeComponent();
        _store = store;
        _repository = repository;
        _drafts = JsonSerializer.Deserialize<List<GitProfile>>(JsonSerializer.Serialize(store.Profiles))!;
        TxtContext.Text = repository == null ? "Create reusable identities, then open a repository to assign one."
            : $"{Path.GetFileName(repository)} — {remoteUrl}";
        ChkAssign.IsEnabled = repository != null;
        ChkAssign.IsChecked = repository != null && store.IsAssigned(repository);
        ProfileList.ItemsSource = _drafts;
        var active = repository == null ? null : store.Resolve(repository, remoteUrl);
        ProfileList.SelectedItem = _drafts.FirstOrDefault(p => p.Id == active?.Id) ?? _drafts.FirstOrDefault();
        Editor.IsEnabled = _editing != null;
    }

    private void Capture()
    {
        if (_editing == null) return;
        _editing.Name = TxtName.Text.Trim();
        _editing.AuthorName = TxtAuthor.Text.Trim();
        _editing.AuthorEmail = TxtEmail.Text.Trim();
        _editing.Username = TxtUsername.Text.Trim();
        _editing.SshKeyPath = TxtSshKey.Text.Trim();
        _editing.RemoteMatch = TxtMatch.Text.Trim();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Capture();
        _editing = ProfileList.SelectedItem as GitProfile;
        Editor.IsEnabled = _editing != null;
        TxtName.Text = _editing?.Name ?? "";
        TxtAuthor.Text = _editing?.AuthorName ?? "";
        TxtEmail.Text = _editing?.AuthorEmail ?? "";
        TxtUsername.Text = _editing?.Username ?? "";
        TxtSshKey.Text = _editing?.SshKeyPath ?? "";
        TxtMatch.Text = _editing?.RemoteMatch ?? "";
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        Capture();
        var profile = new GitProfile();
        _drafts.Add(profile);
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = profile;
        if (_repository != null) ChkAssign.IsChecked = true;
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        _drafts.Remove(_editing);
        _editing = null;
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = _drafts.FirstOrDefault();
        Editor.IsEnabled = _editing != null;
    }

    private void OnForgetCredentials(object sender, RoutedEventArgs e)
    {
        _editing?.Credentials.Clear();
        CredentialsForgotten = true;
        TxtError.Text = "Saved logins will be removed when you save.";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Capture();
        if (_drafts.Any(p => string.IsNullOrWhiteSpace(p.Name))) { TxtError.Text = "Each profile needs a name."; return; }
        if (_drafts.Any(p => p.SshKeyPath.Length > 0 && !File.Exists(p.SshKeyPath)))
        { TxtError.Text = "An SSH key file does not exist. Check the path."; return; }
        try
        {
            _store.Apply(_drafts, _repository, ChkAssign.IsChecked == true ? _editing : null);
            DialogResult = true;
        }
        catch (Exception ex) { TxtError.Text = $"Could not save profiles: {ex.Message}"; }
    }
}
