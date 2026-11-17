using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitPort.Models;
using GitPort.Services;

namespace GitPort.Dialogs;

public partial class PushDialog : Window
{
    private readonly GitService _gitService;
    private readonly string _currentBranch;
    private List<GitCommit> _outgoingCommits = new();
    private GitCommitItem? _selectedCommit;

    public int PushedCommitCount => _outgoingCommits.Count;

    public PushDialog(GitService gitService, string currentBranch)
    {
        InitializeComponent();
        _gitService = gitService;
        _currentBranch = currentBranch;

        TxtLocalBranch.Text = _currentBranch;
        TxtTargetBranch.Text = _currentBranch;

        Loaded += async (_, _) => await InitializeDataAsync();
    }

    private async Task InitializeDataAsync()
    {
        try
        {
            var remotes = await _gitService.GetRemotesAsync();
            CboRemote.Items.Clear();

            if (remotes.Count > 0)
            {
                foreach (var r in remotes)
                {
                    CboRemote.Items.Add(r.Name);
                }
                var defaultRemote = remotes.FirstOrDefault(r => r.Name.Equals("origin", StringComparison.OrdinalIgnoreCase))?.Name ?? remotes[0].Name;
                CboRemote.SelectedItem = defaultRemote;
            }
            else
            {
                CboRemote.Items.Add("origin");
                CboRemote.SelectedIndex = 0;
            }

            await LoadOutgoingCommitsAsync();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error initializing: {ex.Message}";
        }
    }

    private async Task LoadOutgoingCommitsAsync()
    {
        TxtStatusMessage.Text = "Calculating outgoing commits...";
        string remote = CboRemote.SelectedItem?.ToString() ?? "origin";
        string branch = TxtTargetBranch.Text.Trim();
        if (string.IsNullOrEmpty(branch)) branch = _currentBranch;

        _outgoingCommits = await _gitService.GetOutgoingCommitsAsync(branch, remote);
        TxtStatusMessage.Text = "";

        if (_outgoingCommits.Count == 0)
        {
            OutgoingCommitsListView.ItemsSource = null;
            PanelUpToDate.Visibility = Visibility.Visible;
            TxtCommitCountBadge.Text = "0 commits (Up to date)";
            TxtCommitCountBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EC7A6"));
            BadgeCommitCount.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EC7A6"));
            BtnPush.Content = "Push";
            BtnPush.IsEnabled = true; // Still allow push e.g. for tags or force push
            ClearCommitDetails();
        }
        else
        {
            PanelUpToDate.Visibility = Visibility.Collapsed;
            var items = _outgoingCommits.Select(c => new GitCommitItem(c)).ToList();

            OutgoingCommitsListView.ItemsSource = items;
            TxtCommitCountBadge.Text = $"{_outgoingCommits.Count} commit{(_outgoingCommits.Count > 1 ? "s" : "")} to push";
            TxtCommitCountBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B8E5"));
            BadgeCommitCount.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B8E5"));
            BtnPush.Content = $"Push ({_outgoingCommits.Count})";
            BtnPush.IsEnabled = true;

            OutgoingCommitsListView.SelectedIndex = 0;
        }
    }

    private async void OnOutgoingCommitSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OutgoingCommitsListView.SelectedItem is GitCommitItem item)
        {
            _selectedCommit = item;
            var c = item.Commit;
            TxtDetailSubject.Text = c.Subject;
            TxtDetailAuthor.Text = $"{c.Author} <{c.Email}>";
            TxtDetailDate.Text = c.Date;
            TxtDetailHash.Text = c.Hash;

            var files = await _gitService.GetCommitFilesAsync(c.Hash);
            TxtFilesHeader.Text = $"CHANGED FILES ({files.Count})";
            CommitFilesListView.ItemsSource = files;
        }
        else
        {
            ClearCommitDetails();
        }
    }

    private void ClearCommitDetails()
    {
        _selectedCommit = null;
        TxtDetailSubject.Text = "Select a commit to view details";
        TxtDetailAuthor.Text = "";
        TxtDetailDate.Text = "";
        TxtDetailHash.Text = "";
        TxtFilesHeader.Text = "CHANGED FILES (0)";
        CommitFilesListView.ItemsSource = null;
    }

    private async void OnRemoteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await LoadOutgoingCommitsAsync();
        }
    }

    private async void OnPushClicked(object sender, RoutedEventArgs e)
    {
        string remote = CboRemote.SelectedItem?.ToString() ?? "origin";
        string branch = TxtTargetBranch.Text.Trim();
        if (string.IsNullOrEmpty(branch)) branch = _currentBranch;

        bool forceWithLease = ChkForceWithLease.IsChecked == true;
        bool pushTags = ChkPushTags.IsChecked == true;

        BtnPush.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        TxtStatusMessage.Text = $"Pushing to {remote}/{branch}...";

        var (success, stdout, stderr) = await _gitService.PushWithDetailsAsync(
            branch: branch,
            remote: remote,
            forceWithLease: forceWithLease,
            pushTags: pushTags);

        if (!success)
        {
            string error = string.IsNullOrEmpty(stderr) ? stdout : stderr;

            if (GitService.IsAuthFailure(error) || GitService.IsSslFailure(error))
            {
                TxtStatusMessage.Text = "Authentication required...";
                string remoteUrl = await _gitService.GetRemoteUrlAsync(remote);
                var authDlg = new CredentialsDialog(remoteUrl, isSslError: GitService.IsSslFailure(error))
                {
                    Owner = this
                };

                if (authDlg.ShowDialog() == true)
                {
                    if (authDlg.DisableSslVerify)
                    {
                        await _gitService.SetSslVerifyAsync(false);
                    }
                    if (authDlg.RememberCredentials)
                    {
                        await _gitService.ApproveCredentialsAsync(remoteUrl, authDlg.Username, authDlg.Password);
                    }

                    TxtStatusMessage.Text = "Retrying push with credentials...";
                    var retry = await _gitService.PushWithDetailsAsync(
                        branch: branch,
                        remote: remote,
                        forceWithLease: forceWithLease,
                        pushTags: pushTags);

                    if (retry.Success)
                    {
                        TxtStatusMessage.Text = "Push succeeded!";
                        DialogResult = true;
                        Close();
                        return;
                    }
                    else
                    {
                        string retryErr = string.IsNullOrEmpty(retry.Error) ? retry.Output : retry.Error;
                        MessageBox.Show(this, $"Push failed after authentication:\n\n{retryErr}", "Git Push Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else if (GitService.IsPushRejectedFailure(error))
            {
                MessageBox.Show(this,
                    $"Push rejected by remote:\n\n{error}\n\nThe remote repository contains work that you do not have locally. Please Pull or Merge remote changes before pushing.",
                    "Push Rejected (Non-Fast-Forward)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(this, $"Git push failed:\n\n{error}", "Git Push Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            BtnPush.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            TxtStatusMessage.Text = "Push failed.";
            return;
        }

        TxtStatusMessage.Text = "Push succeeded!";
        DialogResult = true;
        Close();
    }

    private async void OnViewCommitDiffClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedCommit != null)
        {
            string diff = await _gitService.GetDiffAsync(_selectedCommit.Commit.Hash);
            var win = new DiffWindow(_selectedCommit.ShortHash, _selectedCommit.Subject, diff)
            {
                Owner = this
            };
            win.ShowDialog();
        }
    }

    private async void OnCommitFileDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (_selectedCommit != null && CommitFilesListView.SelectedItem is GitFileEntry file)
        {
            string diff = await _gitService.GetDiffAsync(_selectedCommit.Commit.Hash, file.Path);
            var win = new DiffWindow(file.Path, $"{_selectedCommit.ShortHash} - {_selectedCommit.Subject}", diff)
            {
                Owner = this
            };
            win.ShowDialog();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
