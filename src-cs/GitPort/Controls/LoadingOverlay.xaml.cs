using System.Windows;
using System.Windows.Controls;
using System;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GitPort.Controls;

public partial class LoadingOverlay : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(LoadingOverlay), new PropertyMetadata("Working…"));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public LoadingOverlay()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => ((RotateTransform)Spinner.RenderTransform).BeginAnimation(
            RotateTransform.AngleProperty, IsVisible ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            { RepeatBehavior = RepeatBehavior.Forever } : null);
    }
}
