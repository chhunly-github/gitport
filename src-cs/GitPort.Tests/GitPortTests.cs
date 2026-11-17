using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GitPort.Models;
using GitPort.Services;
using Xunit;

namespace GitPort.Tests;

public class GitPortTests
{
    [Fact]
    public void TestChangeTreeBuilder_CompactsSingleChildPaths()
    {
        var files = new List<GitFileEntry>
        {
            new("M", "sample-project/src/controllers/admin/SettingsController.cs"),
            new("M", "sample-project/resources/views/templates/settings/form.html"),
            new("M", "sample-project/resources/views/templates/settings/index.html"),
            new("M", "sample-project/routes/api/settings.cs")
        };

        var tree = ChangeTreeBuilder.BuildTree(files);

        Assert.Single(tree);
        var root = tree[0];
        Assert.Equal("sample-project", root.Name);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.Children.Count);

        // Verify compaction: src/controllers/admin (1 file)
        var adminDir = root.Children.FirstOrDefault(c => c.Name == "src/controllers/admin");
        Assert.NotNull(adminDir);
        Assert.Equal(1, adminDir.FileCount);
        Assert.Single(adminDir.Children);
        Assert.Equal("SettingsController.cs", adminDir.Children[0].Name);
        Assert.Equal("M", adminDir.Children[0].Status);
    }

    [Fact]
    public void TestBranchTreeBuilder_StructureAndGrouping()
    {
        var refs = new List<string>
        {
            "refs/heads/ly",
            "refs/heads/master",
            "refs/heads/agents/migrate-companycontroller",
            "refs/heads/agents/worktree-agent-1",
            "refs/remotes/origin/main",
            "refs/remotes/origin/master",
            "refs/remotes/origin/Dr.Dev",
            "refs/remotes/origin/ly"
        };

        var worktrees = new List<GitWorktree>
        {
            new(@"C:\Repos\main-project", "head1", "refs/heads/ly", true, false, "", false),
            new(@"C:\Repos\main-project-agent", "head2", "refs/heads/agents/worktree-agent-1", false, false, "", false)
        };

        var tree = BranchTreeBuilder.BuildTree("ly", worktrees, refs);

        Assert.Equal(4, tree.Count); // All Branches, HEAD, Local, Remote
        Assert.Equal("All Branches", tree[0].Name);
        Assert.Equal("HEAD (Current Branch)", tree[1].Name);
        Assert.Equal("Local", tree[2].Name);
        Assert.Equal("Remote", tree[3].Name);

        var local = tree[2];
        // local should contain ly (current), master (isStar), agents (folder)
        var lyBranch = local.Children.FirstOrDefault(c => c.Name == "ly");
        Assert.NotNull(lyBranch);
        Assert.True(lyBranch.IsCurrent);

        var masterBranch = local.Children.FirstOrDefault(c => c.Name == "master");
        Assert.NotNull(masterBranch);
        Assert.True(masterBranch.IsStar);

        var agentsFolder = local.Children.FirstOrDefault(c => c.Name == "agents");
        Assert.NotNull(agentsFolder);
        Assert.Equal(BranchItemKind.Folder, agentsFolder.Kind);
        Assert.Equal(2, agentsFolder.Children.Count);

        var wtBranch = agentsFolder.Children.FirstOrDefault(c => c.Name == "worktree-agent-1");
        Assert.NotNull(wtBranch);
        Assert.True(wtBranch.IsWorktree);

        // Remote
        var remote = tree[3];
        var originFolder = remote.Children.FirstOrDefault(c => c.Name == "origin");
        Assert.NotNull(originFolder);
        var originMain = originFolder.Children.FirstOrDefault(c => c.Name == "main");
        Assert.NotNull(originMain);
        Assert.True(originMain.IsStar);
    }

    [Fact]
    public void TestGraphService_BuildsLanesAndBullseye()
    {
        var commits = new List<GitCommit>
        {
            new("c111", new[] { "c222" }, "c111", "author", "a@test.com", "Yesterday 12:00", "Top commit", "(HEAD -> ly, origin/ly)"),
            new("c222", new[] { "c333", "c444" }, "c222", "author", "a@test.com", "Yesterday 11:00", "Merge commit", ""),
            new("c333", new[] { "c555" }, "c333", "author", "a@test.com", "Yesterday 10:00", "Branch A", ""),
            new("c444", new[] { "c555" }, "c444", "author", "a@test.com", "Yesterday 09:00", "Branch B", "(origin/Dr.Dev)"),
            new("c555", Array.Empty<string>(), "c555", "author", "a@test.com", "Yesterday 08:00", "Root commit", "(origin/master)")
        };

        var items = GraphService.BuildGraph(commits, "ly", "c111");

        Assert.Equal(5, items.Count);

        // c111 is HEAD with merged WebStorm chip "origin & ly"
        Assert.True(items[0].IsHead);
        Assert.Equal(0, items[0].Lane);
        Assert.Single(items[0].RefChips);
        Assert.Equal("origin & ly", items[0].RefChips[0].Name);

        // c222 is merge commit with 2 parents
        Assert.Equal(2, items[1].OutgoingEdges.Count);
        Assert.Contains(items[1].OutgoingEdges, e => e.IsMerge);

        // c444 has origin/Dr.Dev chip
        Assert.Single(items[3].RefChips);
        Assert.Equal("origin/Dr.Dev", items[3].RefChips[0].Name);
        Assert.True(items[3].RefChips[0].IsRemote);
    }

    [Fact]
    public void TestGraphService_BuildGraph_TopologicalOrdering()
    {
        var commits = new List<GitCommit>
        {
            new("c1", new[] { "c2" }, "c1", "Dev", "dev@example.com", "2026-11-17", "Commit 1", "(HEAD -> main)"),
            new("c2", new[] { "c3" }, "c2", "Dev", "dev@example.com", "2026-11-17", "Commit 2", ""),
            new("c3", Array.Empty<string>(), "c3", "Dev", "dev@example.com", "2026-11-17", "Initial commit", "")
        };

        var graph = GraphService.BuildGraph(commits, "main", "c1");
        Assert.Equal(3, graph.Count);
        Assert.True(graph[0].IsHead);
        Assert.Equal(0, graph[0].Lane);
        Assert.Equal("Commit 1", graph[0].Subject);
    }

    [Fact]
    public void TestRepositoryCatalogService_LoadsExistingSettings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "GitPortTest_" + Guid.NewGuid().ToString("N"));
        string testRepo1 = Path.Combine(tempDir, "repo1");
        string testRepo2 = Path.Combine(tempDir, "repo2");
        Directory.CreateDirectory(testRepo1);
        Directory.CreateDirectory(testRepo2);
        string settingsDir = Path.Combine(tempDir, "data", "profiles", "default");
        Directory.CreateDirectory(settingsDir);
        string settingsFile = Path.Combine(settingsDir, "settings.properties");
        File.WriteAllText(settingsFile, $"repos={testRepo1.Replace(@"\", @"\\")}\\n{testRepo2.Replace(@"\", @"\\")}\nlastRepo={testRepo1.Replace(@"\", @"\\")}", System.Text.Encoding.UTF8);

        try
        {
            var catalog = new RepositoryCatalogService(settingsFile);
            var repos = catalog.GetRepositories();

            Assert.NotEmpty(repos);
            Assert.Contains(repos, r => r.Contains("repo1") || r.Contains("repo2"));

            var groups = catalog.BuildGroups(repos[0]);
            Assert.NotEmpty(groups);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void TestRepositoryCatalogService_RemoveAndPruneRepositories()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "GitPortTest_" + Guid.NewGuid().ToString("N"));
        string testRepo1 = Path.Combine(tempDir, "repo1");
        string testRepo2 = Path.Combine(tempDir, "repo2");
        string missingRepo = Path.Combine(tempDir, "missing_repo_3");
        Directory.CreateDirectory(testRepo1);
        Directory.CreateDirectory(testRepo2);
        string settingsDir = Path.Combine(tempDir, "data", "profiles", "default");
        Directory.CreateDirectory(settingsDir);
        string settingsFile = Path.Combine(settingsDir, "settings.properties");

        try
        {
            var catalog = new RepositoryCatalogService(settingsFile);
            catalog.AddRepository(testRepo1);
            catalog.AddRepository(testRepo2);
            catalog.SetLastRepo(testRepo1);

            // Verify both exist
            var repos = catalog.GetRepositories();
            Assert.Equal(2, repos.Count);

            // Remove testRepo1
            catalog.RemoveRepository(testRepo1);
            repos = catalog.GetRepositories();
            Assert.Single(repos);
            Assert.Equal(testRepo2, repos[0]);
            Assert.Equal(testRepo2, catalog.GetLastRepo());

            // Add missing repository manually to raw
            catalog.AddRepository(missingRepo);
            var missing = catalog.GetMissingRepositories();
            Assert.Single(missing);
            Assert.Equal(missingRepo, missing[0]);

            // Prune missing
            var pruned = catalog.PruneMissingRepositories();
            Assert.Single(pruned);
            Assert.Empty(catalog.GetMissingRepositories());
            Assert.Single(catalog.GetRepositories());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void TestBranchTreeBuilder_BuildTree_Structure()
    {
        var worktrees = new List<GitWorktree>
        {
            new(@"C:\Repos\App", "head1", "refs/heads/main", true, false, "", false)
        };
        var refs = new List<string>
        {
            "refs/heads/main",
            "refs/remotes/origin/main"
        };

        var tree = BranchTreeBuilder.BuildTree("main", worktrees, refs);
        Assert.Equal(4, tree.Count); // All Branches, HEAD, Local, Remote
    }

    [Fact]
    public void TestWorkingTreeItem_DisplayAndColors()
    {
        var itemModified = new WorkingTreeItem { Path = "src/index.ts", Status = "M", IsStaged = false };
        Assert.Equal("M", itemModified.DisplayStatus);
        Assert.Equal("UNSTAGED", itemModified.StageStatusText);

        var itemUntracked = new WorkingTreeItem { Path = "newfile.txt", Status = "??", IsStaged = false };
        Assert.Equal("U", itemUntracked.DisplayStatus);

        var itemStaged = new WorkingTreeItem { Path = "docs/readme.md", Status = "A", IsStaged = true };
        Assert.Equal("A", itemStaged.DisplayStatus);
        Assert.Equal("STAGED", itemStaged.StageStatusText);
    }

    [Fact]
    public void TestDiffLineItem_Creation()
    {
        var addLine = new DiffLineItem { Text = "+ console.log('hello');" };
        var delLine = new DiffLineItem { Text = "- console.log('old');" };
        var hdrLine = new DiffLineItem { Text = "@@ -1,5 +1,6 @@" };

        Assert.StartsWith("+", addLine.Text);
        Assert.StartsWith("-", delLine.Text);
        Assert.StartsWith("@@", hdrLine.Text);
    }

    [Fact]
    public void TestSideBySideDiffBuilder_ParsesAlignedRowsAndStats()
    {
        string rawDiff = @"diff --git a/src/test.txt b/src/test.txt
index 1234567..89abcdef 100644
--- a/src/test.txt
+++ b/src/test.txt
@@ -1,4 +1,5 @@
 context 1
-old line
+new line 1
+new line 2
 context 2";

        var doc = SideBySideDiffBuilder.Build(rawDiff, "src/test.txt");

        Assert.Equal("src/test.txt", doc.FilePath);
        Assert.Equal(2, doc.AdditionsCount);
        Assert.Equal(1, doc.DeletionsCount);

        // Rows should have hunk header first
        Assert.True(doc.SideBySideRows.Count >= 4);
        var hunkRow = doc.SideBySideRows[0];
        Assert.True(hunkRow.IsHunkHeader);
        Assert.Contains("@@", hunkRow.LeftText);

        // Row 1: context line
        var r1 = doc.SideBySideRows[1];
        Assert.Equal(1, r1.LeftLineNumber);
        Assert.Equal(1, r1.RightLineNumber);
        Assert.Equal("context 1", r1.LeftText);
        Assert.Equal("context 1", r1.RightText);

        // Row 2: modification paired (old line vs new line 1)
        var r2 = doc.SideBySideRows[2];
        Assert.Equal(2, r2.LeftLineNumber);
        Assert.Equal(2, r2.RightLineNumber);
        Assert.Equal("old line", r2.LeftText);
        Assert.Equal("new line 1", r2.RightText);
        Assert.Equal(DiffLineType.Deleted, r2.LeftType);
        Assert.Equal(DiffLineType.Added, r2.RightType);

        // Row 3: additional new line (empty on left)
        var r3 = doc.SideBySideRows[3];
        Assert.Null(r3.LeftLineNumber);
        Assert.Equal(3, r3.RightLineNumber);
        Assert.Equal(DiffLineType.Empty, r3.LeftType);
        Assert.Equal(DiffLineType.Added, r3.RightType);
        Assert.Equal("new line 2", r3.RightText);
    }

    [Fact]
    public async Task TestGitService_QueryBranchesAndCommits()
    {
        var git = new GitService(Directory.GetCurrentDirectory());
        var branch = await git.GetCurrentBranchAsync();
        Assert.False(string.IsNullOrEmpty(branch));

        var changes = await git.GetWorkingTreeChangesAsync();
        Assert.NotNull(changes);
    }
}
