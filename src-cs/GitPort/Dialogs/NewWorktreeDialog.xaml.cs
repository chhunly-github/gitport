using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace GitPort.Dialogs;

public partial class NewWorktreeDialog : Window
{
    public string LocationPath { get; private set; } = "";
    public string BranchName { get; private set; } = "";
    public string BaseRef { get; private set; } = "";
    public bool CreateNewBranch { get; private set; } = true;
    public bool Detach { get; private set; } = false;
    public bool OpenTabImmediately { get; private set; } = true;

    public NewWorktreeDialog(
        string repoPath,
        string? defaultBranch = null,
        string? defaultBase = null,
        bool isExistingBranch = false,
        List<string>? availableBranches = null)
    {
        InitializeComponent();

        if (availableBranches != null && availableBranches.Count > 0)
        {
            CboExistingBranch.ItemsSource = availableBranches;
        }

        string parentDir = Directory.GetParent(repoPath)?.FullName ?? repoPath;
        string repoName = Path.GetFileName(repoPath);

        string rawSlug = !string.IsNullOrEmpty(defaultBranch) ? defaultBranch : (defaultBase ?? "worktree");
        string safeSlug = rawSlug.Replace('/', '-').Replace('\\', '-').Trim();
        if (safeSlug.StartsWith("refs-heads-")) safeSlug = safeSlug.Substring(11);
        if (safeSlug.StartsWith("refs-remotes-")) safeSlug = safeSlug.Substring(13);
        if (string.IsNullOrWhiteSpace(safeSlug) || safeSlug == "HEAD") safeSlug = "worktree-1";

        string candidate = Path.Combine(parentDir, $"{repoName}-{safeSlug}");
        int counter = 1;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parentDir, $"{repoName}-{safeSlug}-{counter++}");
        }
        TxtLocation.Text = candidate;

        if (isExistingBranch)
        {
            RadioExisting.IsChecked = true;
            CboExistingBranch.Text = defaultBranch ?? defaultBase ?? "main";
            TxtNewBranch.Text = $"worktree-{safeSlug}";
            TxtBaseRef.Text = defaultBase ?? "HEAD";
        }
        else
        {
            RadioNew.IsChecked = true;
            TxtNewBranch.Text = !string.IsNullOrEmpty(defaultBranch) ? defaultBranch : (defaultBase != null && defaultBase != "HEAD" ? $"worktree-{safeSlug}" : "feature/worktree");
            TxtBaseRef.Text = defaultBase ?? "HEAD";
            if (availableBranches != null && availableBranches.Count > 0)
            {
                CboExistingBranch.SelectedIndex = 0;
            }
        }

        UpdateControlStates();
    }

    private void OnBranchChoiceChanged(object sender, RoutedEventArgs e)
    {
        UpdateControlStates();
    }

    private void UpdateControlStates()
    {
        if (CboExistingBranch == null || GridNewBranch == null) return;

        bool isExisting = RadioExisting.IsChecked == true;
        CboExistingBranch.IsEnabled = isExisting;
        CboExistingBranch.Opacity = isExisting ? 1.0 : 0.4;

        GridNewBranch.IsEnabled = !isExisting;
        GridNewBranch.Opacity = !isExisting ? 1.0 : 0.4;
    }

    private void OnBrowseLocation(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Directory for Worktree",
            InitialDirectory = Path.GetDirectoryName(TxtLocation.Text)
        };

        if (dialog.ShowDialog(this) == true)
        {
            TxtLocation.Text = dialog.FolderName;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        LocationPath = TxtLocation.Text.Trim();
        if (string.IsNullOrWhiteSpace(LocationPath))
        {
            MessageBox.Show(this, "Please specify a location folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Directory.Exists(LocationPath))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(LocationPath).Any())
                {
                    MessageBox.Show(this,
                        $"The target directory already exists and is not empty:\n{LocationPath}\n\nPlease select an empty or new folder.",
                        "Directory Not Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error checking directory:\n{ex.Message}", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        CreateNewBranch = RadioNew.IsChecked == true;
        if (CreateNewBranch)
        {
            BranchName = TxtNewBranch.Text.Trim();
            BaseRef = TxtBaseRef.Text.Trim();

            if (string.IsNullOrWhiteSpace(BranchName))
            {
                MessageBox.Show(this, "Please enter a new branch name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            char[] illegalChars = { ' ', '~', '^', ':', '?', '*', '[', '\\' };
            if (BranchName.IndexOfAny(illegalChars) >= 0 || BranchName.StartsWith('/') || BranchName.EndsWith('/') || BranchName.Contains(".."))
            {
                MessageBox.Show(this,
                    $"Branch name '{BranchName}' contains invalid characters.\nBranches cannot contain spaces, ~, ^, :, ?, *, [, \\, .., or start/end with slashes.",
                    "Invalid Branch Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(BaseRef))
            {
                BaseRef = "HEAD";
            }
        }
        else
        {
            BranchName = CboExistingBranch.Text.Trim();
            BaseRef = "";

            if (string.IsNullOrWhiteSpace(BranchName))
            {
                MessageBox.Show(this, "Please select or enter an existing branch to checkout.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        OpenTabImmediately = ChkOpenTab.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
