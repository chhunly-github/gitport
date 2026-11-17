using System.Windows.Media;

namespace GitPort.Models;

public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted,
    Empty,
    HunkHeader
}

public class SideBySideDiffRow
{
    private static readonly Brush AddBg = new SolidColorBrush(Color.FromArgb(0x35, 0x57, 0x96, 0x5C));
    private static readonly Brush AddText = new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79));
    private static readonly Brush DelBg = new SolidColorBrush(Color.FromArgb(0x35, 0xE0, 0x6C, 0x75));
    private static readonly Brush DelText = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
    private static readonly Brush EmptyBg = new SolidColorBrush(Color.FromArgb(0x20, 0x15, 0x24, 0x38));
    private static readonly Brush HeaderBg = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xB8, 0xE5));
    private static readonly Brush HeaderText = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0xE5));
    private static readonly Brush NormalText = new SolidColorBrush(Color.FromRgb(0xE6, 0xEE, 0xF8));

    // Left pane (Original / Base)
    public int? LeftLineNumber { get; set; }
    public string LeftText { get; set; } = "";
    public DiffLineType LeftType { get; set; } = DiffLineType.Unchanged;

    // Right pane (Modified / Working Tree)
    public int? RightLineNumber { get; set; }
    public string RightText { get; set; } = "";
    public DiffLineType RightType { get; set; } = DiffLineType.Unchanged;

    public bool IsHunkHeader { get; set; }
    public string HunkHeaderText { get; set; } = "";

    public System.Windows.Visibility HunkVisibility => IsHunkHeader ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility CodeVisibility => IsHunkHeader ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public string LeftLineNumberDisplay => LeftLineNumber.HasValue ? LeftLineNumber.Value.ToString() : "";
    public string RightLineNumberDisplay => RightLineNumber.HasValue ? RightLineNumber.Value.ToString() : "";

    public string LeftSign => LeftType switch
    {
        DiffLineType.Deleted => "-",
        _ => " "
    };

    public string RightSign => RightType switch
    {
        DiffLineType.Added => "+",
        _ => " "
    };

    public Brush LeftBackground => LeftType switch
    {
        DiffLineType.Deleted => DelBg,
        DiffLineType.Empty => EmptyBg,
        DiffLineType.HunkHeader => HeaderBg,
        _ => Brushes.Transparent
    };

    public Brush LeftForeground => LeftType switch
    {
        DiffLineType.Deleted => DelText,
        DiffLineType.HunkHeader => HeaderText,
        _ => NormalText
    };

    public Brush RightBackground => RightType switch
    {
        DiffLineType.Added => AddBg,
        DiffLineType.Empty => EmptyBg,
        DiffLineType.HunkHeader => HeaderBg,
        _ => Brushes.Transparent
    };

    public Brush RightForeground => RightType switch
    {
        DiffLineType.Added => AddText,
        DiffLineType.HunkHeader => HeaderText,
        _ => NormalText
    };
}

public class DiffDocument
{
    public string FilePath { get; set; } = "";
    public int AdditionsCount { get; set; }
    public int DeletionsCount { get; set; }
    public string StatsSummary => $"+{AdditionsCount}  -{DeletionsCount}";
    public List<SideBySideDiffRow> SideBySideRows { get; set; } = new();
    public List<DiffLineItem> UnifiedLines { get; set; } = new();
}
