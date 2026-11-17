using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using GitPort.Controls;
using GitPort.Dialogs;
using GitPort.Models;
using GitPort.Services;

namespace GitPort;

public partial class MainWindow : Window
{
    private GitService _gitService;
    private readonly RepositoryCatalogService _catalog;
    private readonly ObservableCollection<RepoTabItem> _openTabs = new();
    private RepoTabItem? _activeTab;

    private string _currentBranch = "";
    private string _headCommitHash = "";
    private string _activeFilterRef = "ALL";
    private List<GitCommitItem> _allCommits = new();
    private List<GitWorktree> _worktrees = new();
    private List<string> _branchRefs = new();

    private readonly ObservableCollection<WorkingTreeItem> _stagedItems = new();
    private readonly ObservableCollection<WorkingTreeItem> _unstagedItems = new();
    private WorkingTreeItem? _selectedWorkingTreeItem;
    private bool _isUpdatingWorkingTreeSelection = false;

    public MainWindow()
    {
        InitializeComponent();

        _catalog = new RepositoryCatalogService();
        string initialRepo = FindInitialRepository();
        _gitService = new GitService(string.IsNullOrEmpty(initialRepo) ? Directory.GetCurrentDirectory() : initialRepo);

        RepoTabsItemsControl.ItemsSource = _openTabs;
        StagedListView.ItemsSource = _stagedItems;
        UnstagedListView.ItemsSource = _unstagedItems;

        CatalogGroupsItemsControl.ItemsSource = _catalog.BuildGroups(initialRepo);

        Loaded += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(initialRepo) && (Directory.Exists(Path.Combine(initialRepo, ".git")) || File.Exists(Path.Combine(initialRepo, ".git"))))
            {
                await OpenRepositoryAsTabAsync(initialRepo);
            }
            else
            {
                ShowCatalogView();
            }
        };
    }

    private string FindInitialRepository()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            return Path.GetFullPath(args[1]);
        }

        string last = _catalog.GetLastRepo();
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
        {
            return Path.GetFullPath(last);
        }

        string current = Directory.GetCurrentDirectory();
        if (Directory.Exists(current) && (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git"))))
        {
            return Path.GetFullPath(current);
        }

        return "";
    }

    #region SourceTree Multi-Repository Tab Management

    public async Task OpenRepositoryAsTabAsync(string repoPath)
    {
        string fullPath = Path.GetFullPath(repoPath);
        if (!Directory.Exists(fullPath)) return;

        var existing = _openTabs.FirstOrDefault(t => string.Equals(t.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            await SwitchToTabAsync(existing);
            return;
        }

        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(name)) name = fullPath;

        var tab = new RepoTabItem
        {
            Path = fullPath,
            Name = name,
            CurrentBranch = "...",
            IsActive = true
        };

        foreach (var t in _openTabs) t.IsActive = false;
        _openTabs.Add(tab);
        _activeTab = tab;

        await SwitchToTabAsync(tab);
    }

    private async Task SwitchToTabAsync(RepoTabItem tab)
    {
        _activeTab = tab;
        foreach (var t in _openTabs)
        {
            t.IsActive = (t == tab);
        }

        ViewCatalog.Visibility = Visibility.Collapsed;
        ViewWorkspace.Visibility = Visibility.Visible;

        await LoadRepositoryAsync(tab.Path);
    }

    public void ShowCatalogView()
    {
        _activeTab = null;
        foreach (var t in _openTabs) t.IsActive = false;

        ViewCatalog.Visibility = Visibility.Visible;
        ViewWorkspace.Visibility = Visibility.Collapsed;

        CatalogGroupsItemsControl.ItemsSource = _catalog.BuildGroups("");
    }

    private void OnTabCatalogClicked(object sender, MouseButtonEventArgs e)
    {
        ShowCatalogView();
    }

    private async void OnRepoTabClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RepoTabItem tab)
        {
            await SwitchToTabAsync(tab);
        }
    }

    private async Task CloseTabAsync(RepoTabItem tab)
    {
        int index = _openTabs.IndexOf(tab);
        _openTabs.Remove(tab);

        if (tab == _activeTab)
        {
            if (_openTabs.Count > 0)
            {
                int nextIndex = Math.Clamp(index - 1, 0, _openTabs.Count - 1);
                await SwitchToTabAsync(_openTabs[nextIndex]);
            }
            else
            {
                ShowCatalogView();
            }
        }
    }

    private async void OnCloseTabClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is RepoTabItem tab)
        {
            await CloseTabAsync(tab);
        }
    }

    private void OnRepoTabContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RepoTabItem tab)
        {
            var menu = new ContextMenu();

            var closeTab = new MenuItem { Header = $"Close '{tab.Name}'" };
            closeTab.Click += async (_, _) => await CloseTabAsync(tab);
            menu.Items.Add(closeTab);

            var closeOthers = new MenuItem { Header = "Close Other Tabs" };
            closeOthers.Click += async (_, _) =>
            {
                var others = _openTabs.Where(t => t != tab).ToList();
                foreach (var o in others) _openTabs.Remove(o);
                await SwitchToTabAsync(tab);
            };
            menu.Items.Add(closeOthers);

            var closeRight = new MenuItem { Header = "Close Tabs to the Right" };
            closeRight.Click += (_, _) =>
            {
                int idx = _openTabs.IndexOf(tab);
                if (idx >= 0)
                {
                    var toRemove = _openTabs.Skip(idx + 1).ToList();
                    foreach (var r in toRemove) _openTabs.Remove(r);
                }
            };
            menu.Items.Add(closeRight);

            menu.Items.Add(new Separator());

            var explorerItem = new MenuItem { Header = "Show in File Explorer" };
            explorerItem.Click += (_, _) => ShowInFileExplorer(tab.Path);
            menu.Items.Add(explorerItem);

            var copyPathItem = new MenuItem { Header = "Copy Repository Path" };
            copyPathItem.Click += (_, _) => SafeSetClipboard(tab.Path);
            menu.Items.Add(copyPathItem);

            menu.Items.Add(new Separator());

            var removeCatalogItem = new MenuItem { Header = "Remove from GitPort Catalog..." };
            removeCatalogItem.Click += (_, _) => ConfirmAndRemoveRepository(tab.Path, tab.Name);
            menu.Items.Add(removeCatalogItem);

            fe.ContextMenu = menu;
        }
    }

    private async void OnOpenRepoClicked(object sender, RoutedEventArgs e)
    {
        string? selected = PickNativeFolder("Select Git Repository", _activeTab?.Path);
        if (!string.IsNullOrEmpty(selected) && Directory.Exists(selected))
        {
            _catalog.AddRepository(selected);
            await OpenRepositoryAsTabAsync(selected);
        }
    }

    private string? PickNativeFolder(string title, string? initialFolder)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = initialFolder ?? ""
            };
            if (dialog.ShowDialog(this) == true)
            {
                return dialog.FolderName;
            }
        }
        catch
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string picker = Path.Combine(appDir, "FolderPicker.exe");
            if (File.Exists(picker))
            {
                var psi = new ProcessStartInfo(picker, $"\"{title}\" \"{initialFolder}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string outText = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && !string.IsNullOrEmpty(outText) && Directory.Exists(outText))
                    {
                        return outText;
                    }
                }
            }
        }
        return null;
    }

    private async void OnCatalogCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RepoItem repo)
        {
            await OpenRepositoryAsTabAsync(repo.Path);
        }
    }

    private async void OnOpenTabFromCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RepoItem repo)
        {
            await OpenRepositoryAsTabAsync(repo.Path);
        }
    }

    private void OnCatalogSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtCatalogSearch.Text.Trim();
        var groups = _catalog.BuildGroups(_activeTab?.Path ?? "");

        if (!string.IsNullOrEmpty(query))
        {
            foreach (var g in groups)
            {
                var filtered = g.Repositories.Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                g.Repositories.Clear();
                foreach (var r in filtered) g.Repositories.Add(r);
            }
        }

        CatalogGroupsItemsControl.ItemsSource = groups;
    }

    private void OnNewGroupClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new InputBoxDialog("New Repository Group", "Group name:", "Projects");
        dlg.Owner = this;
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputValue))
        {
            _catalog.CreateGroup(dlg.InputValue.Trim());
            CatalogGroupsItemsControl.ItemsSource = _catalog.BuildGroups(_activeTab?.Path ?? "");
        }
    }

    private void ConfirmAndRemoveRepository(string repoPath, string repoName)
    {
        var result = MessageBox.Show(
            $"Remove '{repoName}' from the GitPort catalog?\n\nPath: {repoPath}\n\n(This only removes the bookmark from GitPort. Local files and Git history will NOT be deleted.)",
            "Remove Repository from Catalog",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _catalog.RemoveRepository(repoPath);

            // If it is currently open in a tab, close that tab
            var openTab = _openTabs.FirstOrDefault(t => string.Equals(t.Path, repoPath, StringComparison.OrdinalIgnoreCase));
            if (openTab != null)
            {
                _ = CloseTabAsync(openTab);
            }

            CatalogGroupsItemsControl.ItemsSource = _catalog.BuildGroups(_activeTab?.Path ?? "");
        }
    }

    private void OnRemoveRepoFromCardClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is RepoItem repo)
        {
            ConfirmAndRemoveRepository(repo.Path, repo.Name);
        }
    }

    private void OnCardMenuClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is RepoItem repo)
        {
            var menu = BuildCatalogCardMenu(repo);
            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }
    }

    private void OnCatalogCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RepoItem repo)
        {
            fe.ContextMenu = BuildCatalogCardMenu(repo);
        }
    }

    private ContextMenu BuildCatalogCardMenu(RepoItem repo)
    {
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Open as Tab" };
        openItem.Click += async (_, _) => await OpenRepositoryAsTabAsync(repo.Path);
        menu.Items.Add(openItem);

        var expItem = new MenuItem { Header = "Show in File Explorer" };
        expItem.Click += (_, _) => ShowInFileExplorer(repo.Path);
        menu.Items.Add(expItem);

        var termItem = new MenuItem { Header = "Open Terminal Here" };
        termItem.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{repo.Path}\"") { UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/k cd /d \"{repo.Path}\"") { UseShellExecute = true });
            }
        };
        menu.Items.Add(termItem);

        var copyPathItem = new MenuItem { Header = "Copy Repository Path" };
        copyPathItem.Click += (_, _) => SafeSetClipboard(repo.Path);
        menu.Items.Add(copyPathItem);

        menu.Items.Add(new Separator());

        var removeItem = new MenuItem { Header = "Remove from GitPort Catalog..." };
        removeItem.Click += (_, _) => ConfirmAndRemoveRepository(repo.Path, repo.Name);
        menu.Items.Add(removeItem);

        return menu;
    }

    private void OnCleanMissingReposClicked(object sender, RoutedEventArgs e)
    {
        var missing = _catalog.GetMissingRepositories();
        if (missing.Count == 0)
        {
            MessageBox.Show("All bookmarked repositories currently exist on disk.", "Clean Missing Repositories", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string list = string.Join("\n", missing.Select(p => $"• {p}"));
        var result = MessageBox.Show(
            $"Found {missing.Count} missing repository path(s) that no longer exist on disk:\n\n{list}\n\nRemove these missing entries from GitPort?",
            "Clean Missing Repositories",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _catalog.PruneMissingRepositories();
            CatalogGroupsItemsControl.ItemsSource = _catalog.BuildGroups(_activeTab?.Path ?? "");
            MessageBox.Show($"Removed {missing.Count} missing repository entry/entries from the catalog.", "Clean Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Repository Loading

    private async Task LoadRepositoryAsync(string repoPath)
    {
        try
        {
            TxtStatusMessage.Text = "Loading repository...";
            _gitService.SetRepository(repoPath);
            _catalog.AddRepository(repoPath);
            _catalog.SetLastRepo(repoPath);

            string repoName = Path.GetFileName(repoPath);
            if (string.IsNullOrEmpty(repoName)) repoName = repoPath;
            TxtWorkspaceRepoName.Text = repoName;
            TxtStatusRepo.Text = repoPath;
            Title = $"GitPort - {repoName} ({repoPath})";

            // 1. Current branch & HEAD commit
            _currentBranch = await _gitService.GetCurrentBranchAsync();
            _headCommitHash = await _gitService.GetHeadCommitHashAsync();
            TxtStatusBranch.Text = _currentBranch;
            if (_activeTab != null)
            {
                _activeTab.CurrentBranch = _currentBranch;
            }

            // 2. Worktrees & Branch Refs
            _worktrees = await _gitService.GetWorktreesAsync();
            _branchRefs = await _gitService.GetBranchRefsAsync();

            var incomingMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in new[] { "master", "main", _currentBranch })
            {
                int count = await _gitService.GetIncomingCountAsync(b);
                if (count > 0) incomingMap[b] = count;
            }

            var branchTree = BranchTreeBuilder.BuildTree(_currentBranch, _worktrees, _branchRefs, incomingMap);
            BranchesTreeView.ItemsSource = branchTree;

            // 3. Load Working Tree status
            await RefreshWorkingTreeAsync();

            // 4. Load Commits Log
            _activeFilterRef = "ALL";
            TxtBranchFilterLabel.Text = "All Branches";
            await LoadCommitsForRefAsync(_activeFilterRef);

            // 5. Update Outgoing Commits Push Status
            await UpdatePushStatusAsync();

            TxtStatusMessage.Text = "Ready";
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error: {ex.Message}";
            MessageBox.Show(this, $"Failed to load repository:\n{ex.Message}", "Git Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadCommitsForRefAsync(string targetRef)
    {
        try
        {
            TxtStatusMessage.Text = $"Loading log for {targetRef}...";
            _activeFilterRef = targetRef;

            var commits = await _gitService.GetCommitsAsync(targetRef, 500);
            _allCommits = GraphService.BuildGraph(commits, _currentBranch, _headCommitHash);
            CommitsListView.ItemsSource = _allCommits;

            if (_allCommits.Count > 0)
            {
                int headIndex = _allCommits.FindIndex(c => c.IsHead);
                CommitsListView.SelectedIndex = (headIndex >= 0) ? headIndex : 0;
            }
            else
            {
                TxtFileCountHeader.Text = "0 files";
                FilesTreeView.ItemsSource = null;
                TxtDetailSubject.Text = "No commits in view";
                TxtDetailAuthor.Text = "";
                TxtDetailBranches.Text = "";
            }
            TxtStatusMessage.Text = "Ready";
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region Subtab Switching (Log vs Working Tree)

    private void OnSubtabLogClicked(object sender, RoutedEventArgs e)
    {
        ViewLog.Visibility = Visibility.Visible;
        ViewWorkingTree.Visibility = Visibility.Collapsed;
        PanelLogFilters.Visibility = Visibility.Visible;

        BtnSubtabLog.Background = (Brush)FindResource("BrushPanel");
        BtnSubtabWorkingTree.Background = Brushes.Transparent;
    }

    private async void OnSubtabWorkingTreeClicked(object sender, RoutedEventArgs e)
    {
        ViewLog.Visibility = Visibility.Collapsed;
        ViewWorkingTree.Visibility = Visibility.Visible;
        PanelLogFilters.Visibility = Visibility.Collapsed;

        BtnSubtabWorkingTree.Background = (Brush)FindResource("BrushPanel");
        BtnSubtabLog.Background = Brushes.Transparent;

        await RefreshWorkingTreeAsync();
    }

    #endregion

    #region Working Tree Staging, Side-by-Side Diff & Commit

    private async Task RefreshWorkingTreeAsync()
    {
        try
        {
            var rawChanges = await _gitService.GetWorkingTreeChangesAsync();

            _stagedItems.Clear();
            _unstagedItems.Clear();

            foreach (var ch in rawChanges)
            {
                var item = new WorkingTreeItem
                {
                    Status = ch.Status,
                    Path = ch.Path,
                    IsStaged = ch.IsStaged
                };

                if (ch.IsStaged)
                {
                    _stagedItems.Add(item);
                }
                else
                {
                    _unstagedItems.Add(item);
                }
            }

            int stagedCount = _stagedItems.Count;
            int unstagedCount = _unstagedItems.Count;
            int total = stagedCount + unstagedCount;

            TxtStagedCount.Text = $"({stagedCount})";
            TxtUnstagedCount.Text = $"({unstagedCount})";
            TxtWorkingTreeBadge.Text = total.ToString();
            TxtWorkingTreeFileCount.Text = $"({total})";
            BadgeWorkingTreeCount.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;

            BtnCommit.Content = stagedCount > 0 ? $"Commit ({stagedCount})" : "Commit";

            // Re-select or select first
            if (total > 0)
            {
                if (_selectedWorkingTreeItem != null)
                {
                    var match = _stagedItems.FirstOrDefault(i => i.Path == _selectedWorkingTreeItem.Path)
                                ?? _unstagedItems.FirstOrDefault(i => i.Path == _selectedWorkingTreeItem.Path);
                    if (match != null)
                    {
                        await LoadWorkingTreeDiffAsync(match);
                        return;
                    }
                }

                if (_unstagedItems.Count > 0)
                {
                    UnstagedListView.SelectedIndex = 0;
                }
                else if (_stagedItems.Count > 0)
                {
                    StagedListView.SelectedIndex = 0;
                }
            }
            else
            {
                TxtDiffFileTitle.Text = "Working tree clean - No changes";
                TxtDiffStatsAdd.Text = "+0";
                TxtDiffStatsDel.Text = "-0";
                WorkingTreeSideBySideListView.ItemsSource = null;
                WorkingTreeUnifiedItemsControl.ItemsSource = null;
            }
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Working tree error: {ex.Message}";
        }
    }

    private async void OnStagedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingWorkingTreeSelection) return;
        if (StagedListView.SelectedItem is WorkingTreeItem item)
        {
            try
            {
                _isUpdatingWorkingTreeSelection = true;
                UnstagedListView.SelectedItem = null;
            }
            finally
            {
                _isUpdatingWorkingTreeSelection = false;
            }

            _selectedWorkingTreeItem = item;
            await LoadWorkingTreeDiffAsync(item);
        }
    }

    private async void OnUnstagedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingWorkingTreeSelection) return;
        if (UnstagedListView.SelectedItem is WorkingTreeItem item)
        {
            try
            {
                _isUpdatingWorkingTreeSelection = true;
                StagedListView.SelectedItem = null;
            }
            finally
            {
                _isUpdatingWorkingTreeSelection = false;
            }

            _selectedWorkingTreeItem = item;
            await LoadWorkingTreeDiffAsync(item);
        }
    }

    private async void OnItemStageCheckboxClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is WorkingTreeItem item)
        {
            if (cb.IsChecked == true)
            {
                TxtStatusMessage.Text = $"Staging {item.FileName}...";
                await _gitService.StageFilesAsync(new[] { item.Path });
            }
            else
            {
                TxtStatusMessage.Text = $"Unstaging {item.FileName}...";
                await _gitService.UnstageFilesAsync(new[] { item.Path });
            }
            await RefreshWorkingTreeAsync();
            TxtStatusMessage.Text = "Ready";
        }
    }

    private async void OnStageItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WorkingTreeItem item)
        {
            TxtStatusMessage.Text = $"Staging {item.FileName}...";
            await _gitService.StageFilesAsync(new[] { item.Path });
            await RefreshWorkingTreeAsync();
            TxtStatusMessage.Text = "Ready";
        }
    }

    private async void OnUnstageItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WorkingTreeItem item)
        {
            TxtStatusMessage.Text = $"Unstaging {item.FileName}...";
            await _gitService.UnstageFilesAsync(new[] { item.Path });
            await RefreshWorkingTreeAsync();
            TxtStatusMessage.Text = "Ready";
        }
    }

    private void OnWorkingTreeFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as ListView)?.SelectedItem as WorkingTreeItem;
        if (item != null)
        {
            string fullPath = Path.Combine(_gitService.RepositoryPath, item.Path);
            OpenFileWithDefaultProgram(fullPath);
        }
    }

    private void OnWorkingTreePreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item != null)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    private void OnStagedContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (StagedListView.SelectedItem is not WorkingTreeItem item)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        string fullPath = Path.Combine(_gitService.RepositoryPath, item.Path);

        var unstageItem = new MenuItem { Header = $"Unstage '{item.FileName}'" };
        unstageItem.Click += async (_, _) =>
        {
            await _gitService.UnstageFilesAsync(new[] { item.Path });
            await RefreshWorkingTreeAsync();
        };
        menu.Items.Add(unstageItem);

        var diffItem = new MenuItem { Header = "Show Diff in Window" };
        diffItem.Click += async (_, _) =>
        {
            string rawDiff = await _gitService.GetWorkingTreeFileDiffAsync(item.Path, staged: true);
            var diffWin = new DiffWindow(item.Path, "Staged Changes", rawDiff) { Owner = this };
            diffWin.Show();
        };
        menu.Items.Add(diffItem);

        menu.Items.Add(new Separator());

        var openFileItem = new MenuItem { Header = "Open File" };
        openFileItem.Click += (_, _) => OpenFileWithDefaultProgram(fullPath);
        menu.Items.Add(openFileItem);

        var explorerItem = new MenuItem { Header = "Show in File Explorer" };
        explorerItem.Click += (_, _) => ShowInFileExplorer(fullPath);
        menu.Items.Add(explorerItem);

        menu.Items.Add(new Separator());

        var copyRelItem = new MenuItem { Header = "Copy Relative Path" };
        copyRelItem.Click += (_, _) => SafeSetClipboard(item.Path);
        menu.Items.Add(copyRelItem);

        var copyFullItem = new MenuItem { Header = "Copy Absolute Path" };
        copyFullItem.Click += (_, _) => SafeSetClipboard(fullPath);
        menu.Items.Add(copyFullItem);

        StagedListView.ContextMenu = menu;
    }

    private void OnUnstagedContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (UnstagedListView.SelectedItem is not WorkingTreeItem item)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        string fullPath = Path.Combine(_gitService.RepositoryPath, item.Path);

        var stageItem = new MenuItem { Header = $"Stage '{item.FileName}'" };
        stageItem.Click += async (_, _) =>
        {
            await _gitService.StageFilesAsync(new[] { item.Path });
            await RefreshWorkingTreeAsync();
        };
        menu.Items.Add(stageItem);

        var discardItem = new MenuItem { Header = $"Discard Changes in '{item.FileName}'..." };
        discardItem.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this,
                $"Are you sure you want to discard changes in '{item.FileName}'?\nThis will revert tracked changes or delete untracked files.",
                "Discard Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                await _gitService.DiscardFilesAsync(new[] { item.Path });
                await RefreshWorkingTreeAsync();
            }
        };
        menu.Items.Add(discardItem);

        menu.Items.Add(new Separator());

        var diffItem = new MenuItem { Header = "Show Diff in Window" };
        diffItem.Click += async (_, _) =>
        {
            string rawDiff = await _gitService.GetWorkingTreeFileDiffAsync(item.Path, staged: false);
            var diffWin = new DiffWindow(item.Path, "Unstaged Changes", rawDiff) { Owner = this };
            diffWin.Show();
        };
        menu.Items.Add(diffItem);

        var openFileItem = new MenuItem { Header = "Open File" };
        openFileItem.Click += (_, _) => OpenFileWithDefaultProgram(fullPath);
        menu.Items.Add(openFileItem);

        var explorerItem = new MenuItem { Header = "Show in File Explorer" };
        explorerItem.Click += (_, _) => ShowInFileExplorer(fullPath);
        menu.Items.Add(explorerItem);

        menu.Items.Add(new Separator());

        var copyRelItem = new MenuItem { Header = "Copy Relative Path" };
        copyRelItem.Click += (_, _) => SafeSetClipboard(item.Path);
        menu.Items.Add(copyRelItem);

        var copyFullItem = new MenuItem { Header = "Copy Absolute Path" };
        copyFullItem.Click += (_, _) => SafeSetClipboard(fullPath);
        menu.Items.Add(copyFullItem);

        UnstagedListView.ContextMenu = menu;
    }

    internal async Task LoadWorkingTreeDiffAsync(WorkingTreeItem item)
    {
        try
        {
            bool staged = item.IsStaged || RbDiffStaged.IsChecked == true;
            string rawDiff = await _gitService.GetWorkingTreeFileDiffAsync(item.Path, staged);
            var doc = SideBySideDiffBuilder.Build(rawDiff, item.Path);

            TxtDiffFileTitle.Text = $"{item.FileName} ({(staged ? "Staged" : "Unstaged")}) - {item.DirectoryPath}";
            TxtDiffStatsAdd.Text = $"+{doc.AdditionsCount}";
            TxtDiffStatsDel.Text = $"-{doc.DeletionsCount}";

            WorkingTreeSideBySideListView.ItemsSource = doc.SideBySideRows;
            WorkingTreeUnifiedItemsControl.ItemsSource = doc.UnifiedLines;
        }
        catch (Exception ex)
        {
            WorkingTreeSideBySideListView.ItemsSource = null;
            WorkingTreeUnifiedItemsControl.ItemsSource = new List<DiffLineItem>
            {
                new() { Text = $"Failed to load diff: {ex.Message}", TextBrush = Brushes.Red }
            };
        }
    }

    private async void OnDiffModeChanged(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkingTreeItem != null)
        {
            await LoadWorkingTreeDiffAsync(_selectedWorkingTreeItem);
        }
    }

    private void OnDiffLayoutChanged(object sender, RoutedEventArgs e)
    {
        if (WorkingTreeSideBySideListView == null || WorkingTreeUnifiedScrollViewer == null || RbDiffSideBySide == null)
            return;

        if (RbDiffSideBySide.IsChecked == true)
        {
            WorkingTreeSideBySideListView.Visibility = Visibility.Visible;
            WorkingTreeUnifiedScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            WorkingTreeSideBySideListView.Visibility = Visibility.Collapsed;
            WorkingTreeUnifiedScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private async void OnStageAllClicked(object sender, RoutedEventArgs e)
    {
        TxtStatusMessage.Text = "Staging all changes...";
        await _gitService.StageAllAsync();
        await RefreshWorkingTreeAsync();
        TxtStatusMessage.Text = "Staged all changes.";
    }

    private async void OnUnstageAllClicked(object sender, RoutedEventArgs e)
    {
        TxtStatusMessage.Text = "Unstaging all changes...";
        await _gitService.UnstageAllAsync();
        await RefreshWorkingTreeAsync();
        TxtStatusMessage.Text = "Unstaged all changes.";
    }

    private async void OnDiscardSelectedClicked(object sender, RoutedEventArgs e)
    {
        var selected = new List<string>();

        if (UnstagedListView.SelectedItems.Count > 0)
        {
            selected.AddRange(UnstagedListView.SelectedItems.OfType<WorkingTreeItem>().Select(i => i.Path));
        }
        else if (StagedListView.SelectedItems.Count > 0)
        {
            selected.AddRange(StagedListView.SelectedItems.OfType<WorkingTreeItem>().Select(i => i.Path));
        }
        else if (_selectedWorkingTreeItem != null)
        {
            selected.Add(_selectedWorkingTreeItem.Path);
        }

        if (selected.Count == 0) return;

        var result = MessageBox.Show(this,
            $"Are you sure you want to discard changes in {selected.Count} file(s)?\nThis will revert tracked edits and remove untracked files.",
            "Discard Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            TxtStatusMessage.Text = $"Discarding {selected.Count} file(s)...";
            await _gitService.DiscardFilesAsync(selected);
            await RefreshWorkingTreeAsync();
            TxtStatusMessage.Text = "Discarded changes.";
        }
    }

    private async void OnCommitClicked(object sender, RoutedEventArgs e)
    {
        string message = TxtCommitMessage.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            MessageBox.Show(this, "Please enter a commit message.", "Commit Message Required", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtCommitMessage.Focus();
            return;
        }

        bool amend = ChkAmend.IsChecked == true;
        TxtStatusMessage.Text = amend ? "Amending commit..." : "Committing...";

        var (success, output) = await _gitService.CommitAsync(message, amend);
        if (success)
        {
            TxtCommitMessage.Clear();
            ChkAmend.IsChecked = false;
            TxtStatusMessage.Text = "Committed successfully.";

            await RefreshWorkingTreeAsync();
            _headCommitHash = await _gitService.GetHeadCommitHashAsync();
            await LoadCommitsForRefAsync(_activeFilterRef);
            await UpdatePushStatusAsync();
        }
        else
        {
            MessageBox.Show(this, $"Commit failed:\n{output}", "Git Commit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatusMessage.Text = "Commit failed.";
        }
    }

    private void OnCommitMessagePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            OnCommitClicked(sender, e);
        }
    }

    #endregion

    #region WebStorm Log & DAG Commit Graph

    private async void OnCommitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadSelectedCommitFilesAsync();
    }

    internal async Task LoadSelectedCommitFilesAsync()
    {
        if (CommitsListView.SelectedItem is GitCommitItem commit)
        {
            TxtDetailSubject.Text = commit.Subject;
            TxtDetailAuthor.Text = $"{commit.Hash.Substring(0, Math.Min(8, commit.Hash.Length))} by {commit.Author} on {commit.Date}";
            TxtDetailBranches.Text = string.IsNullOrEmpty(commit.Commit.Refs) ? "" : $"In refs: {commit.Commit.Refs}";

            try
            {
                var files = await _gitService.GetCommitFilesAsync(commit.Hash);
                TxtFileCountHeader.Text = $"{files.Count} {(files.Count == 1 ? "file" : "files")}";

                var tree = ChangeTreeBuilder.BuildTree(files);
                FilesTreeView.ItemsSource = tree;
            }
            catch (Exception ex)
            {
                TxtFileCountHeader.Text = $"Error: {ex.Message}";
            }
        }
    }

    private void OnCommitPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (item != null)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    private async Task PerformMergeAsync(string branchOrRef)
    {
        var res = MessageBox.Show(this,
            $"Merge '{branchOrRef}' into current branch '{_currentBranch}'?",
            "Merge into " + _currentBranch, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            TxtStatusMessage.Text = $"Merging {branchOrRef} into {_currentBranch}...";
            bool isRemote = branchOrRef.StartsWith("origin/") || branchOrRef.StartsWith("refs/remotes/");
            var (ok, output) = await _gitService.MergeWithOutputAsync(branchOrRef, fetchFirst: isRemote);
            if (ok)
            {
                TxtStatusMessage.Text = $"Merged {branchOrRef} into {_currentBranch}.";
                await LoadRepositoryAsync(_gitService.RepositoryPath);
                await RefreshWorkingTreeAsync();
            }
            else
            {
                MessageBox.Show(this,
                    $"Merge completed with output or conflicts:\n{output}",
                    "Git Merge Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadRepositoryAsync(_gitService.RepositoryPath);
                await RefreshWorkingTreeAsync();
            }
        }
    }

    private async Task CreatePatchAsync(string commitHash)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Commit Patch",
            Filter = "Patch files (*.patch)|*.patch|All files (*.*)|*.*",
            FileName = $"{commitHash.Substring(0, Math.Min(8, commitHash.Length))}.patch"
        };
        if (dlg.ShowDialog() == true)
        {
            var (code, stdout, _) = await _gitService.RunGitAsync("format-patch", "-1", commitHash, "--stdout");
            if (code == 0)
            {
                File.WriteAllText(dlg.FileName, stdout);
                TxtStatusMessage.Text = $"Patch saved to {Path.GetFileName(dlg.FileName)}";
            }
        }
    }

    private async Task CompareCommitWithLocalAsync(string commitHash)
    {
        TxtStatusMessage.Text = "Loading diff with local...";
        string rawDiff = await _gitService.GetDiffAsync(commitHash);
        var diffWin = new DiffWindow($"Diff: {commitHash.Substring(0, Math.Min(8, commitHash.Length))} vs Local", "Compare with Local", rawDiff) { Owner = this };
        diffWin.Show();
        TxtStatusMessage.Text = "Ready";
    }

    private List<string> GetBranchNamesForCommit(GitCommitItem commit)
    {
        var branches = new List<string>();
        foreach (var chip in commit.RefChips)
        {
            if (chip.IsTag) continue;
            string name = chip.Name.Trim();
            if (name.StartsWith("origin & "))
            {
                string b = name.Substring(9);
                branches.Add(b);
                branches.Add($"origin/{b}");
            }
            else
            {
                branches.Add(name);
            }
        }

        if (branches.Count == 0 && !string.IsNullOrEmpty(commit.Commit.Refs))
        {
            string raw = commit.Commit.Refs.Trim('(', ')', ' ');
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                string token = p.Trim();
                if (token.StartsWith("tag: ")) continue;
                if (token.StartsWith("HEAD -> ")) token = token.Substring(8);
                if (token.EndsWith("/HEAD") || token == "HEAD") continue;
                if (!branches.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    branches.Add(token);
                }
            }
        }

        return branches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private MenuItem CreateBranchSubmenu(string branchName)
    {
        var branchMenu = new MenuItem { Header = $"Branch '{branchName}'" };

        // 1. Checkout
        var checkout = new MenuItem { Header = "Checkout" };
        checkout.Click += async (_, _) =>
        {
            TxtStatusMessage.Text = $"Checking out {branchName}...";
            await _gitService.SwitchBranchAsync(branchName);
            await LoadRepositoryAsync(_gitService.RepositoryPath);
        };
        branchMenu.Items.Add(checkout);

        // 2. New Branch from '<branchName>'...
        var newBranch = new MenuItem { Header = $"New Branch from '{branchName}'..." };
        newBranch.Click += async (_, _) =>
        {
            string defaultName = branchName.StartsWith("origin/") ? branchName.Substring(7) + "-new" : branchName + "-new";
            var dlg = new InputBoxDialog("New Branch", $"Create branch from '{branchName}':", defaultName) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
            {
                bool ok = await _gitService.CreateBranchAsync(dlg.InputValue, branchName);
                if (ok)
                {
                    await _gitService.SwitchBranchAsync(dlg.InputValue);
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                }
            }
        };
        branchMenu.Items.Add(newBranch);

        // 3. Checkout and Rebase onto '<current>'
        var checkoutAndRebase = new MenuItem { Header = $"Checkout and Rebase onto '{_currentBranch}'" };
        checkoutAndRebase.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Checkout '{branchName}' and rebase onto '{_currentBranch}'?", "Checkout and Rebase", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await _gitService.SwitchBranchAsync(branchName);
                await _gitService.RebaseAsync(_currentBranch);
                await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        branchMenu.Items.Add(checkoutAndRebase);

        branchMenu.Items.Add(new Separator());

        // 4. Compare with '<current>'
        var compareWithCurrent = new MenuItem { Header = $"Compare with '{_currentBranch}'" };
        compareWithCurrent.Click += async (_, _) =>
        {
            string rawDiff = await _gitService.GetDiffAsync($"{_currentBranch}..{branchName}");
            var diffWin = new DiffWindow($"Compare: {_currentBranch} vs {branchName}", $"{branchName}", rawDiff) { Owner = this };
            diffWin.Show();
        };
        branchMenu.Items.Add(compareWithCurrent);

        // 5. Show Diff with Working Tree
        var showDiffWithWT = new MenuItem { Header = "Show Diff with Working Tree" };
        showDiffWithWT.Click += async (_, _) =>
        {
            string rawDiff = await _gitService.GetDiffAsync(branchName);
            var diffWin = new DiffWindow($"Diff: Working Tree vs {branchName}", branchName, rawDiff) { Owner = this };
            diffWin.Show();
        };
        branchMenu.Items.Add(showDiffWithWT);

        branchMenu.Items.Add(new Separator());

        // 6. Rebase '<current>' onto '<branchName>'
        var rebaseCurrent = new MenuItem { Header = $"Rebase '{_currentBranch}' onto '{branchName}'" };
        rebaseCurrent.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Rebase '{_currentBranch}' onto '{branchName}'?", "Rebase", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                TxtStatusMessage.Text = $"Rebasing onto {branchName}...";
                bool ok = await _gitService.RebaseAsync(branchName);
                if (ok) await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        branchMenu.Items.Add(rebaseCurrent);

        // 7. Merge '<branchName>' into '<current>' (WebStorm 1:1 match!)
        var mergeItem = new MenuItem
        {
            Header = $"Merge '{branchName}' into '{_currentBranch}'",
            FontWeight = FontWeights.Bold
        };
        mergeItem.Click += async (_, _) => await PerformMergeAsync(branchName);
        branchMenu.Items.Add(mergeItem);

        branchMenu.Items.Add(new Separator());

        // 8. New Worktree...
        var newWt = new MenuItem { Header = "New Worktree..." };
        newWt.Click += (_, _) => ShowNewWorktreeDialog(branchName, isExistingBranch: true);
        branchMenu.Items.Add(newWt);

        branchMenu.Items.Add(new Separator());

        // 9. Pull into '<current>' Using Rebase
        var pullRebase = new MenuItem { Header = $"Pull into '{_currentBranch}' Using Rebase" };
        pullRebase.Click += async (_, _) =>
        {
            await ExecutePullWithAuthRetryAsync(branchName, rebase: true);
        };
        branchMenu.Items.Add(pullRebase);

        // 10. Pull into '<current>' Using Merge
        var pullMerge = new MenuItem { Header = $"Pull into '{_currentBranch}' Using Merge" };
        pullMerge.Click += async (_, _) =>
        {
            await ExecutePullWithAuthRetryAsync(branchName, rebase: false);
        };
        branchMenu.Items.Add(pullMerge);

        branchMenu.Items.Add(new Separator());

        // 11. Delete
        var deleteItem = new MenuItem { Header = "Delete", InputGestureText = "Delete" };
        deleteItem.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Are you sure you want to delete branch '{branchName}'?", "Delete Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                if (branchName.StartsWith("origin/"))
                {
                    string rBranch = branchName.Substring(7);
                    await _gitService.DeleteRemoteBranchAsync("origin", rBranch);
                }
                else
                {
                    await _gitService.DeleteBranchAsync(branchName, force: true);
                }
                await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        branchMenu.Items.Add(deleteItem);

        return branchMenu;
    }

    private void OnCommitContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (CommitsListView.SelectedItem is not GitCommitItem commit)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        string shortHash = commit.Hash.Substring(0, Math.Min(8, commit.Hash.Length));

        // 1. Copy Revision Number
        var copyHashItem = new MenuItem { Header = "Copy Revision Number", InputGestureText = "Ctrl+Alt+Shift+C" };
        copyHashItem.Click += (_, _) => SafeSetClipboard(commit.Hash);
        menu.Items.Add(copyHashItem);

        // 2. Create Patch...
        var createPatchItem = new MenuItem { Header = "Create Patch..." };
        createPatchItem.Click += async (_, _) => await CreatePatchAsync(commit.Hash);
        menu.Items.Add(createPatchItem);

        // 3. Cherry-Pick
        var cherryPickItem = new MenuItem { Header = "Cherry-Pick" };
        cherryPickItem.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Cherry-pick commit {shortHash} into '{_currentBranch}'?", "Cherry-Pick", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                TxtStatusMessage.Text = $"Cherry-picking {shortHash}...";
                bool ok = await _gitService.CherryPickAsync(commit.Hash);
                if (ok) await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        menu.Items.Add(cherryPickItem);

        menu.Items.Add(new Separator());

        // 4. Checkout Revision
        var checkoutItem = new MenuItem { Header = "Checkout Revision" };
        checkoutItem.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this,
                $"Checkout commit {shortHash} in detached HEAD state?",
                "Checkout Revision", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await _gitService.SwitchBranchAsync(commit.Hash);
                await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        menu.Items.Add(checkoutItem);

        // 5. Compare with Local
        var compareWithLocalItem = new MenuItem { Header = "Compare with Local" };
        compareWithLocalItem.Click += async (_, _) => await CompareCommitWithLocalAsync(commit.Hash);
        menu.Items.Add(compareWithLocalItem);

        menu.Items.Add(new Separator());

        // 6. Reset Current Branch to Here...
        var resetMenuItem = new MenuItem { Header = "Reset Current Branch to Here..." };

        var resetSoft = new MenuItem { Header = "Soft (keep working tree & staged changes)" };
        resetSoft.Click += async (_, _) =>
        {
            await _gitService.ResetCurrentBranchAsync(commit.Hash, "soft");
            await LoadRepositoryAsync(_gitService.RepositoryPath);
        };
        resetMenuItem.Items.Add(resetSoft);

        var resetMixed = new MenuItem { Header = "Mixed (keep working tree, unstaged)" };
        resetMixed.Click += async (_, _) =>
        {
            await _gitService.ResetCurrentBranchAsync(commit.Hash, "mixed");
            await LoadRepositoryAsync(_gitService.RepositoryPath);
        };
        resetMenuItem.Items.Add(resetMixed);

        var resetHard = new MenuItem { Header = "Hard (DISCARD all uncommitted changes!)" };
        resetHard.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this,
                $"WARNING: Hard reset will PERMANENTLY DISCARD all uncommitted changes and move HEAD to {shortHash}!\nContinue?",
                "Confirm Hard Reset", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
            if (res == MessageBoxResult.Yes)
            {
                await _gitService.ResetCurrentBranchAsync(commit.Hash, "hard");
                await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        resetMenuItem.Items.Add(resetHard);

        menu.Items.Add(resetMenuItem);

        // 7. Revert Commit
        var revertItem = new MenuItem { Header = "Revert Commit" };
        revertItem.Click += async (_, _) =>
        {
            var res = MessageBox.Show(this, $"Revert commit {shortHash}?", "Revert Commit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                TxtStatusMessage.Text = $"Reverting {shortHash}...";
                bool ok = await _gitService.RevertCommitAsync(commit.Hash);
                if (ok) await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        menu.Items.Add(revertItem);

        menu.Items.Add(new Separator());

        // 8. Branch '<branch>' > submenus (WebStorm exact match!)
        var branchNames = GetBranchNamesForCommit(commit);
        foreach (var bName in branchNames)
        {
            var branchSubmenu = CreateBranchSubmenu(bName);
            menu.Items.Add(branchSubmenu);
        }

        // 9. Direct "Merge into '<current>'" item!
        string mergeTarget = branchNames.Count > 0 ? branchNames[0] : shortHash;
        var directMergeItem = new MenuItem
        {
            Header = $"Merge '{mergeTarget}' into '{_currentBranch}'...",
            FontWeight = FontWeights.Bold
        };
        directMergeItem.Click += async (_, _) => await PerformMergeAsync(mergeTarget);
        menu.Items.Add(directMergeItem);

        menu.Items.Add(new Separator());

        // 10. New Branch...
        var newBranchItem = new MenuItem { Header = "New Branch...", InputGestureText = "Ctrl+Alt+N" };
        newBranchItem.Click += async (_, _) =>
        {
            var dlg = new InputBoxDialog("New Branch", $"Create branch from {shortHash}:", "feature/new-branch") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
            {
                bool ok = await _gitService.CreateBranchAsync(dlg.InputValue, commit.Hash);
                if (ok)
                {
                    await _gitService.SwitchBranchAsync(dlg.InputValue);
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                }
            }
        };
        menu.Items.Add(newBranchItem);

        // 11. New Tag...
        var newTagItem = new MenuItem { Header = "New Tag..." };
        newTagItem.Click += async (_, _) =>
        {
            var dlg = new InputBoxDialog("New Tag", $"Create tag on {shortHash}:", "v1.0.0") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
            {
                await _gitService.CreateTagAsync(dlg.InputValue, commit.Hash);
                await LoadRepositoryAsync(_gitService.RepositoryPath);
            }
        };
        menu.Items.Add(newTagItem);

        // 12. New Worktree...
        var newWorktreeItem = new MenuItem { Header = "New Worktree..." };
        newWorktreeItem.Click += (_, _) => ShowNewWorktreeDialog(commit.Hash, isExistingBranch: false);
        menu.Items.Add(newWorktreeItem);

        menu.Items.Add(new Separator());

        var copySubjectItem = new MenuItem { Header = "Copy Commit Subject" };
        copySubjectItem.Click += (_, _) => SafeSetClipboard(commit.Subject);
        menu.Items.Add(copySubjectItem);

        CommitsListView.ContextMenu = menu;
    }

    private void OnFilesTreePreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem != null)
        {
            treeViewItem.Focus();
            treeViewItem.IsSelected = true;
        }
    }

    private void OnFilesTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FilesTreeView.SelectedItem is not GitChangeItem ch)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();
        string fullPath = Path.Combine(_gitService.RepositoryPath, ch.RelativePath);

        if (!ch.IsDirectory)
        {
            var diffItem = new MenuItem { Header = "Show Diff", InputGestureText = "Enter" };
            diffItem.Click += (s, args) => OnShowDiffClicked(s, args);
            menu.Items.Add(diffItem);

            var openFileItem = new MenuItem { Header = "Open File" };
            openFileItem.Click += (_, _) => OpenFileWithDefaultProgram(fullPath);
            menu.Items.Add(openFileItem);

            menu.Items.Add(new Separator());
        }

        var explorerItem = new MenuItem { Header = "Show in File Explorer" };
        explorerItem.Click += (_, _) => ShowInFileExplorer(fullPath);
        menu.Items.Add(explorerItem);

        menu.Items.Add(new Separator());

        var copyRelItem = new MenuItem { Header = "Copy Relative Path" };
        copyRelItem.Click += (_, _) => SafeSetClipboard(ch.RelativePath);
        menu.Items.Add(copyRelItem);

        var copyFullItem = new MenuItem { Header = "Copy Absolute Path" };
        copyFullItem.Click += (_, _) => SafeSetClipboard(fullPath);
        menu.Items.Add(copyFullItem);

        FilesTreeView.ContextMenu = menu;
    }

    private void OnFilesTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OnShowDiffClicked(sender, e);
    }

    private async void OnShowDiffClicked(object sender, RoutedEventArgs e)
    {
        if (CommitsListView.SelectedItem is not GitCommitItem commit) return;

        string? selectedFilePath = null;
        if (FilesTreeView.SelectedItem is GitChangeItem ch && !ch.IsDirectory)
        {
            selectedFilePath = ch.RelativePath;
        }

        try
        {
            TxtStatusMessage.Text = "Loading diff...";
            string rawDiff = await _gitService.GetDiffAsync(commit.Hash, selectedFilePath);

            string title = selectedFilePath ?? commit.Subject;
            string summary = $"{commit.Hash.Substring(0, 8)} - {commit.Author}";

            var diffWin = new DiffWindow(title, summary, rawDiff) { Owner = this };
            diffWin.Show();
            TxtStatusMessage.Text = "Ready";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load diff:\n{ex.Message}", "Diff Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtCommitFilter.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            CommitsListView.ItemsSource = _allCommits;
        }
        else
        {
            var filtered = _allCommits.Where(c =>
                c.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Hash.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                c.Commit.Refs.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            CommitsListView.ItemsSource = filtered;
        }
    }

    private async void OnResetBranchFilter(object sender, RoutedEventArgs e)
    {
        TxtBranchFilterLabel.Text = "All Branches";
        await LoadCommitsForRefAsync("ALL");
    }

    #endregion

    #region WebStorm Branches Tree & Worktrees

    private void OnBranchSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtBranchSearch.Text.Trim();
        var incomingMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tree = BranchTreeBuilder.BuildTree(_currentBranch, _worktrees, _branchRefs, incomingMap);

        if (!string.IsNullOrEmpty(query))
        {
            FilterBranchNodes(tree, query);
        }

        BranchesTreeView.ItemsSource = tree;
    }

    private static bool FilterBranchNodes(IList<GitBranchItem> nodes, string query)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var n = nodes[i];
            bool hasMatchingChild = FilterBranchNodes(n.Children, query);
            bool matches = n.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (!matches && !hasMatchingChild && n.Kind != BranchItemKind.Category)
            {
                nodes.RemoveAt(i);
            }
            else if (hasMatchingChild)
            {
                n.IsExpanded = true;
            }
        }
        return nodes.Count > 0;
    }

    private async void OnBranchSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (BranchesTreeView.SelectedItem is GitBranchItem item && item.Kind == BranchItemKind.Branch)
        {
            TxtBranchFilterLabel.Text = item.Name;
            await LoadCommitsForRefAsync(item.FullRef);
        }
    }

    private async void OnBranchTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BranchesTreeView.SelectedItem is GitBranchItem item)
        {
            if (item.IsWorktree && !string.IsNullOrEmpty(item.WorktreePath))
            {
                await OpenRepositoryAsTabAsync(item.WorktreePath);
            }
            else if (item.Kind == BranchItemKind.Branch && !item.IsCurrent)
            {
                var res = MessageBox.Show(this, $"Checkout branch '{item.Name}'?", "Checkout Branch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    TxtStatusMessage.Text = $"Checking out {item.Name}...";
                    bool ok = await _gitService.SwitchBranchAsync(item.FullRef);
                    if (ok)
                    {
                        await LoadRepositoryAsync(_gitService.RepositoryPath);
                    }
                    else
                    {
                        MessageBox.Show(this, $"Failed to checkout {item.Name}", "Checkout Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }

    private void OnBranchTreePreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem != null)
        {
            treeViewItem.Focus();
            treeViewItem.IsSelected = true;
        }
    }

    private void OnBranchContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (BranchesTreeView.SelectedItem is not GitBranchItem item)
        {
            e.Handled = true;
            return;
        }

        var menu = new ContextMenu();

        if (item.Kind == BranchItemKind.Worktree || (item.IsWorktree && !string.IsNullOrEmpty(item.WorktreePath)))
        {
            var openTabItem = new MenuItem { Header = $"Open Worktree in Tab" };
            openTabItem.Click += async (_, _) =>
            {
                if (!string.IsNullOrEmpty(item.WorktreePath))
                {
                    if (Directory.Exists(item.WorktreePath))
                    {
                        await OpenRepositoryAsTabAsync(item.WorktreePath);
                    }
                    else
                    {
                        MessageBox.Show(this, $"Worktree directory does not exist:\n{item.WorktreePath}", "Cannot Open Worktree", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };
            menu.Items.Add(openTabItem);

            var openExplorerItem = new MenuItem { Header = "Show in File Explorer" };
            openExplorerItem.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(item.WorktreePath))
                {
                    ShowInFileExplorer(item.WorktreePath);
                }
            };
            menu.Items.Add(openExplorerItem);

            var openTerminalItem = new MenuItem { Header = "Open Terminal Here" };
            openTerminalItem.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(item.WorktreePath))
                {
                    OpenTerminalAt(item.WorktreePath);
                }
            };
            menu.Items.Add(openTerminalItem);

            var copyPathItem = new MenuItem { Header = "Copy Worktree Path" };
            copyPathItem.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(item.WorktreePath))
                {
                    SafeSetClipboard(item.WorktreePath);
                    TxtStatusMessage.Text = $"Copied path to clipboard: {item.WorktreePath}";
                }
            };
            menu.Items.Add(copyPathItem);

            menu.Items.Add(new Separator());

            var removeWtItem = new MenuItem { Header = $"Remove Worktree '{item.Name}'..." };
            removeWtItem.Click += async (_, _) =>
            {
                if (string.IsNullOrEmpty(item.WorktreePath)) return;
                var res = MessageBox.Show(this,
                    $"Are you sure you want to remove worktree '{item.Name}' at:\n{item.WorktreePath}?\n\nThis will remove the working directory and unregister the worktree.",
                    "Remove Worktree", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    TxtStatusMessage.Text = $"Removing worktree {item.Name}...";

                    var openTab = _openTabs.FirstOrDefault(t => string.Equals(t.Path, item.WorktreePath, StringComparison.OrdinalIgnoreCase));
                    if (openTab != null)
                    {
                        await CloseTabAsync(openTab);
                    }

                    var (ok, error) = await _gitService.RemoveWorktreeAsync(item.WorktreePath, force: true);
                    if (!ok)
                    {
                        if (!Directory.Exists(item.WorktreePath))
                        {
                            await _gitService.PruneWorktreesAsync();
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                            TxtStatusMessage.Text = "Pruned missing worktree.";
                            return;
                        }
                        MessageBox.Show(this, $"Failed to remove worktree:\n{error}", "Remove Worktree Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        TxtStatusMessage.Text = "Failed to remove worktree.";
                    }
                    else
                    {
                        await _gitService.PruneWorktreesAsync();
                        _catalog.RemoveRepository(item.WorktreePath);
                        await LoadRepositoryAsync(_gitService.RepositoryPath);
                        TxtStatusMessage.Text = $"Worktree '{item.Name}' removed.";
                    }
                }
            };
            menu.Items.Add(removeWtItem);
        }
        else if (item.Kind == BranchItemKind.Branch)
        {
            bool isRemote = item.FullRef.StartsWith("refs/remotes/") || item.FullRef.StartsWith("origin/");
            string branchName = item.Name;

            if (!isRemote)
            {
                // Local Branch
                var checkoutItem = new MenuItem
                {
                    Header = $"Checkout '{branchName}'",
                    IsEnabled = !item.IsCurrent
                };
                checkoutItem.Click += async (_, _) =>
                {
                    await _gitService.SwitchBranchAsync(item.FullRef);
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                };
                menu.Items.Add(checkoutItem);

                var newBranchItem = new MenuItem { Header = $"New Branch from '{branchName}'..." };
                newBranchItem.Click += async (_, _) =>
                {
                    var dlg = new InputBoxDialog("New Branch", $"Create branch from '{branchName}':", $"{branchName}-new") { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
                    {
                        TxtStatusMessage.Text = $"Creating branch {dlg.InputValue}...";
                        bool ok = await _gitService.CreateBranchAsync(dlg.InputValue, item.FullRef);
                        if (ok)
                        {
                            await _gitService.SwitchBranchAsync(dlg.InputValue);
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(newBranchItem);

                var newWorktreeItem = new MenuItem { Header = $"New Worktree from '{branchName}'..." };
                newWorktreeItem.Click += (_, _) => ShowNewWorktreeDialog(item.FullRef, isExistingBranch: true);
                menu.Items.Add(newWorktreeItem);

                menu.Items.Add(new Separator());

                var mergeItem = new MenuItem
                {
                    Header = $"Merge into '{_currentBranch}'...",
                    IsEnabled = !item.IsCurrent,
                    FontWeight = FontWeights.Bold
                };
                mergeItem.Click += async (_, _) => await PerformMergeAsync(branchName);
                menu.Items.Add(mergeItem);

                var rebaseItem = new MenuItem
                {
                    Header = $"Rebase '{_currentBranch}' onto '{branchName}'...",
                    IsEnabled = !item.IsCurrent
                };
                rebaseItem.Click += async (_, _) =>
                {
                    var res = MessageBox.Show(this, $"Rebase current branch '{_currentBranch}' onto '{branchName}'?", "Rebase Branch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res == MessageBoxResult.Yes)
                    {
                        TxtStatusMessage.Text = $"Rebasing onto {branchName}...";
                        bool ok = await _gitService.RebaseAsync(item.FullRef);
                        if (ok)
                        {
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(rebaseItem);

                menu.Items.Add(new Separator());

                var renameItem = new MenuItem { Header = $"Rename Branch '{branchName}'..." };
                renameItem.Click += async (_, _) =>
                {
                    var dlg = new InputBoxDialog("Rename Branch", $"Enter new name for branch '{branchName}':", branchName) { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue) && dlg.InputValue != branchName)
                    {
                        TxtStatusMessage.Text = $"Renaming {branchName} to {dlg.InputValue}...";
                        bool ok = await _gitService.RenameBranchAsync(branchName, dlg.InputValue);
                        if (ok)
                        {
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(renameItem);

                var deleteItem = new MenuItem
                {
                    Header = $"Delete Branch '{branchName}'",
                    IsEnabled = !item.IsCurrent
                };
                deleteItem.Click += async (_, _) =>
                {
                    var res = MessageBox.Show(this, $"Are you sure you want to delete branch '{branchName}'?", "Delete Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res == MessageBoxResult.Yes)
                    {
                        TxtStatusMessage.Text = $"Deleting branch {branchName}...";
                        bool ok = await _gitService.DeleteBranchAsync(branchName, force: true);
                        if (ok)
                        {
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(deleteItem);

                menu.Items.Add(new Separator());

                var copyNameItem = new MenuItem { Header = "Copy Branch Name" };
                copyNameItem.Click += (_, _) => SafeSetClipboard(branchName);
                menu.Items.Add(copyNameItem);
            }
            else
            {
                // Remote Branch
                string localCandidate = branchName.Contains('/') ? branchName.Substring(branchName.LastIndexOf('/') + 1) : branchName;
                var checkoutItem = new MenuItem { Header = $"Checkout as Local Branch '{localCandidate}'..." };
                checkoutItem.Click += async (_, _) =>
                {
                    TxtStatusMessage.Text = $"Checking out {item.FullRef}...";
                    await _gitService.SwitchBranchAsync(item.FullRef);
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                };
                menu.Items.Add(checkoutItem);

                var pullItem = new MenuItem { Header = $"Pull into '{_currentBranch}'" };
                pullItem.Click += async (_, _) =>
                {
                    await ExecutePullWithAuthRetryAsync(item.Name, rebase: false);
                };
                menu.Items.Add(pullItem);

                var mergeItem = new MenuItem
                {
                    Header = $"Merge into '{_currentBranch}'...",
                    FontWeight = FontWeights.Bold
                };
                mergeItem.Click += async (_, _) => await PerformMergeAsync(item.Name);
                menu.Items.Add(mergeItem);

                var rebaseItem = new MenuItem { Header = $"Rebase '{_currentBranch}' onto '{item.Name}'..." };
                rebaseItem.Click += async (_, _) =>
                {
                    var res = MessageBox.Show(this, $"Rebase '{_currentBranch}' onto '{item.Name}'?", "Rebase Remote Branch", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res == MessageBoxResult.Yes)
                    {
                        await _gitService.RebaseAsync(item.FullRef);
                        await LoadRepositoryAsync(_gitService.RepositoryPath);
                    }
                };
                menu.Items.Add(rebaseItem);

                var newWorktreeItem = new MenuItem { Header = $"New Worktree from '{branchName}'..." };
                newWorktreeItem.Click += (_, _) => ShowNewWorktreeDialog(item.FullRef, isExistingBranch: false);
                menu.Items.Add(newWorktreeItem);

                menu.Items.Add(new Separator());

                var deleteRemoteItem = new MenuItem { Header = $"Delete Remote Branch '{item.Name}'..." };
                deleteRemoteItem.Click += async (_, _) =>
                {
                    var res = MessageBox.Show(this,
                        $"Warning: This will delete the remote branch '{item.Name}' on origin!\nAre you sure?",
                        "Delete Remote Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res == MessageBoxResult.Yes)
                    {
                        TxtStatusMessage.Text = $"Deleting remote branch {item.Name}...";
                        string remoteName = "origin";
                        string remoteBranch = branchName.StartsWith("origin/") ? branchName.Substring(7) : branchName;
                        await _gitService.DeleteRemoteBranchAsync(remoteName, remoteBranch);
                        await _gitService.FetchAllAsync();
                        await LoadRepositoryAsync(_gitService.RepositoryPath);
                    }
                };
                menu.Items.Add(deleteRemoteItem);

                menu.Items.Add(new Separator());

                var copyNameItem = new MenuItem { Header = "Copy Remote Branch Name" };
                copyNameItem.Click += (_, _) => SafeSetClipboard(item.Name);
                menu.Items.Add(copyNameItem);
            }
        }
        else if (item.Kind == BranchItemKind.Category)
        {
            if (item.FullRef == "WORKTREES" || item.Name == "Worktrees")
            {
                var newWt = new MenuItem { Header = "New Worktree..." };
                newWt.Click += (_, _) => ShowNewWorktreeDialog();
                menu.Items.Add(newWt);

                var pruneWt = new MenuItem { Header = "Prune Stale Worktrees" };
                pruneWt.Click += async (_, _) =>
                {
                    TxtStatusMessage.Text = "Pruning stale worktrees...";
                    var (ok, err) = await _gitService.PruneWorktreesAsync();
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                    TxtStatusMessage.Text = ok ? "Worktrees pruned." : $"Prune failed: {err}";
                };
                menu.Items.Add(pruneWt);

                menu.Items.Add(new Separator());
                var refreshWtItem = new MenuItem { Header = "Refresh Worktrees" };
                refreshWtItem.Click += async (_, _) => await LoadRepositoryAsync(_gitService.RepositoryPath);
                menu.Items.Add(refreshWtItem);
            }
            else if (item.FullRef == "HEAD")
            {
                var newBranchItem = new MenuItem { Header = "New Branch from HEAD..." };
                newBranchItem.Click += async (_, _) =>
                {
                    var dlg = new InputBoxDialog("New Branch", "Create new branch from HEAD:", "feature/new-branch") { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
                    {
                        bool ok = await _gitService.CreateBranchAsync(dlg.InputValue, "HEAD");
                        if (ok)
                        {
                            await _gitService.SwitchBranchAsync(dlg.InputValue);
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(newBranchItem);

                var newWorktreeItem = new MenuItem { Header = "New Worktree from HEAD..." };
                newWorktreeItem.Click += (_, _) => ShowNewWorktreeDialog("HEAD", isExistingBranch: false);
                menu.Items.Add(newWorktreeItem);
            }
            else if (item.Name == "Remote")
            {
                var fetchItem = new MenuItem { Header = "Fetch All Remotes" };
                fetchItem.Click += async (_, _) =>
                {
                    TxtStatusMessage.Text = "Fetching all remotes...";
                    await _gitService.FetchAllAsync();
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                    TxtStatusMessage.Text = "Fetch complete.";
                };
                menu.Items.Add(fetchItem);
            }
            else if (item.Name == "Local")
            {
                var newBranchItem = new MenuItem { Header = "New Branch..." };
                newBranchItem.Click += async (_, _) =>
                {
                    var dlg = new InputBoxDialog("New Branch", "Enter new branch name:", "feature/new-branch") { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputValue))
                    {
                        bool ok = await _gitService.CreateBranchAsync(dlg.InputValue);
                        if (ok)
                        {
                            await _gitService.SwitchBranchAsync(dlg.InputValue);
                            await LoadRepositoryAsync(_gitService.RepositoryPath);
                        }
                    }
                };
                menu.Items.Add(newBranchItem);
            }
            else if (item.Name == "All Branches")
            {
                var showAllItem = new MenuItem { Header = "Show All Commits" };
                showAllItem.Click += async (_, _) =>
                {
                    TxtBranchFilterLabel.Text = "All Branches";
                    await LoadCommitsForRefAsync("ALL");
                };
                menu.Items.Add(showAllItem);

                var fetchItem = new MenuItem { Header = "Fetch All Remotes" };
                fetchItem.Click += async (_, _) =>
                {
                    TxtStatusMessage.Text = "Fetching all remotes...";
                    await _gitService.FetchAllAsync();
                    await LoadRepositoryAsync(_gitService.RepositoryPath);
                    TxtStatusMessage.Text = "Fetch complete.";
                };
                menu.Items.Add(fetchItem);
            }

            menu.Items.Add(new Separator());
            var refreshItem = new MenuItem { Header = "Refresh Branches" };
            refreshItem.Click += async (_, _) => await LoadRepositoryAsync(_gitService.RepositoryPath);
            menu.Items.Add(refreshItem);
        }
        else if (item.Kind == BranchItemKind.Folder)
        {
            var expandItem = new MenuItem { Header = "Expand All" };
            expandItem.Click += (_, _) => SetSubtreeExpanded(item, true);
            menu.Items.Add(expandItem);

            var collapseItem = new MenuItem { Header = "Collapse All" };
            collapseItem.Click += (_, _) => SetSubtreeExpanded(item, false);
            menu.Items.Add(collapseItem);
        }

        if (menu.Items.Count > 0)
        {
            BranchesTreeView.ContextMenu = menu;
        }
        else
        {
            e.Handled = true;
        }
    }

    private static void SetSubtreeExpanded(GitBranchItem item, bool isExpanded)
    {
        item.IsExpanded = isExpanded;
        foreach (var child in item.Children)
        {
            SetSubtreeExpanded(child, isExpanded);
        }
    }

    private static void ShowInFileExplorer(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else if (Directory.Exists(fullPath))
            {
                Process.Start("explorer.exe", $"\"{fullPath}\"");
            }
        }
        catch
        {
            // Ignore process launch errors
        }
    }

    private static void OpenFileWithDefaultProgram(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static void SafeSetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Ignore
        }
    }

    private async void OnFetchAllClicked(object sender, RoutedEventArgs e)
    {
        TxtStatusMessage.Text = "Fetching all remotes...";
        await _gitService.FetchAllAsync();
        await LoadRepositoryAsync(_gitService.RepositoryPath);
        TxtStatusMessage.Text = "Fetch complete.";
    }

    private async Task<bool> ExecutePullWithAuthRetryAsync(string? targetRef = null, bool rebase = false)
    {
        TxtStatusMessage.Text = string.IsNullOrEmpty(targetRef)
            ? $"Pulling '{_currentBranch}'..."
            : $"Pulling {targetRef} into '{_currentBranch}'...";

        var (success, stdout, stderr) = await _gitService.PullWithDetailsAsync(targetRef, rebase);
        if (!success)
        {
            string error = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            if (GitService.IsAuthFailure(error) || GitService.IsSslFailure(error))
            {
                TxtStatusMessage.Text = "Authentication required...";
                string remoteUrl = await _gitService.GetRemoteUrlAsync();
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
                    TxtStatusMessage.Text = "Retrying pull with credentials...";
                    var retry = await _gitService.PullWithDetailsAsync(targetRef, rebase);
                    if (retry.Success)
                    {
                        TxtStatusMessage.Text = "Pull succeeded!";
                        await LoadRepositoryAsync(_gitService.RepositoryPath);
                        await UpdatePushStatusAsync();
                        return true;
                    }
                    else
                    {
                        string retryError = string.IsNullOrEmpty(retry.Error) ? retry.Output : retry.Error;
                        MessageBox.Show(this, $"Pull failed after authentication:\n\n{retryError}", "Git Pull Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        TxtStatusMessage.Text = "Pull failed.";
                        return false;
                    }
                }
                else
                {
                    TxtStatusMessage.Text = "Pull cancelled.";
                    return false;
                }
            }
            else if (GitService.IsConflictFailure(error))
            {
                MessageBox.Show(this, $"Pull resulted in merge conflicts:\n\n{error}\n\nPlease resolve conflicts in your working tree.", "Merge Conflicts", MessageBoxButton.OK, MessageBoxImage.Warning);
                await LoadRepositoryAsync(_gitService.RepositoryPath);
                await UpdatePushStatusAsync();
                OnSubtabWorkingTreeClicked(this, new RoutedEventArgs());
                return false;
            }
            else
            {
                MessageBox.Show(this, $"Git pull failed:\n\n{error}", "Git Pull Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatusMessage.Text = "Pull failed.";
                return false;
            }
        }

        TxtStatusMessage.Text = "Pull complete.";
        await LoadRepositoryAsync(_gitService.RepositoryPath);
        await UpdatePushStatusAsync();
        return true;
    }

    private async void OnPullClicked(object sender, RoutedEventArgs e)
    {
        await ExecutePullWithAuthRetryAsync(null, rebase: false);
    }

    private async void OnPushClicked(object sender, RoutedEventArgs e)
    {
        var pushDlg = new PushDialog(_gitService, _currentBranch)
        {
            Owner = this
        };
        if (pushDlg.ShowDialog() == true)
        {
            TxtStatusMessage.Text = pushDlg.PushedCommitCount > 0
                ? $"Successfully pushed {pushDlg.PushedCommitCount} commit(s) to remote."
                : "Push complete.";
            await LoadRepositoryAsync(_gitService.RepositoryPath);
            await UpdatePushStatusAsync();
        }
    }

    private async void OnToolbarCommitClicked(object sender, RoutedEventArgs e)
    {
        var commitDlg = new CommitDialog(_gitService, _currentBranch)
        {
            Owner = this
        };
        if (commitDlg.ShowDialog() == true)
        {
            TxtStatusMessage.Text = "Commit complete.";
            await LoadRepositoryAsync(_gitService.RepositoryPath);
            await UpdatePushStatusAsync();

            if (commitDlg.ShouldPush)
            {
                OnPushClicked(sender, e);
            }
        }
    }

    public async Task UpdatePushStatusAsync()
    {
        try
        {
            int count = await _gitService.GetOutgoingCommitCountAsync(_currentBranch);
            if (count > 0)
            {
                BtnToolbarPush.Content = $"⬆ Push ({count})";
                BtnToolbarPush.ToolTip = $"Push {count} commit{(count > 1 ? "s" : "")} to remote (Ctrl+Shift+K)";
                BtnToolbarPush.Foreground = (Brush)FindResource("BrushStar");
            }
            else
            {
                BtnToolbarPush.Content = "⬆ Push";
                BtnToolbarPush.ToolTip = "Push Current Branch (Ctrl+Shift+K)";
                BtnToolbarPush.Foreground = (Brush)FindResource("BrushTextMuted");
            }
        }
        catch
        {
            BtnToolbarPush.Content = "⬆ Push";
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OnPushClicked(this, new RoutedEventArgs());
            }
            else
            {
                OnToolbarCommitClicked(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            OnPullClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            OnRefreshClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnNewWorktreeClicked(object sender, RoutedEventArgs e)
    {
        ShowNewWorktreeDialog();
    }

    private async void ShowNewWorktreeDialog(string? baseRef = null, bool isExistingBranch = false)
    {
        string? branchCandidate = null;
        if (isExistingBranch && !string.IsNullOrEmpty(baseRef))
        {
            branchCandidate = baseRef.StartsWith("refs/heads/") ? baseRef.Substring(11) : baseRef;
        }

        var cleanBranches = _branchRefs
            .Select(r => r.StartsWith("refs/heads/") ? r.Substring(11) : (r.StartsWith("refs/remotes/") ? r.Substring(13) : r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b)
            .ToList();

        var dlg = new NewWorktreeDialog(
            _gitService.RepositoryPath,
            defaultBranch: branchCandidate,
            defaultBase: baseRef,
            isExistingBranch: isExistingBranch,
            availableBranches: cleanBranches)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.LocationPath))
        {
            TxtStatusMessage.Text = $"Creating worktree at '{dlg.LocationPath}'...";
            try
            {
                var (success, error) = await _gitService.CreateWorktreeAsync(
                    dlg.LocationPath,
                    dlg.BranchName,
                    dlg.BaseRef,
                    createNewBranch: dlg.CreateNewBranch,
                    detach: dlg.Detach);

                if (!success)
                {
                    MessageBox.Show(this,
                        $"Failed to create worktree:\n{error}",
                        "Worktree Creation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    TxtStatusMessage.Text = "Failed to create worktree.";
                    return;
                }

                TxtStatusMessage.Text = $"Worktree created at '{dlg.LocationPath}'.";
                await LoadRepositoryAsync(_gitService.RepositoryPath);

                if (dlg.OpenTabImmediately)
                {
                    await OpenRepositoryAsTabAsync(dlg.LocationPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Error creating worktree:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                TxtStatusMessage.Text = "Error creating worktree.";
            }
        }
    }

    private static void OpenTerminalAt(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return;
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = folderPath,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = folderPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await LoadRepositoryAsync(_gitService.RepositoryPath);
    }

    #endregion

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
