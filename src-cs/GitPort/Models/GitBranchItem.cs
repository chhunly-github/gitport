using System.Collections.ObjectModel;

namespace GitPort.Models;

public enum BranchItemKind
{
    Category, // "HEAD", "Local", "Remote"
    Folder,   // "agents", "origin", etc.
    Branch,   // branch leaf
    Worktree  // branch linked to worktree
}

public class GitBranchItem
{
    public string Name { get; set; } = "";
    public string FullRef { get; set; } = "";
    public BranchItemKind Kind { get; set; } = BranchItemKind.Branch;
    public bool IsCurrent { get; set; }
    public bool IsWorktree { get; set; }
    public string? WorktreePath { get; set; }
    public bool IsStar { get; set; }
    public string BadgeText { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<GitBranchItem> Children { get; set; } = new();

    public override string ToString() => Name;
}
