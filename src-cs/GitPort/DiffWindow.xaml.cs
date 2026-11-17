using System.Windows;
using GitPort.Models;
using GitPort.Services;

namespace GitPort;

public partial class DiffWindow : Window
{
    private readonly DiffDocument _doc;

    public DiffWindow(string filePath, string commitSummary, string rawDiff)
    {
        InitializeComponent();

        TxtFilePath.Text = filePath;
        TxtCommitSummary.Text = commitSummary;

        _doc = SideBySideDiffBuilder.Build(rawDiff, filePath);

        TxtDiffStatsAdd.Text = $"+{_doc.AdditionsCount}";
        TxtDiffStatsDel.Text = $"-{_doc.DeletionsCount}";

        SideBySideListView.ItemsSource = _doc.SideBySideRows;
        UnifiedItemsControl.ItemsSource = _doc.UnifiedLines;
    }

    private void OnViewModeChanged(object sender, RoutedEventArgs e)
    {
        if (RbSideBySide.IsChecked == true)
        {
            SideBySideListView.Visibility = Visibility.Visible;
            UnifiedScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            SideBySideListView.Visibility = Visibility.Collapsed;
            UnifiedScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
