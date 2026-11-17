using System;
using System.Collections.Generic;
using System.Windows.Media;
using GitPort.Services;

namespace GitPort.Models;

public class GraphEdge
{
    public int FromLane { get; set; }
    public int ToLane { get; set; }
    public Color Color { get; set; }
    public Brush Brush { get; set; } = Brushes.LightGray;
    public bool IsMerge { get; set; }
    public bool IsFork { get; set; }
}

public class PassingLaneInfo
{
    public int Lane { get; set; }
    public Color Color { get; set; }
    public Brush Brush { get; set; } = Brushes.LightGray;
}

public class RefChip
{
    public string Name { get; set; } = "";
    public bool IsHead { get; set; }
    public bool IsLocal { get; set; }
    public bool IsRemote { get; set; }
    public bool IsTag { get; set; }
    public Brush Background { get; set; } = Brushes.Transparent;
    public Brush Foreground { get; set; } = Brushes.LightGray;
    public Brush Border { get; set; } = Brushes.Gray;
}

public class GitCommitItem
{
    public GitCommit Commit { get; }
    public int Lane { get; set; }
    public Color LaneColor { get; set; }
    public Brush LaneBrush { get; set; } = Brushes.LightGray;
    public bool IsHead { get; set; }
    public bool IsWorkingTree => Commit.IsWorkingTree;

    public bool HasIncomingFromTop { get; set; }
    public List<PassingLaneInfo> PassingLanesList { get; } = new();
    public List<GraphEdge> OutgoingEdges { get; } = new();

    public List<RefChip> RefChips { get; } = new();
    public string RefSummary { get; set; } = "";
    public string BranchContainmentText { get; set; } = "";

    public string ShortHash => Commit.ShortHash;
    public string Hash => Commit.Hash;
    public string Subject => Commit.Subject;
    public string Author => Commit.Author;
    public string Date => Commit.Date;

    public GitCommitItem(GitCommit commit)
    {
        Commit = commit;
    }
}
