using System.Collections.ObjectModel;

namespace GitPort.Models;

public class RepoItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupId { get; set; } = "ungrouped";
    public bool IsActive { get; set; }
    public string CurrentBranch { get; set; } = "";
    public int ChangeCount { get; set; }
    public string Tooltip => Path;
}

public class RepoGroupItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<RepoItem> Repositories { get; set; } = new();

    public int Count => Repositories.Count;
    public string DisplayHeader => $"{Name} ({Count})";
}
