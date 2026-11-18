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

        // 3. Worktrees (Dedicated category for all linked secondary worktrees)
        var worktreesCategory = new GitBranchItem
        {
            Name = "Worktrees",
            FullRef = "WORKTREES",
            Kind = BranchItemKind.Category,
            IsExpanded = true
        };

        foreach (var wt in worktrees.Where(w => !w.IsMain))
        {
            string folderName = System.IO.Path.GetFileName(wt.Path.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(folderName)) folderName = wt.Path;
            string label = $"{folderName} [{wt.DisplayBranch}]";

            var wtItem = new GitBranchItem
            {
                Name = label,
                FullRef = !string.IsNullOrEmpty(wt.Branch) ? wt.Branch : wt.HeadCommit,
                Kind = BranchItemKind.Worktree,
                IsWorktree = true,
                WorktreePath = wt.Path,
                BadgeText = wt.IsLocked ? "🔒" : ""
            };
            worktreesCategory.Children.Add(wtItem);
        }
        rootList.Add(worktreesCategory);

        // Map secondary worktrees by branch name and leaf name for linking in Local branches
        var worktreeBranchMap = new Dictionary<string, GitWorktree>(StringComparer.OrdinalIgnoreCase);
        foreach (var wt in worktrees.Where(w => !w.IsMain))
        {
            string b = wt.DisplayBranch;
            if (!string.IsNullOrEmpty(b))
            {
                worktreeBranchMap[b] = wt;
                if (!b.StartsWith("refs/heads/"))
                {
                    worktreeBranchMap[$"refs/heads/{b}"] = wt;
                }
            }
            if (!string.IsNullOrEmpty(wt.Branch))
            {
                worktreeBranchMap[wt.Branch] = wt;
            }
        }

        // 4. Local
        var localCategory = new GitBranchItem
        {
            Name = "Local",
            Kind = BranchItemKind.Category,
            IsExpanded = true
        };

        // 5. Remote
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

            if (!isRemote && worktreeMap.TryGetValue(b, out var wt))
            {
                if (!wt.IsMain)
                {
                    isSecondaryWorktree = true;
                    wtPath = wt.Path;
                }
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
                FullRef = isRemote ? $"refs/remotes/{b}" : b,
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
