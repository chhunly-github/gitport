using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace GitPort.Models;

public class GitChangeItem
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string Status { get; set; } = ""; // M, A, D, R, etc.
    public int FileCount { get; set; }
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<GitChangeItem> Children { get; set; } = new();

    public Brush StatusBrush => Status switch
    {
        "M" => (Brush)new BrushConverter().ConvertFromString("#61AFEF")!, // Blue
        "A" => (Brush)new BrushConverter().ConvertFromString("#98C379")!, // Green
        "D" => (Brush)new BrushConverter().ConvertFromString("#E06C75")!, // Red
        "R" => (Brush)new BrushConverter().ConvertFromString("#56B6C2")!, // Cyan
        _ => (Brush)new BrushConverter().ConvertFromString("#E5C07B")!  // Yellow
    };

    public string DisplayText => IsDirectory
        ? $"{Name}  {FileCount} {(FileCount == 1 ? "file" : "files")}"
        : Name;

    public string DisplayFileCount => IsDirectory
        ? $"{FileCount} {(FileCount == 1 ? "file" : "files")}"
        : "";
}
