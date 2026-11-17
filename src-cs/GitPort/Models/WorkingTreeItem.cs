using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GitPort.Models;

public class WorkingTreeItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isStaged;

    public string Path { get; set; } = "";
    public string Status { get; set; } = ""; // M, A, D, ??, etc.

    public string FileName => System.IO.Path.GetFileName(Path);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
    public string DisplayDirectory => string.IsNullOrEmpty(DirectoryPath) ? "" : $"{DirectoryPath}/";

    public bool IsStaged
    {
        get => _isStaged;
        set
        {
            if (_isStaged != value)
            {
                _isStaged = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StageStatusText));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayStatus => Status switch
    {
        "??" => "U",
        _ => Status
    };

    public string StageStatusText => IsStaged ? "STAGED" : "UNSTAGED";

    public Brush StatusBrush => Status switch
    {
        "M" => (Brush)new BrushConverter().ConvertFromString("#E5C07B")!, // Orange/Yellow
        "A" or "??" => (Brush)new BrushConverter().ConvertFromString("#98C379")!, // Green
        "D" => (Brush)new BrushConverter().ConvertFromString("#E06C75")!, // Red
        _ => (Brush)new BrushConverter().ConvertFromString("#61AFEF")!  // Blue
    };

    public Brush StageStatusBrush => IsStaged
        ? (Brush)new BrushConverter().ConvertFromString("#98C379")!
        : (Brush)new BrushConverter().ConvertFromString("#868A91")!;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
