using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using GitPort.Models;
using GitPort.Services;

namespace GitPort.Services;

public class GraphService
{
    public static readonly Color Magenta = Color.FromRgb(0xD9, 0x57, 0x96); // #D95796 Pink/Magenta
    public static readonly Color Gold = Color.FromRgb(0xE5, 0xC0, 0x7B);    // #E5C07B Gold/Yellow
    public static readonly Color Cyan = Color.FromRgb(0x00, 0xB8, 0xE5);    // #00B8E5 Brand Cyan/Blue (Concept color)
    public static readonly Color Purple = Color.FromRgb(0xC6, 0x78, 0xDD);  // #C678DD Purple
    public static readonly Color Green = Color.FromRgb(0x98, 0xC3, 0x79);   // #98C379 Green
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x94, 0x3D);  // #FF943D Brand Orange (Concept color)
    public static readonly Color Teal = Color.FromRgb(0x38, 0xBD, 0xF8);    // #38BDF8 Sky/Teal
    public static readonly Color Crimson = Color.FromRgb(0xE0, 0x6C, 0x75); // #E06C75 Crimson

    public static readonly Color[] Palette = new[]
    {
        Magenta, Gold, Cyan, Purple, Green, Orange, Teal, Crimson
    };

    public static Color GetLaneColor(int lane) => Palette[Math.Abs(lane) % Palette.Length];

    public static List<GitCommitItem> BuildGraph(List<GitCommit> commits, string currentBranch, string headCommit)
    {
        var items = new List<GitCommitItem>();
        if (commits.Count == 0) return items;

        var activeLanes = new List<string?>(); // expected next hash in lane
        var laneColors = new List<Color>();    // color of each active lane

        for (int i = 0; i < commits.Count; i++)
        {
            var c = commits[i];
            var item = new GitCommitItem(c);

            // Determine if HEAD
            item.IsHead = (!string.IsNullOrEmpty(headCommit) && c.Hash.StartsWith(headCommit)) ||
                          (c.Refs.Contains("HEAD ->") || c.Refs.Contains("HEAD,") || c.Refs == "HEAD");

            // Parse refs & chip badges
            ParseRefs(item, currentBranch);

            // 1. Check if this commit was expected from a row above
            int lane = activeLanes.IndexOf(c.Hash);
            bool hasIncoming = (lane >= 0);

            Color nodeColor;
            if (hasIncoming)
            {
                nodeColor = laneColors[lane];
            }
            else
            {
                // Find first free slot or create new lane
                lane = activeLanes.IndexOf(null);
                if (lane < 0)
                {
                    lane = activeLanes.Count;
                    nodeColor = PickColorForCommit(item, lane);
                    activeLanes.Add(null);
                    laneColors.Add(nodeColor);
                }
                else
                {
                    nodeColor = PickColorForCommit(item, lane);
                    laneColors[lane] = nodeColor;
                }
            }

            // If commit has specific branch tags (e.g. master/main), update lane color to match branch
            if (item.RefChips.Any(r => r.Name.Contains("master") || r.Name.Contains("main")))
            {
                nodeColor = Gold;
                laneColors[lane] = nodeColor;
            }
            else if (item.IsHead || item.RefChips.Any(r => r.Name == currentBranch || r.Name == "ly"))
            {
                nodeColor = Magenta;
                laneColors[lane] = nodeColor;
            }

            item.Lane = lane;
            item.LaneColor = nodeColor;
            item.LaneBrush = new SolidColorBrush(nodeColor);
            item.LaneBrush.Freeze();
            item.HasIncomingFromTop = hasIncoming;

            // 2. Record passing lanes (active lanes not ending or starting at this row)
            for (int l = 0; l < activeLanes.Count; l++)
            {
                if (l != lane && activeLanes[l] != null)
                {
                    var passBrush = new SolidColorBrush(laneColors[l]);
                    passBrush.Freeze();
                    item.PassingLanesList.Add(new PassingLaneInfo
                    {
                        Lane = l,
                        Color = laneColors[l],
                        Brush = passBrush
                    });
                }
            }

            // 3. Update active lanes for parents
            if (c.Parents.Length == 0)
            {
                // Initial/root commit: ends this lane
                activeLanes[lane] = null;
            }
            else
            {
                // Primary parent continues down in this lane
                string p1 = c.Parents[0];
                activeLanes[lane] = p1;

                var p1Brush = new SolidColorBrush(nodeColor);
                p1Brush.Freeze();
                item.OutgoingEdges.Add(new GraphEdge
                {
                    FromLane = lane,
                    ToLane = lane,
                    Color = nodeColor,
                    Brush = p1Brush,
                    IsMerge = false
                });

                // Secondary parents (merge commit)
                for (int p = 1; p < c.Parents.Length; p++)
                {
                    string pOther = c.Parents[p];
                    int otherLane = activeLanes.IndexOf(pOther);
                    Color mergeColor;

                    if (otherLane < 0)
                    {
                        // Allocate lane for merge parent
                        otherLane = activeLanes.IndexOf(null);
                        mergeColor = Palette[(lane + p) % Palette.Length];

                        if (otherLane < 0)
                        {
                            otherLane = activeLanes.Count;
                            activeLanes.Add(pOther);
                            laneColors.Add(mergeColor);
                        }
                        else
                        {
                            activeLanes[otherLane] = pOther;
                            laneColors[otherLane] = mergeColor;
                        }
                    }
                    else
                    {
                        mergeColor = laneColors[otherLane];
                    }

                    var mergeBrush = new SolidColorBrush(mergeColor);
                    mergeBrush.Freeze();

                    item.OutgoingEdges.Add(new GraphEdge
                    {
                        FromLane = lane,
                        ToLane = otherLane,
                        Color = mergeColor,
                        Brush = mergeBrush,
                        IsMerge = true
                    });
                }
            }

            // 4. Check for convergence: multiple active lanes pointing to the same parent
            for (int l1 = 0; l1 < activeLanes.Count; l1++)
            {
                if (activeLanes[l1] == null) continue;
                for (int l2 = l1 + 1; l2 < activeLanes.Count; l2++)
                {
                    if (activeLanes[l2] != null && activeLanes[l1] == activeLanes[l2])
                    {
                        // Lane l2 converges into l1
                        var forkBrush = new SolidColorBrush(laneColors[l2]);
                        forkBrush.Freeze();
                        item.OutgoingEdges.Add(new GraphEdge
                        {
                            FromLane = l2,
                            ToLane = l1,
                            Color = laneColors[l2],
                            Brush = forkBrush,
                            IsFork = true
                        });
                        activeLanes[l2] = null;
                    }
                }
            }

            // Trim nulls from end
            while (activeLanes.Count > 0 && activeLanes[^1] == null)
            {
                activeLanes.RemoveAt(activeLanes.Count - 1);
                laneColors.RemoveAt(laneColors.Count - 1);
            }

            items.Add(item);
        }

        return items;
    }

    private static Color PickColorForCommit(GitCommitItem item, int lane)
    {
        if (item.IsHead || item.RefChips.Any(r => r.IsHead)) return Magenta;
        if (item.RefChips.Any(r => r.Name.Contains("master") || r.Name.Contains("main"))) return Gold;
        if (item.RefChips.Any(r => r.Name.Contains("Dr.Dev"))) return Cyan;
        if (item.RefChips.Any(r => r.Name.Contains("bio"))) return Purple;
        return Palette[lane % Palette.Length];
    }

    private static void ParseRefs(GitCommitItem item, string currentBranch)
    {
        string raw = item.Commit.Refs.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        raw = raw.Trim('(', ')', ' ');
        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var localBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remoteBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();

        foreach (var p in parts)
        {
            string token = p.Trim();
            if (string.IsNullOrEmpty(token)) continue;
            if (token.EndsWith("/HEAD") || token == "origin/HEAD") continue;

            if (token.StartsWith("HEAD ->"))
            {
                string b = token.Substring(7).Trim();
                localBranches.Add(b);
                item.IsHead = true;
            }
            else if (token == "HEAD")
            {
                item.IsHead = true;
            }
            else if (token.StartsWith("tag:"))
            {
                tags.Add(token.Substring(4).Trim());
            }
            else if (token.StartsWith("origin/"))
            {
                remoteBranches.Add(token.Substring(7).Trim());
            }
            else
            {
                localBranches.Add(token);
            }
        }

        var lavenderBrush = new SolidColorBrush(Color.FromRgb(0xA3, 0x71, 0xF7)); // WebStorm purple/lavender
        lavenderBrush.Freeze();
        var goldBrush = new SolidColorBrush(Gold);
        goldBrush.Freeze();

        var summaries = new List<string>();

        // 1. Check for overlapping local and remote branches (e.g. "ly" and "origin/ly" -> "origin & ly")
        var shared = localBranches.Intersect(remoteBranches, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var s in shared)
        {
            item.RefChips.Add(new RefChip
            {
                Name = $"origin & {s}",
                IsLocal = true,
                IsRemote = true,
                Foreground = lavenderBrush
            });
            summaries.Add($"origin & {s}");
            localBranches.Remove(s);
            remoteBranches.Remove(s);
        }

        // 2. Remaining remote branches
        foreach (var r in remoteBranches)
        {
            item.RefChips.Add(new RefChip
            {
                Name = $"origin/{r}",
                IsRemote = true,
                Foreground = lavenderBrush
            });
            summaries.Add($"origin/{r}");
        }

        // 3. Remaining local branches
        foreach (var l in localBranches)
        {
            item.RefChips.Add(new RefChip
            {
                Name = l,
                IsLocal = true,
                Foreground = (l == "master" || l == "main") ? goldBrush : lavenderBrush
            });
            summaries.Add(l);
        }

        // 4. Tags
        foreach (var t in tags)
        {
            item.RefChips.Add(new RefChip
            {
                Name = t,
                IsTag = true,
                Foreground = goldBrush
            });
            summaries.Add($"tag: {t}");
        }

        item.RefSummary = string.Join(", ", summaries);
        item.BranchContainmentText = summaries.Count > 0
            ? $"In {summaries.Count} {(summaries.Count == 1 ? "branch" : "branches")}: {string.Join(", ", summaries)}"
            : "";
    }
}
