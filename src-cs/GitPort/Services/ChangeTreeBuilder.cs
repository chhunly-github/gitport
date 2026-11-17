using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using GitPort.Models;

namespace GitPort.Services;

public class ChangeTreeBuilder
{
    private class TempNode
    {
        public string Name { get; set; } = "";
        public bool IsFile { get; set; }
        public string Status { get; set; } = "";
        public string FullPath { get; set; } = "";
        public Dictionary<string, TempNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int FileCount { get; set; }
    }

    public static ObservableCollection<GitChangeItem> BuildTree(IEnumerable<GitFileEntry> files)
    {
        var root = new TempNode { Name = "root" };

        foreach (var file in files)
        {
            string normalized = file.Path.Replace('\\', '/').Trim('/');
            var parts = normalized.Split('/');
            TempNode current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                bool isLast = (i == parts.Length - 1);

                if (!current.Children.TryGetValue(part, out var next))
                {
                    next = new TempNode
                    {
                        Name = part,
                        IsFile = isLast,
                        Status = isLast ? file.Status : "",
                        FullPath = string.Join('/', parts.Take(i + 1))
                    };
                    current.Children[part] = next;
                }
                current = next;
            }
        }

        // Count files
        CountFiles(root);

        // Compact folders and convert to ObservableCollection
        var result = new ObservableCollection<GitChangeItem>();
        foreach (var child in root.Children.Values)
        {
            var item = ConvertAndCompact(child);
            if (item != null)
            {
                result.Add(item);
            }
        }
        return result;
    }

    private static int CountFiles(TempNode node)
    {
        if (node.IsFile)
        {
            node.FileCount = 1;
            return 1;
        }

        int count = 0;
        foreach (var child in node.Children.Values)
        {
            count += CountFiles(child);
        }
        node.FileCount = count;
        return count;
    }

    private static GitChangeItem? ConvertAndCompact(TempNode node)
    {
        if (node.IsFile)
        {
            return new GitChangeItem
            {
                Name = node.Name,
                RelativePath = node.FullPath,
                IsDirectory = false,
                Status = node.Status,
                FileCount = 1
            };
        }

        // It's a directory: compact single-child chains
        // E.g. app -> Http -> Controllers -> Banks
        string displayName = node.Name;
        TempNode current = node;

        while (current.Children.Count == 1)
        {
            var singleChild = current.Children.Values.First();
            if (singleChild.IsFile) break; // Don't collapse folder into file

            displayName += "/" + singleChild.Name;
            current = singleChild;
        }

        var folderItem = new GitChangeItem
        {
            Name = displayName,
            RelativePath = current.FullPath,
            IsDirectory = true,
            FileCount = node.FileCount,
            IsExpanded = true
        };

        foreach (var child in current.Children.Values.OrderBy(c => c.IsFile).ThenBy(c => c.Name))
        {
            var childItem = ConvertAndCompact(child);
            if (childItem != null)
            {
                folderItem.Children.Add(childItem);
            }
        }

        return folderItem;
    }
}
