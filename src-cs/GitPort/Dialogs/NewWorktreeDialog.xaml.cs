using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GitPort.Dialogs;

public partial class NewWorktreeDialog : Window
{
    public string LocationPath { get; private set; } = "";
    public string BranchName { get; private set; } = "";
    public string BaseRef { get; private set; } = "";

    public NewWorktreeDialog(string repoPath, string? defaultBranch = null, string? defaultBase = null)
    {
        InitializeComponent();

        string parentDir = Directory.GetParent(repoPath)?.FullName ?? repoPath;
        string defaultPath = Path.Combine(parentDir, $"{Path.GetFileName(repoPath)}-worktree");
        TxtLocation.Text = defaultPath;
        TxtBranch.Text = defaultBranch ?? "new-worktree-branch";
        TxtBaseRef.Text = defaultBase ?? "HEAD";
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
        BranchName = TxtBranch.Text.Trim();
        BaseRef = TxtBaseRef.Text.Trim();

        if (string.IsNullOrEmpty(LocationPath))
        {
            MessageBox.Show(this, "Please specify a location folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
