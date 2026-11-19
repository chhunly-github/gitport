using System.IO;
using GitPort.Services;

namespace GitPort.Tests;

public sealed class GitWorkflowTests : IDisposable
{
    private sealed class CaptureProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    [Fact]
    public async Task PushProgress_HandlesCarriageReturnsAndPreservesDiagnostics()
    {
        const string output = "Counting objects: 50% (1/2)\rCounting objects: 100% (2/2), done.\r\nWriting objects: 100% (2/2), 234 bytes | 234.00 KiB/s, done.\nremote: accepted";
        var updates = new List<string>();
        var captured = await GitService.ReadGitProgressAsync(new StringReader(output), new CaptureProgress(updates.Add));
        Assert.Equal(output, captured);
        Assert.Equal(4, updates.Count);
        Assert.Equal("Counting objects: 50% (1/2)", updates[0]);
        Assert.Contains("234 bytes", updates[2]);
        Assert.Equal("remote: accepted", updates[3]);
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GitPortWorkflow_" + Guid.NewGuid().ToString("N"));
    public GitWorkflowTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        if (!Path.GetFullPath(_root).StartsWith(Path.Combine(Path.GetTempPath(), "GitPortWorkflow_"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected fixture directory.");
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private async Task<GitService> Repo(string name, ProfileService? profiles = null)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        var git = new GitService(path, profiles: profiles);
        await Ok(git, "init", "-b", "main");
        await Ok(git, "config", "user.name", "Fixture User");
        await Ok(git, "config", "user.email", "fixture@example.test");
        await Ok(git, "config", "commit.gpgSign", "false");
        return git;
    }

    private static async Task<string> Ok(GitService git, params string[] args)
    {
        var result = await git.RunGitAsync(args);
        Assert.True(result.ExitCode == 0, result.StdErr);
        return result.StdOut.Trim();
    }

    private static async Task Write(GitService git, string name, string content) =>
        await File.WriteAllTextAsync(Path.Combine(git.RepositoryPath, name), content);

    [Fact]
    public void Profiles_ResolveByRepositoryThenMostSpecificRemote_AndPersist()
    {
        string path = Path.Combine(_root, "profiles.json");
        var profiles = new ProfileService(path);
        var host = new GitProfile { Name = "Office", RemoteMatch = "gogs.local:3000" };
        var team = new GitProfile { Name = "Team", RemoteMatch = "http://gogs.local:3000/team" };
        profiles.Profiles.AddRange([host, team]);
        Assert.Same(team, profiles.Resolve(_root, "http://gogs.local:3000/team/repo.git"));
        Assert.Same(host, profiles.Resolve(_root, "http://gogs.local:3000/teammate/repo.git"));
        Assert.Null(profiles.Resolve(_root, "http://gogs.local.evil:3000/team/repo.git"));
        profiles.Assign(_root, host);
        profiles.Save();
        var loaded = new ProfileService(path);
        Assert.Equal(host.Id, loaded.Resolve(_root, "http://gogs.local:3000/team/repo.git")?.Id);
        loaded.Assign(_root, null);
        Assert.Equal(team.Id, loaded.Resolve(_root, "http://gogs.local:3000/team/repo.git")?.Id);
        Assert.True(ProfileService.MatchesRemote("gogs.local", "git@gogs.local:team/repo.git"));
    }

    [Fact]
    public async Task Profiles_OverrideCommitIdentityWithoutChangingRepositoryConfig()
    {
        var profiles = new ProfileService(Path.Combine(_root, "profiles.json"));
        var git = await Repo("identity", profiles);
        var profile = new GitProfile { Name = "Work", AuthorName = "Work User", AuthorEmail = "work@example.test" };
        profiles.Profiles.Add(profile);
        profiles.Assign(git.RepositoryPath, profile);
        await git.ResolveProfileAsync();
        await Write(git, "file.txt", "hello");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("initial")).Success);
        Assert.Equal("Work User <work@example.test>", await Ok(git, "log", "-1", "--format=%an <%ae>"));
        Assert.Equal("Fixture User", await Ok(git, "config", "--local", "user.name"));
    }

    [Fact]
    public async Task Status_PreservesUnicodeAndSpaces_AndShowsBothStagingStates()
    {
        var git = await Repo("status");
        string file = "ខ្មែរ space.txt";
        await Write(git, file, "first\n");
        Assert.True(await git.StageFilesAsync([file]));
        Assert.True(await git.UnstageAllAsync()); // unborn HEAD
        Assert.Single(await git.GetWorkingTreeChangesAsync());
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("initial")).Success);
        await Write(git, file, "second\n");
        Assert.True(await git.StageFilesAsync([file]));
        await Write(git, file, "third\n");
        var changes = await git.GetWorkingTreeChangesAsync();
        Assert.Contains(changes, c => c.Path == file && c.IsStaged);
        Assert.Contains(changes, c => c.Path == file && !c.IsStaged);
        Assert.Contains("second", await git.GetWorkingTreeFileDiffAsync(file, true));
        Assert.Contains("third", await git.GetWorkingTreeFileDiffAsync(file, false));
        Assert.Contains("third", await git.GetCommitPreviewDiffAsync(file));
        Assert.Equal(file, Assert.Single(await git.GetCommitFilesAsync("HEAD")).Path);
    }

    [Fact]
    public async Task CommitSelection_DoesNotCommitUncheckedStagedFiles()
    {
        var git = await Repo("selection");
        await Write(git, "selected.txt", "yes");
        await Write(git, "unchecked.txt", "no");
        Assert.True(await git.StageAllAsync());
        var commit = await git.CommitFilesAsync("selected only", ["selected.txt"]);
        Assert.True(commit.Success, commit.Output);
        Assert.Equal("selected.txt", Assert.Single(await git.GetCommitFilesAsync("HEAD")).Path);
        Assert.Contains(await git.GetWorkingTreeChangesAsync(), c => c.Path == "unchecked.txt" && c.IsStaged);
        var amend = await git.CommitFilesAsync("amend selected only", [], amend: true);
        Assert.True(amend.Success, amend.Output);
        Assert.Equal("selected.txt", Assert.Single(await git.GetCommitFilesAsync("HEAD")).Path);
        Assert.Contains(await git.GetWorkingTreeChangesAsync(), c => c.Path == "unchecked.txt" && c.IsStaged);
    }

    [Fact]
    public async Task NamedRemote_PushesCurrentHeadToTargetBranch_AndTracksIncoming()
    {
        var git = await Repo("local");
        string remote = Path.Combine(_root, "server.git");
        await Ok(git, "init", "--bare", remote);
        await Ok(git, "remote", "add", "office", remote);
        await Write(git, "file.txt", "first");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("initial")).Success);
        var updates = new List<string>();
        var push = await git.PushWithDetailsAsync("release", "office", progress: new CaptureProgress(updates.Add));
        Assert.True(push.Success, push.Error);
        Assert.Contains(updates, line => System.Text.RegularExpressions.Regex.IsMatch(line,
            @"Writing objects:\s+100% \(\d+/\d+\),\s+[\d.]+\s+(bytes|[KMGT]iB)"));
        Assert.Contains("Writing objects:", push.Error);
        Assert.Equal(await git.GetHeadCommitHashAsync(), await Ok(git, "rev-parse", "office/release"));
        Assert.Equal("office", await git.GetDefaultRemoteAsync());
        Assert.Empty(await git.GetOutgoingCommitsAsync("release", "office"));
        await Write(git, "file.txt", "second");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("second")).Success);
        Assert.Single(await git.GetOutgoingCommitsAsync("release", "office"));
        await Ok(git, "branch", "--unset-upstream");
        Assert.Single(await git.GetOutgoingCommitsAsync("release", "office"));
        Assert.True((await git.PushWithDetailsAsync("release", "office")).Success);
        var other = await Repo("other");
        await Ok(other, "remote", "add", "office", remote);
        await Ok(other, "fetch", "office");
        Assert.True(await other.SwitchBranchAsync("office/release"));
        await Write(other, "other.txt", "remote work");
        Assert.True(await other.StageAllAsync());
        Assert.True((await other.CommitAsync("remote work")).Success);
        Assert.True((await other.PushWithDetailsAsync("release", "office")).Success);
        var rejected = await git.PushWithDetailsAsync("release", "office");
        Assert.False(rejected.Success);
        Assert.True(GitService.IsPushRejectedFailure(rejected.Error));
        Assert.True((await git.FetchWithDetailsAsync("office")).Success);
        Assert.Equal(1, await git.GetIncomingCountAsync("main"));
        Assert.True((await git.PullWithDetailsAsync()).Success);
        Assert.Equal(0, await git.GetIncomingCountAsync("main"));
    }

    [Fact]
    public async Task Branches_Worktrees_MergeRebaseCherryPickRevertAndTag()
    {
        var git = await Repo("branches");
        await Write(git, "base.txt", "base\n");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("base")).Success);
        string baseHash = await git.GetHeadCommitHashAsync();
        Assert.True(await git.CreateBranchAsync("feature/nested"));
        await Write(git, "feature.txt", "feature\n");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("feature")).Success);
        string featureHash = await git.GetHeadCommitHashAsync();
        Assert.True(await git.SwitchBranchAsync("main"));
        Assert.True((await git.PullWithDetailsAsync("feature/nested")).Success);
        Assert.Equal(featureHash, await git.GetHeadCommitHashAsync());
        Assert.Contains("feature.txt", await git.CompareAsync(baseHash, "HEAD"));
        Assert.Empty(await git.CompareAsync("HEAD"));
        Assert.True(await git.CreateTagAsync("v-test", "HEAD"));
        Assert.True(await git.RevertCommitAsync(featureHash));
        Assert.False(File.Exists(Path.Combine(git.RepositoryPath, "feature.txt")));
        Assert.True(await git.CreateBranchAsync("pick", baseHash));
        Assert.True(await git.CherryPickAsync(featureHash));
        Assert.True(File.Exists(Path.Combine(git.RepositoryPath, "feature.txt")));
        Assert.True(await git.RebaseAsync("main"));
        Assert.True(await git.SwitchBranchAsync("main"));
        Assert.True(await git.MergeAsync("pick"));
        Assert.True(await git.RenameBranchAsync("feature/nested", "feature/renamed"));
        Assert.True(await git.DeleteBranchAsync("feature/renamed"));
        string worktree = Path.Combine(_root, "linked");
        var created = await git.CreateWorktreeAsync(worktree, "worktree/branch", createNewBranch: true);
        Assert.True(created.Success, created.Error);
        Assert.Contains(await git.GetWorktreesAsync(), w => w.Path == worktree && w.DisplayBranch == "worktree/branch");
        Assert.True((await git.RemoveWorktreeAsync(worktree)).Success);
        await Write(git, "reset.txt", "reset example");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("reset example")).Success);
        Assert.True(await git.ResetCurrentBranchAsync(baseHash, "soft"));
        Assert.NotEmpty(await git.GetWorkingTreeChangesAsync());
    }

    [Fact]
    public async Task Rename_UnstagesBothPaths_AndDiscardRestoresTrackedAndRemovesUntracked()
    {
        var git = await Repo("rename");
        await Write(git, "old name.txt", "rename me");
        Assert.True(await git.StageAllAsync());
        Assert.True((await git.CommitAsync("initial")).Success);
        await Ok(git, "mv", "old name.txt", "new name.txt");
        var change = Assert.Single(await git.GetWorkingTreeChangesAsync());
        Assert.Equal("new name.txt", change.Path);
        Assert.Equal("old name.txt", change.OriginalPath);
        Assert.True(await git.UnstageFilesAsync([change.Path]));
        Assert.DoesNotContain(await git.GetWorkingTreeChangesAsync(), c => c.IsStaged);
        Assert.True(await git.DiscardFilesAsync(["old name.txt", "new name.txt"]));
        Assert.Empty(await git.GetWorkingTreeChangesAsync());
        Assert.True(File.Exists(Path.Combine(git.RepositoryPath, "old name.txt")));
        Assert.False(File.Exists(Path.Combine(git.RepositoryPath, "new name.txt")));
        await Ok(git, "mv", "old name.txt", "new name.txt");
        var commit = await git.CommitFilesAsync("rename selected file", ["new name.txt"]);
        Assert.True(commit.Success, commit.Output);
        Assert.Equal("new name.txt", await Ok(git, "ls-tree", "--name-only", "HEAD"));
        Assert.Empty(await git.GetWorkingTreeChangesAsync());
    }

    [Fact]
    public void BranchTree_PreservesNamedRemoteAndNestedLocalRefs_WithoutWorktreeCollisions()
    {
        var tree = BranchTreeBuilder.BuildTree("main",
            [new GitWorktree("linked", "abc", "refs/heads/feature/same", false, false, "", false)],
            ["refs/heads/other/same", "refs/remotes/office/feature/same"]);
        var local = tree.Single(n => n.Name == "Local").Children.Single().Children.Single();
        Assert.Equal("other/same", local.FullRef);
        Assert.False(local.IsWorktree);
        var remote = tree.Single(n => n.Name == "Remote").Children.Single().Children.Single().Children.Single();
        Assert.Equal("refs/remotes/office/feature/same", remote.FullRef);
        Assert.False(remote.IsWorktree);
        var graph = GraphService.BuildGraph(
            [new GitCommit("abc", [], "abc", "Dev", "dev@example.test", "Today", "Subject", "(office/feature/same)")],
            "main", "abc", ["office"]);
        Assert.True(Assert.Single(graph[0].RefChips).IsRemote);
    }

    [Fact]
    public async Task Credentials_RememberedEncrypted_AndSessionDoesNotWriteToDisk()
    {
        string path = Path.Combine(_root, "profiles.json");
        var profiles = new ProfileService(path);
        var git = await Repo("credentials", profiles);
        await git.SetCredentialsAsync("http://localhost:3000/team/repo.git", "alice", "temporary-secret", false);
        Assert.False(File.Exists(path));
        await git.SetCredentialsAsync("http://localhost:3000/team/repo.git", "alice", "saved-secret", true);
        string json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("saved-secret", json);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("alice:saved-secret")), json);
        var loaded = new ProfileService(path);
        Assert.NotNull(loaded.Resolve(git.RepositoryPath, "http://localhost:3000/team/repo.git"));
    }
}
