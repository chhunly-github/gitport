using System;
using System.Windows;
using System.Windows.Media;
using GitPort.Models;

namespace GitPort.Controls;

public class CommitGraphControl : FrameworkElement
{
    public static readonly DependencyProperty CommitItemProperty =
        DependencyProperty.Register(
            nameof(CommitItem),
            typeof(GitCommitItem),
            typeof(CommitGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public GitCommitItem? CommitItem
    {
        get => (GitCommitItem?)GetValue(CommitItemProperty);
        set => SetValue(CommitItemProperty, value);
    }

    public static readonly DependencyProperty LaneWidthProperty =
        DependencyProperty.Register(
            nameof(LaneWidth),
            typeof(double),
            typeof(CommitGraphControl),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double LaneWidth
    {
        get => (double)GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    static CommitGraphControl()
    {
        ClipToBoundsProperty.OverrideMetadata(typeof(CommitGraphControl), new FrameworkPropertyMetadata(true));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (CommitItem == null) return new Size(24, 24);
        int maxLane = CommitItem.Lane;
        foreach (var p in CommitItem.PassingLanesList) if (p.Lane > maxLane) maxLane = p.Lane;
        foreach (var e in CommitItem.OutgoingEdges)
        {
            if (e.FromLane > maxLane) maxLane = e.FromLane;
            if (e.ToLane > maxLane) maxLane = e.ToLane;
        }

        double width = Math.Max(28.0, 10.0 + (maxLane + 1) * LaneWidth + 6.0);
        return new Size(width, 24.0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (CommitItem == null) return;

        double laneW = LaneWidth;
        double leftPad = 10.0;
        double h = ActualHeight > 0 ? ActualHeight : 24.0;
        double cy = h / 2.0;

        // 1. Passing lanes (vertical lines from top to bottom)
        foreach (var pass in CommitItem.PassingLanesList)
        {
            double px = leftPad + pass.Lane * laneW;
            var pen = new Pen(pass.Brush, 1.5);
            pen.Freeze();
            dc.DrawLine(pen, new Point(px, 0), new Point(px, h));
        }

        double nodeX = leftPad + CommitItem.Lane * laneW;

        // 2. Incoming edge from top to node (ONLY if there is a commit above in this lane!)
        if (CommitItem.HasIncomingFromTop)
        {
            var nodePen = new Pen(CommitItem.LaneBrush, 1.5);
            nodePen.Freeze();
            dc.DrawLine(nodePen, new Point(nodeX, 0), new Point(nodeX, cy));
        }

        // 3. Outgoing edges to parents below
        foreach (var edge in CommitItem.OutgoingEdges)
        {
            double fromX = leftPad + edge.FromLane * laneW;
            double toX = leftPad + edge.ToLane * laneW;
            var edgePen = new Pen(edge.Brush, 1.5);
            edgePen.Freeze();

            if (Math.Abs(fromX - toX) < 0.5)
            {
                // Straight line down to bottom of cell
                dc.DrawLine(edgePen, new Point(fromX, cy), new Point(toX, h));
            }
            else
            {
                // Smooth cubic bezier curve
                var geom = new PathGeometry();
                var fig = new PathFigure
                {
                    StartPoint = new Point(fromX, cy),
                    IsFilled = false
                };
                fig.Segments.Add(new BezierSegment(
                    new Point(fromX, cy + (h - cy) * 0.55),
                    new Point(toX, cy + (h - cy) * 0.45),
                    new Point(toX, h),
                    true
                ));
                geom.Figures.Add(fig);
                geom.Freeze();
                dc.DrawGeometry(null, edgePen, geom);
            }
        }

        // 4. Commit Node Dot
        if (CommitItem.IsHead)
        {
            // WebStorm Concentric Bullseye: outer ring + inner solid dot
            var outerPen = new Pen(CommitItem.LaneBrush, 1.5);
            outerPen.Freeze();
            dc.DrawEllipse(null, outerPen, new Point(nodeX, cy), 6.0, 6.0);
            dc.DrawEllipse(CommitItem.LaneBrush, null, new Point(nodeX, cy), 2.5, 2.5);
        }
        else
        {
            // WebStorm solid node circle
            dc.DrawEllipse(CommitItem.LaneBrush, null, new Point(nodeX, cy), 3.5, 3.5);
        }
    }
}
