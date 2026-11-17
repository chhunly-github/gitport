using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitPort.Services;

public record GitWorktree(
    string Path,
    string HeadCommit,
    string Branch,
    bool IsMain,
    bool IsLocked,
    string LockReason,
    bool IsPrunable
) {
    public string DisplayBranch => string.IsNullOrEmpty(Branch)
        ? (IsMain ? "HEAD" : (HeadCommit.Length >= 7 ? $"(detached at {HeadCommit[..7]})" : "(detached)"))
        : Branch.StartsWith("refs/heads/") ? Branch.Substring(11) : Branch;
}

public record GitCommit(
    string Hash,
    string[] Parents,
    string ShortHash,
    string Author,
    string Email,
    string Date,
    string Subject,
    string Refs
) {
    public bool IsWorkingTree => Hash == "WORKING_TREE";
}

public record GitChange(string Status, string Path, bool IsStaged);

public record GitFileEntry(string Status, string Path);

public class GitService
{
    private readonly string _gitExe;
    public string RepositoryPath { get; private set; }

    public GitService(string repoPath, string? gitExe = null)
    {
        RepositoryPath = repoPath;
        _gitExe = gitExe ?? FindGitExecutable();
    }

    public void SetRepository(string repoPath)
    {
        RepositoryPath = repoPath;
    }

    public static string FindGitExecutable()
    {
        // 1. Check bundled git beside the app
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string bundledGit = Path.Combine(appDir, @"git\cmd\git.exe");
        if (File.Exists(bundledGit)) return Path.GetFullPath(bundledGit);

        // 2. Check PATH
        return "git.exe";
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gitExe,
            WorkingDirectory = RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }

    public async Task<string> GetHeadCommitHashAsync()
    {
        var (code, stdout, _) = await RunGitAsync("rev-parse", "HEAD");
        return code == 0 ? stdout.Trim() : "";
    }

    public async Task<string> GetCurrentBranchAsync()
    {
        var (code, stdout, _) = await RunGitAsync("symbolic-ref", "--quiet", "--short", "HEAD");
        if (code == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }

        var (code2, stdout2, _) = await RunGitAsync("rev-parse", "--short", "HEAD");
        return code2 == 0 ? $"HEAD ({stdout2.Trim()})" : "Detached HEAD";
    }

    public async Task<List<GitWorktree>> GetWorktreesAsync()
    {
        var (code, stdout, _) = await RunGitAsync("worktree", "list", "--porcelain");
        var list = new List<GitWorktree>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        string currentPath = "";
        string currentHead = "";
        string currentBranch = "";
        bool isLocked = false;
        string lockReason = "";
        bool isPrunable = false;
        bool isMain = true;

        foreach (var line in stdout.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith("worktree "))
            {
                if (!string.IsNullOrEmpty(currentPath))
                {
                    list.Add(new GitWorktree(currentPath, currentHead, currentBranch, isMain, isLocked, lockReason, isPrunable));
                    isMain = false;
                    isLocked = false;
                    lockReason = "";
                    isPrunable = false;
                    currentBranch = "";
                    currentHead = "";
                }
                string rawPath = t.Substring(9).Trim();
                try
                {
                    currentPath = Path.GetFullPath(rawPath);
                }
                catch
                {
                    currentPath = rawPath;
                }
            }
            else if (t.StartsWith("HEAD ")) currentHead = t.Substring(5).Trim();
            else if (t.StartsWith("branch ")) currentBranch = t.Substring(7).Trim();
            else if (t.StartsWith("locked"))
            {
                isLocked = true;
                lockReason = t.Length > 7 ? t.Substring(7).Trim() : "";
            }
            else if (t.StartsWith("prunable")) isPrunable = true;
        }

        if (!string.IsNullOrEmpty(currentPath))
        {
            list.Add(new GitWorktree(currentPath, currentHead, currentBranch, isMain, isLocked, lockReason, isPrunable));
        }

        return list;
    }

    public async Task<List<string>> GetBranchRefsAsync()
    {
        var (code, stdout, _) = await RunGitAsync("for-each-ref", "--format=%(refname)", "refs/heads/", "refs/remotes/");
        var list = new List<string>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        foreach (var line in stdout.Split('\n'))
        {
            string r = line.Trim();
            if (!string.IsNullOrEmpty(r) && !r.EndsWith("/HEAD"))
            {
                list.Add(r);
            }
        }
        return list;
    }

    public async Task<int> GetIncomingCountAsync(string branch)
    {
        var (code, stdout, _) = await RunGitAsync("rev-list", "--count", $"{branch}..origin/{branch}");
        if (code == 0 && int.TryParse(stdout.Trim(), out int count))
        {
            return count;
        }
        return 0;
    }

    public async Task<List<GitCommit>> GetCommitsAsync(string? targetRef = null, int max = 300)
    {
        var args = new List<string> { "log", "-n", $"{max}", "--date-order" };
        if (!string.IsNullOrEmpty(targetRef) && targetRef != "ALL")
        {
            args.Add(targetRef);
        }
        else
        {
            args.Add("--all");
        }
        args.AddRange(new[] {
            "--format=%H%x1f%P%x1f%h%x1f%an%x1f%ae%x1f%ct%x1f%s%x1f%d"
        });

        var (code, stdout, _) = await RunGitAsync(args.ToArray());
        var list = new List<GitCommit>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        foreach (var line in stdout.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\u001f');
            if (parts.Length >= 7)
            {
                string hash = parts[0].Trim();
                string[] parents = string.IsNullOrWhiteSpace(parts[1]) ? Array.Empty<string>() : parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string shortHash = parts[2].Trim();
                string author = parts[3].Trim();
                string email = parts[4].Trim();
                string dateRaw = parts[5].Trim();
                string subject = parts[6].Trim();
                string refs = parts.Length > 7 ? parts[7].Trim() : "";

                // Format date as Today/Yesterday or dd/MM/yyyy HH:mm
                string dateDisplay = dateRaw;
                if (long.TryParse(dateRaw, out long ts))
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();
                    var now = DateTimeOffset.Now;
                    if (dt.Date == now.Date)
                    {
                        dateDisplay = $"Today {dt:HH:mm}";
                    }
                    else if (dt.Date == now.Date.AddDays(-1))
                    {
                        dateDisplay = $"Yesterday {dt:HH:mm}";
                    }
                    else
                    {
                        dateDisplay = dt.ToString("dd/MM/yyyy HH:mm");
                    }
                }

                list.Add(new GitCommit(hash, parents, shortHash, author, email, dateDisplay, subject, refs));
            }
        }
        return list;
    }

    public async Task<List<GitChange>> GetWorkingTreeChangesAsync()
    {
        var (code, stdout, _) = await RunGitAsync("status", "--porcelain=v1");
        var list = new List<GitChange>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        foreach (var line in stdout.Split('\n'))
        {
            if (line.Length < 4) continue;
            string xy = line.Substring(0, 2);
            string path = line.Substring(3).Trim();
            list.Add(new GitChange(xy.Trim(), path, xy[0] != ' ' && xy[0] != '?'));
        }
        return list;
    }

    public async Task<List<GitFileEntry>> GetCommitFilesAsync(string commitHash)
    {
        var (code, stdout, _) = await RunGitAsync("show", "--name-status", "--oneline", "-M", "-C", commitHash);
        var list = new List<GitFileEntry>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        var lines = stdout.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var tokens = line.Split('\t', 2);
            if (tokens.Length < 2) tokens = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 2)
            {
                list.Add(new GitFileEntry(tokens[0].Trim(), tokens[1].Trim()));
            }
        }
        return list;
    }

    public async Task<string> GetDiffAsync(string hash, string? filePath = null)
    {
        var args = new List<string>();
        if (hash == "WORKING_TREE")
        {
            args.Add("diff");
            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }
        }
        else
        {
            args.AddRange(new[] { "show", "--stat", "--patch", hash });
            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }
        }

        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return code == 0 ? stdout : (string.IsNullOrEmpty(stdout) ? stderr : stdout);
    }

    public async Task<bool> SwitchBranchAsync(string refName)
    {
        var args = new List<string> { "switch" };
        if (refName.StartsWith("origin/"))
        {
            args.Add("--track");
        }
        args.Add(refName);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> CreateBranchAsync(string name, string? baseRef = null)
    {
        var args = new List<string> { "switch", "-c", name };
        if (!string.IsNullOrEmpty(baseRef)) args.Add(baseRef);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<(bool Success, string Error)> CreateWorktreeAsync(
        string path,
        string? branch = null,
        string? baseRef = null,
        bool createNewBranch = false,
        bool detach = false,
        bool force = false)
    {
        var args = new List<string> { "worktree", "add" };
        if (force) args.Add("--force");
        if (detach) args.Add("--detach");

        if (createNewBranch && !string.IsNullOrEmpty(branch))
        {
            args.AddRange(new[] { "-b", branch });
            args.Add(path);
            if (!string.IsNullOrEmpty(baseRef)) args.Add(baseRef);
        }
        else
        {
            args.Add(path);
            if (!string.IsNullOrEmpty(branch))
            {
                args.Add(branch);
            }
            else if (!string.IsNullOrEmpty(baseRef))
            {
                args.Add(baseRef);
            }
        }

        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        if (code == 0) return (true, "");
        string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, string.IsNullOrWhiteSpace(err) ? "Unknown git error" : err.Trim());
    }

    public async Task<(bool Success, string Error)> RemoveWorktreeAsync(string path, bool force = false)
    {
        var args = new List<string> { "worktree", "remove" };
        if (force) args.Add("--force");
        args.Add(path);
        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        if (code == 0) return (true, "");
        string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, string.IsNullOrWhiteSpace(err) ? "Unknown git error" : err.Trim());
    }

    public async Task<(bool Success, string Error)> PruneWorktreesAsync()
    {
        var (code, stdout, stderr) = await RunGitAsync("worktree", "prune");
        if (code == 0) return (true, "");
        string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, string.IsNullOrWhiteSpace(err) ? "Unknown git error" : err.Trim());
    }

    public async Task<(bool Success, string Output)> MergeWithOutputAsync(string refName, bool fetchFirst = false)
    {
        if (fetchFirst)
        {
            string remote = "origin";
            string branch = refName;
            if (refName.StartsWith("origin/"))
            {
                branch = refName.Substring(7);
            }
            else if (refName.StartsWith("refs/remotes/origin/"))
            {
                branch = refName.Substring(20);
            }
            await RunGitAsync("fetch", remote, branch);
        }

        var (code, stdout, stderr) = await RunGitAsync("merge", refName);
        string output = string.IsNullOrEmpty(stdout) ? stderr : stdout;
        return (code == 0, output);
    }

    public async Task<bool> MergeAsync(string refName, bool fetchFirst = false)
    {
        var (ok, _) = await MergeWithOutputAsync(refName, fetchFirst);
        return ok;
    }

    public async Task<bool> RebaseAsync(string refName)
    {
        var (code, _, _) = await RunGitAsync("rebase", refName);
        return code == 0;
    }

    public async Task<bool> PullAsync(string refName, bool rebase = false)
    {
        var args = new List<string> { "pull" };
        if (rebase) args.Add("--rebase");
        if (refName.StartsWith("origin/"))
        {
            args.Add("origin");
            args.Add(refName.Substring(7));
        }
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> DeleteBranchAsync(string refName, bool force = false)
    {
        var args = new List<string> { "branch", force ? "-D" : "-d", refName };
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> StageFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return true;
        var args = new List<string> { "add", "--" };
        args.AddRange(list);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> StageAllAsync()
    {
        var (code, _, _) = await RunGitAsync("add", "-A");
        return code == 0;
    }

    public async Task<bool> UnstageFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return true;
        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(list);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> UnstageAllAsync()
    {
        var (code, _, _) = await RunGitAsync("restore", "--staged", ".");
        return code == 0;
    }

    public async Task<bool> DiscardFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return true;
        var args = new List<string> { "restore", "--" };
        args.AddRange(list);
        await RunGitAsync(args.ToArray());

        var cleanArgs = new List<string> { "clean", "-f", "--" };
        cleanArgs.AddRange(list);
        await RunGitAsync(cleanArgs.ToArray());

        return true;
    }

    public async Task<(bool Success, string Output)> CommitAsync(string message, bool amend = false)
    {
        if (string.IsNullOrWhiteSpace(message)) return (false, "Commit message cannot be empty.");
        var args = new List<string> { "commit" };
        if (amend) args.Add("--amend");
        args.AddRange(new[] { "-m", message });
        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return (code == 0, code == 0 ? stdout : (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
    }

    public async Task<string> GetWorkingTreeFileDiffAsync(string filePath, bool staged)
    {
        if (staged)
        {
            var (code, stdout, stderr) = await RunGitAsync("diff", "--cached", "--", filePath);
            return code == 0 ? stdout : stderr;
        }
        else
        {
            var (code, stdout, stderr) = await RunGitAsync("diff", "--", filePath);
            if (code == 0 && !string.IsNullOrWhiteSpace(stdout)) return stdout;

            string fullPath = Path.Combine(RepositoryPath, filePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(fullPath);
                    var sb = new StringBuilder();
                    sb.AppendLine($"--- /dev/null");
                    sb.AppendLine($"+++ b/{filePath}");
                    sb.AppendLine($"@@ -0,0 +1,{Math.Min(lines.Length, 2000)} @@");
                    for (int i = 0; i < Math.Min(lines.Length, 2000); i++)
                    {
                        sb.AppendLine("+" + lines[i]);
                    }
                    if (lines.Length > 2000) sb.AppendLine("+... [Truncated remaining lines]");
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    return $"Error reading untracked file: {ex.Message}";
                }
            }
            return string.IsNullOrEmpty(stdout) ? stderr : stdout;
        }
    }

    public async Task<bool> FetchAllAsync()
    {
        var (code, _, _) = await RunGitAsync("fetch", "--all", "--prune");
        return code == 0;
    }

    public async Task<bool> RenameBranchAsync(string oldName, string newName)
    {
        var (code, _, _) = await RunGitAsync("branch", "-m", oldName, newName);
        return code == 0;
    }

    public async Task<bool> DeleteRemoteBranchAsync(string remote, string branchName)
    {
        var (code, _, _) = await RunGitAsync("push", remote, "--delete", branchName);
        return code == 0;
    }

    public async Task<bool> ResetCurrentBranchAsync(string hash, string mode)
    {
        var (code, _, _) = await RunGitAsync("reset", $"--{mode}", hash);
        return code == 0;
    }

    public async Task<bool> CherryPickAsync(string hash)
    {
        var (code, _, _) = await RunGitAsync("cherry-pick", hash);
        return code == 0;
    }

    public async Task<bool> RevertCommitAsync(string hash)
    {
        var (code, _, _) = await RunGitAsync("revert", "--no-edit", hash);
        return code == 0;
    }

    public async Task<bool> CreateTagAsync(string tagName, string hash)
    {
        var (code, _, _) = await RunGitAsync("tag", tagName, hash);
        return code == 0;
    }

    public async Task<bool> PushAsync(string? branch = null)
    {
        var args = new List<string> { "push" };
        if (!string.IsNullOrEmpty(branch))
        {
            args.AddRange(new[] { "origin", branch });
        }
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }
}
