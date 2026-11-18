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
    private bool _initializing = true;
    private bool _pushing;
    private int _previewVersion;

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
                var defaultRemote = await _gitService.GetDefaultRemoteAsync();
                CboRemote.SelectedItem = defaultRemote;
                TxtTargetBranch.Text = await _gitService.GetPushTargetBranchAsync(defaultRemote);
            }
            else
            {
                TxtStatusMessage.Text = "No remotes configured. Add a remote with Git before pushing.";
                BtnPush.IsEnabled = false;
                return;
            }

            _initializing = false;
            await LoadOutgoingCommitsAsync();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error initializing: {ex.Message}";
        }
    }

    private async Task LoadOutgoingCommitsAsync()
    {
        int version = ++_previewVersion;
        TxtStatusMessage.Text = "Calculating outgoing commits...";
        string remote = CboRemote.SelectedItem?.ToString() ?? "origin";
        string branch = TxtTargetBranch.Text.Trim();
        if (string.IsNullOrEmpty(branch)) branch = _currentBranch;

        var outgoing = await _gitService.GetOutgoingCommitsAsync(branch, remote);
        if (version != _previewVersion) return;
        _outgoingCommits = outgoing;
        TxtStatusMessage.Text = "";

        if (_outgoingCommits.Count == 0)
        {
            OutgoingCommitsListView.ItemsSource = null;
            PanelUpToDate.Visibility = Visibility.Visible;
            TxtCommitCountBadge.Text = "0 commits (Up to date)";
            TxtCommitCountBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EC7A6"));
            BadgeCommitCount.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EC7A6"));
            BtnPush.Content = "Push";
            BtnPush.IsEnabled = !_pushing;
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
            BtnPush.IsEnabled = !_pushing;

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
        if (IsLoaded && !_initializing)
        {
            await LoadOutgoingCommitsAsync();
        }
    }

    private async void OnPushClicked(object sender, RoutedEventArgs e)
    {
        if (_pushing) return;
        _pushing = true;
        PushContent.IsEnabled = false;
        LoadingHost.Visibility = Visibility.Visible;
        CboRemote.IsEnabled = false;
        TxtTargetBranch.IsEnabled = false;
        try { await PerformPushAsync(); }
        catch (Exception ex) { TxtStatusMessage.Text = $"Push failed: {ex.Message}"; }
        finally
        {
            _pushing = false;
            LoadingHost.Visibility = Visibility.Collapsed;
            PushContent.IsEnabled = true;
            CboRemote.IsEnabled = true;
            TxtTargetBranch.IsEnabled = true;
            BtnPush.IsEnabled = true;
            BtnCancel.IsEnabled = true;
        }
    }

    private async void OnTargetBranchLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_initializing && !_pushing) await LoadOutgoingCommitsAsync();
    }

    private async Task PerformPushAsync()
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
                string remoteUrl = await _gitService.GetRemoteUrlAsync(remote, forPush: true);
                if (await ConnectionAuthentication.PromptAsync(this, _gitService, remoteUrl, error))
                {
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
