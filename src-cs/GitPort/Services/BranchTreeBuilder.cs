using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GitPort.Models;

namespace GitPort.Services;

public class BranchTreeBuilder
{
    public static ObservableCollection<GitBranchItem> BuildTree(
        string currentBranch,
        List<GitWorktree> worktrees,
        List<string> refs,
        Dictionary<string, int>? incomingMap = null)
    {
        var rootList = new ObservableCollection<GitBranchItem>();

        // 1. All Branches (to view full repository history)
        var allBranchesItem = new GitBranchItem
        {
            Name = "All Branches",
            FullRef = "ALL",
            Kind = BranchItemKind.Category,
            IsExpanded = true
        };
        rootList.Add(allBranchesItem);

        // 2. HEAD
        var headItem = new GitBranchItem
        {
            Name = "HEAD (Current Branch)",
            FullRef = "HEAD",
            Kind = BranchItemKind.Category,
            IsCurrent = true,
            IsExpanded = true
        };
        rootList.Add(headItem);

        // Map worktrees by branch name and leaf name
        var worktreeBranchMap = new Dictionary<string, GitWorktree>(StringComparer.OrdinalIgnoreCase);
        foreach (var wt in worktrees)
        {
            string b = wt.DisplayBranch;
            if (!string.IsNullOrEmpty(b))
            {
                worktreeBranchMap[b] = wt;
                if (!b.StartsWith("refs/heads/"))
                {
                    worktreeBranchMap[$"refs/heads/{b}"] = wt;
                }
                string leaf = b.Contains('/') ? b.Substring(b.LastIndexOf('/') + 1) : b;
                worktreeBranchMap[leaf] = wt;
            }
        }

        // 3. Local
        var localCategory = new GitBranchItem
        {
            Name = "Local",
            Kind = BranchItemKind.Category,
            IsExpanded = true
        };

        // 4. Remote
        var remoteCategory = new GitBranchItem
        {
            Name = "Remote",
            Kind = BranchItemKind.Category,
            IsExpanded = true
        };

        var localRefs = new List<string>();
        var remoteRefs = new List<string>();

        foreach (var r in refs)
        {
            string clean = r.Trim();
            if (clean.EndsWith("/HEAD") || clean == "origin" || clean == "HEAD") continue;

            if (clean.StartsWith("refs/heads/"))
            {
                localRefs.Add(clean.Substring(11));
            }
            else if (clean.StartsWith("refs/remotes/"))
            {
                remoteRefs.Add(clean.Substring(13));
            }
            else if (clean.StartsWith("origin/"))
            {
                remoteRefs.Add(clean);
            }
            else
            {
                localRefs.Add(clean);
            }
        }

        // Build Local hierarchy
        BuildBranchHierarchy(localRefs, localCategory, currentBranch, worktreeBranchMap, isRemote: false, incomingMap);

        // Build Remote hierarchy
        BuildBranchHierarchy(remoteRefs, remoteCategory, currentBranch, worktreeBranchMap, isRemote: true, incomingMap);

        rootList.Add(localCategory);
        rootList.Add(remoteCategory);

        return rootList;
    }

    private static void BuildBranchHierarchy(
        List<string> branchNames,
        GitBranchItem parentNode,
        string currentBranch,
        Dictionary<string, GitWorktree> worktreeMap,
        bool isRemote,
        Dictionary<string, int>? incomingMap)
    {
        var folderMap = new Dictionary<string, GitBranchItem>(StringComparer.OrdinalIgnoreCase);

        // Sort: active branch first, then starred master/main, then others
        var sorted = branchNames.Distinct().OrderBy(b =>
        {
            if (b.Equals(currentBranch, StringComparison.OrdinalIgnoreCase)) return 0;
            if (b.Equals("master", StringComparison.OrdinalIgnoreCase) || b.Equals("main", StringComparison.OrdinalIgnoreCase)) return 1;
            if (!b.Contains('/')) return 2;
            return 3;
        }).ThenBy(b => b).ToList();

        foreach (var b in sorted)
        {
            var parts = b.Split('/', StringSplitOptions.RemoveEmptyEntries);
            GitBranchItem currentParent = parentNode;

            // Only create folders if there are slashes
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folderKey = string.Join('/', parts.Take(i + 1));
                if (!folderMap.TryGetValue(folderKey, out var folderItem))
                {
                    folderItem = new GitBranchItem
                    {
                        Name = parts[i],
                        FullRef = folderKey,
                        Kind = BranchItemKind.Folder,
                        IsExpanded = true
                    };
                    folderMap[folderKey] = folderItem;
                    currentParent.Children.Add(folderItem);
                }
                currentParent = folderItem;
            }

            string leafName = parts[^1];
            bool isCurrent = !isRemote && string.Equals(b, currentBranch, StringComparison.OrdinalIgnoreCase);

            // Only secondary worktrees (separate directories) get the worktree icon
            bool isSecondaryWorktree = false;
            string? wtPath = null;

            if (worktreeMap.TryGetValue(b, out var wt))
            {
                if (!wt.IsMain)
                {
                    isSecondaryWorktree = true;
                    wtPath = wt.Path;
                }
            }
            else if (worktreeMap.TryGetValue(leafName, out var wtLeaf))
            {
                if (!wtLeaf.IsMain)
                {
                    isSecondaryWorktree = true;
                    wtPath = wtLeaf.Path;
                }
            }
            else if (b.StartsWith("worktree-"))
            {
                isSecondaryWorktree = true;
            }

            bool isStar = leafName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
                          leafName.Equals("main", StringComparison.OrdinalIgnoreCase);

            string badge = "";
            if (incomingMap != null && incomingMap.TryGetValue(b, out int count) && count > 0)
            {
                badge = $"⬇ {count}";
            }

            var leaf = new GitBranchItem
            {
                Name = leafName,
                FullRef = isRemote ? (b.StartsWith("origin/") ? b : $"origin/{b}") : b,
                Kind = isSecondaryWorktree ? BranchItemKind.Worktree : BranchItemKind.Branch,
                IsCurrent = isCurrent,
                IsWorktree = isSecondaryWorktree,
                WorktreePath = wtPath,
                IsStar = isStar,
                BadgeText = badge
            };
            currentParent.Children.Add(leaf);
        }
    }
}
