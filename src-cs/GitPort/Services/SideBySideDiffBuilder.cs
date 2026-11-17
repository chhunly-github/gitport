using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GitPort.Models;

namespace GitPort.Services;

public static class SideBySideDiffBuilder
{
    private static readonly Regex HunkRegex = new(@"^@@\s+-(\d+)(?:,(\d+))?\s+\+(\d+)(?:,(\d+))?\s+@@(.*)$", RegexOptions.Compiled);

    public static DiffDocument Build(string rawDiff, string filePath = "")
    {
        var doc = new DiffDocument { FilePath = filePath };
        if (string.IsNullOrWhiteSpace(rawDiff))
        {
            doc.SideBySideRows.Add(new SideBySideDiffRow
            {
                LeftText = "(No differences)",
                RightText = "(No differences)",
                LeftType = DiffLineType.Unchanged,
                RightType = DiffLineType.Unchanged
            });
            return doc;
        }

        var lines = rawDiff.Split('\n');
        var pendingDeleted = new List<string>();
        var pendingAdded = new List<string>();
        int oldLine = 1;
        int newLine = 1;
        bool inHunks = false;

        void FlushPending()
        {
            if (pendingDeleted.Count == 0 && pendingAdded.Count == 0) return;

            int count = Math.Max(pendingDeleted.Count, pendingAdded.Count);
            for (int i = 0; i < count; i++)
            {
                var row = new SideBySideDiffRow();

                if (i < pendingDeleted.Count)
                {
                    row.LeftLineNumber = oldLine++;
                    row.LeftText = pendingDeleted[i];
                    row.LeftType = DiffLineType.Deleted;
                }
                else
                {
                    row.LeftType = DiffLineType.Empty;
                }

                if (i < pendingAdded.Count)
                {
                    row.RightLineNumber = newLine++;
                    row.RightText = pendingAdded[i];
                    row.RightType = DiffLineType.Added;
                }
                else
                {
                    row.RightType = DiffLineType.Empty;
                }

                doc.SideBySideRows.Add(row);
            }

            pendingDeleted.Clear();
            pendingAdded.Clear();
        }

        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');

            // Also keep unified line for toggle mode
            doc.UnifiedLines.Add(new DiffLineItem { Text = line });

            var match = HunkRegex.Match(line);
            if (match.Success)
            {
                FlushPending();
                inHunks = true;

                oldLine = int.Parse(match.Groups[1].Value);
                newLine = int.Parse(match.Groups[3].Value);

                string context = match.Groups[5].Value.Trim();
                doc.SideBySideRows.Add(new SideBySideDiffRow
                {
                    IsHunkHeader = true,
                    HunkHeaderText = line,
                    LeftType = DiffLineType.HunkHeader,
                    RightType = DiffLineType.HunkHeader,
                    LeftText = $"@@ -{oldLine} +{newLine} @@ {context}",
                    RightText = $"@@ -{oldLine} +{newLine} @@ {context}"
                });
                continue;
            }

            if (!inHunks)
            {
                // Diff metadata header lines (diff --git, index, ---, +++)
                continue;
            }

            if (line.StartsWith('-') && !line.StartsWith("---"))
            {
                doc.DeletionsCount++;
                pendingDeleted.Add(line.Length > 1 ? line.Substring(1) : "");
            }
            else if (line.StartsWith('+') && !line.StartsWith("+++"))
            {
                doc.AdditionsCount++;
                pendingAdded.Add(line.Length > 1 ? line.Substring(1) : "");
            }
            else if (line.StartsWith(' '))
            {
                FlushPending();
                string text = line.Length > 1 ? line.Substring(1) : "";
                doc.SideBySideRows.Add(new SideBySideDiffRow
                {
                    LeftLineNumber = oldLine++,
                    RightLineNumber = newLine++,
                    LeftText = text,
                    RightText = text,
                    LeftType = DiffLineType.Unchanged,
                    RightType = DiffLineType.Unchanged
                });
            }
            else if (line.StartsWith('\\'))
            {
                // \ No newline at end of file - ignore
            }
        }

        FlushPending();

        // If no hunks were detected (e.g. untracked file output), format directly
        if (doc.SideBySideRows.Count == 0 && lines.Length > 0)
        {
            int lineNum = 1;
            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                string text = line.StartsWith('+') ? line.Substring(1) : line;
                doc.AdditionsCount++;
                doc.SideBySideRows.Add(new SideBySideDiffRow
                {
                    LeftType = DiffLineType.Empty,
                    RightLineNumber = lineNum++,
                    RightText = text,
                    RightType = DiffLineType.Added
                });
            }
        }

        return doc;
    }
}
