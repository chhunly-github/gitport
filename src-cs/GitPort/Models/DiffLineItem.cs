using System.Windows.Media;

namespace GitPort.Models;

public class DiffLineItem
{
    public string Text { get; set; } = "";
    public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush TextBrush { get; set; } = Brushes.White;
}
