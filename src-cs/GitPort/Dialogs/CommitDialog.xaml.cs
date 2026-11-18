using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitPort.Models;
using GitPort.Services;

namespace GitPort.Dialogs;

public class CommitFileItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public string Path { get; set; } = "";
    public string Status { get; set; } = "M";
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class CommitDialog : Window
{
    private readonly GitService _gitService;
    private readonly string _currentBranch;
    private List<CommitFileItem> _fileItems = new();
    private CommitFileItem? _selectedFile;
    private string _currentRawDiff = "";

    public bool ShouldPush { get; private set; }

    public CommitDialog(GitService gitService, string currentBranch)
    {
        InitializeComponent();
        _gitService = gitService;
        _currentBranch = currentBranch;
        TxtBranchName.Text = currentBranch;

        Loaded += async (_, _) => await InitializeDataAsync();
    }

    private async Task InitializeDataAsync()
    {
        try
        {
            var changes = await _gitService.GetWorkingTreeChangesAsync();
            if (changes.Count == 0)
            {
                PanelNoChanges.Visibility = Visibility.Visible;
                BtnCommit.IsEnabled = false;
                BtnCommitAndPush.IsEnabled = false;
                UpdateSelectionSummary();
                return;
            }

            PanelNoChanges.Visibility = Visibility.Collapsed;
            _fileItems = changes.DistinctBy(c => c.Path).Select(c => new CommitFileItem
            {
                Path = c.Path,
                Status = c.Status,
                IsSelected = true
            }).ToList();

            ChangesListView.ItemsSource = _fileItems;
            UpdateSelectionSummary();

            if (_fileItems.Count > 0)
            {
                ChangesListView.SelectedIndex = 0;
            }

            TxtCommitMessage.Focus();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error loading changes: {ex.Message}";
        }
    }

    private void UpdateSelectionSummary()
    {
        int selected = _fileItems.Count(f => f.IsSelected);
        int total = _fileItems.Count;
        TxtFilesSummaryBadge.Text = $"{selected} of {total} selected";
    }

    private void OnFileCheckboxClicked(object sender, RoutedEventArgs e)
    {
        UpdateSelectionSummary();
    }

    private void OnSelectAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var item in _fileItems)
        {
            item.IsSelected = true;
        }
        UpdateSelectionSummary();
    }

    private void OnUnselectAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var item in _fileItems)
        {
            item.IsSelected = false;
        }
        UpdateSelectionSummary();
    }

    private async void OnSelectedFileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChangesListView.SelectedItem is CommitFileItem file)
        {
            _selectedFile = file;
            TxtDiffFilePath.Text = file.Path;
            await LoadDiffForFileAsync(file.Path);
        }
    }

    private async Task LoadDiffForFileAsync(string filePath)
    {
        try
        {
            _currentRawDiff = await _gitService.GetCommitPreviewDiffAsync(filePath);
            var doc = SideBySideDiffBuilder.Build(_currentRawDiff, filePath);
            UnifiedDiffItemsControl.ItemsSource = doc.UnifiedLines;
        }
        catch (Exception ex)
        {
            UnifiedDiffItemsControl.ItemsSource = new List<DiffLineItem>
            {
                new() { Text = $"Error loading diff: {ex.Message}", TextBrush = System.Windows.Media.Brushes.Red }
            };
        }
    }

    private void OnOpenInDiffWindowClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedFile != null && !string.IsNullOrEmpty(_currentRawDiff))
        {
            var win = new DiffWindow(_selectedFile.Path, "Working Tree Changes", _currentRawDiff)
            {
                Owner = this
            };
            win.ShowDialog();
        }
    }

    private async void OnAmendCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (ChkAmend.IsChecked == true)
        {
            var commits = await _gitService.GetCommitsAsync(max: 1);
            if (commits.Count > 0)
            {
                TxtCommitMessage.Text = commits[0].Subject;
            }
        }
    }

    private void OnMessagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift || (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                OnCommitAndPushClicked(sender, e);
            }
            else
            {
                OnCommitClicked(sender, e);
            }
            e.Handled = true;
        }
    }

    private async void OnCommitClicked(object sender, RoutedEventArgs e)
    {
        await PerformCommitAsync(pushAfter: false);
    }

    private async void OnCommitAndPushClicked(object sender, RoutedEventArgs e)
    {
        await PerformCommitAsync(pushAfter: true);
    }

    private async Task PerformCommitAsync(bool pushAfter)
    {
        string message = TxtCommitMessage.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            TxtStatusMessage.Text = "Please enter a commit message.";
            TxtCommitMessage.Focus();
            return;
        }

        var selectedFiles = _fileItems.Where(f => f.IsSelected).Select(f => f.Path).ToList();
        if (selectedFiles.Count == 0 && ChkAmend.IsChecked != true)
        {
            TxtStatusMessage.Text = "Please select at least one file to commit.";
            return;
        }

        BtnCommit.IsEnabled = false;
        BtnCommitAndPush.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        TxtStatusMessage.Text = "Committing changes...";

        try
        {
            bool amend = ChkAmend.IsChecked == true;
            var (ok, output) = await _gitService.CommitFilesAsync(message, selectedFiles, amend);

            if (!ok)
            {
                TxtStatusMessage.Text = $"Commit failed: {output}";
                MessageBox.Show(this, $"Commit failed:\n\n{output}", "Git Commit Error", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnCommit.IsEnabled = true;
                BtnCommitAndPush.IsEnabled = true;
                BtnCancel.IsEnabled = true;
                return;
            }

            ShouldPush = pushAfter;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error: {ex.Message}";
            BtnCommit.IsEnabled = true;
            BtnCommitAndPush.IsEnabled = true;
            BtnCancel.IsEnabled = true;
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
