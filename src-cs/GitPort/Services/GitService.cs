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

public record GitChange(string Status, string Path, bool IsStaged, string? OriginalPath = null);

public record GitFileEntry(string Status, string Path);

public record GitRemote(string Name, string Url);

public record GitProviderInfo(string Name, string Hint, string Icon);

public class GitService
{
    private readonly string _gitExe;
    private readonly ProfileService? _profiles;
    private readonly Dictionary<string, string> _sessionCredentials = new();
    public GitProfile? ActiveProfile { get; private set; }
    public string RepositoryPath { get; private set; }

    public GitService(string repoPath, string? gitExe = null, ProfileService? profiles = null)
    {
        RepositoryPath = repoPath;
        _gitExe = gitExe ?? FindGitExecutable();
        _profiles = profiles;
    }

    public async Task ResolveProfileAsync()
    {
        ActiveProfile = null;
        if (_profiles != null) ActiveProfile = _profiles.Resolve(RepositoryPath, await GetRemoteUrlAsync());
    }

    public void ClearSessionCredentials() => _sessionCredentials.Clear();

    private void ConfigureProcess(ProcessStartInfo psi)
    {
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "Never";
        var config = new List<(string Key, string Value)> { ("credential.useHttpPath", "true") };
        var profile = ActiveProfile;
        if (profile != null)
        {
            if (!string.IsNullOrWhiteSpace(profile.AuthorName)) config.Add(("user.name", profile.AuthorName));
            if (!string.IsNullOrWhiteSpace(profile.AuthorEmail)) config.Add(("user.email", profile.AuthorEmail));
            if (!string.IsNullOrWhiteSpace(profile.Username)) config.Add(("credential.username", profile.Username));
            if (!string.IsNullOrWhiteSpace(profile.SshKeyPath))
            {
                string key = profile.SshKeyPath.Replace('\\', '/').Replace("'", "'\\''");
                psi.Environment["GIT_SSH_COMMAND"] = $"ssh -i '{key}' -o IdentitiesOnly=yes -o BatchMode=yes";
            }
            foreach (var entry in profile.Credentials)
            {
                try { config.Add(($"http.{entry.Key}.extraHeader", CredentialProtection.Unprotect(entry.Value))); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FormatException)
                { /* A copied portable folder may belong to another Windows user. Ask for login again. */ }
            }
        }
        string sessionPrefix = (profile?.Id ?? "") + "|";
        foreach (var entry in _sessionCredentials.Where(e => e.Key.StartsWith(sessionPrefix, StringComparison.Ordinal)))
        {
            string key = $"http.{entry.Key[sessionPrefix.Length..]}.extraHeader";
            config.RemoveAll(c => c.Key == key);
            config.Add((key, entry.Value));
        }
        if (config.Any(c => c.Key.EndsWith(".extraHeader"))) config.Add(("credential.helper", ""));
        psi.Environment.TryGetValue("GIT_CONFIG_COUNT", out var inheritedCount);
        int count = int.TryParse(inheritedCount, out var inherited) ? inherited : 0;
        foreach (var (key, value) in config)
        {
            psi.Environment[$"GIT_CONFIG_KEY_{count}"] = key;
            psi.Environment[$"GIT_CONFIG_VALUE_{count++}"] = value;
        }
        psi.Environment["GIT_CONFIG_COUNT"] = count.ToString();
    }

    public Task SetCredentialsAsync(string url, string username, string password, bool remember)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Username/password login requires an HTTP or HTTPS remote. Configure an SSH key in Profiles for SSH remotes.");
        if (username.Contains(':') || username.Any(char.IsControl) || password.Any(char.IsControl))
            throw new ArgumentException("Credentials contain unsupported characters.");
        string context = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri.TrimEnd('/');
        string header = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        if (remember && _profiles != null)
        {
            if (ActiveProfile == null)
            {
                ActiveProfile = new GitProfile { Name = Path.GetFileName(RepositoryPath), Username = username };
                _profiles.Profiles.Add(ActiveProfile);
                _profiles.Assign(RepositoryPath, ActiveProfile);
            }
            ActiveProfile.Credentials[context] = CredentialProtection.Protect(header);
            _profiles.Save();
        }
        _sessionCredentials[(ActiveProfile?.Id ?? "") + "|" + context] = header;
        return Task.CompletedTask;
    }

    public void SetRepository(string repoPath)
    {
        if (!string.Equals(RepositoryPath, repoPath, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfile = null;
            _sessionCredentials.Clear();
        }
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
        ConfigureProcess(psi);

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

    public async Task<(int ExitCode, string StdOut, string StdErr)> RunGitWithInputAsync(string input, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gitExe,
            WorkingDirectory = RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        ConfigureProcess(psi);

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();

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
        var (code, stdout, _) = await RunGitAsync("rev-list", "--count", $"{branch}..{branch}@{{upstream}}");
        if (code == 0 && int.TryParse(stdout.Trim(), out int count))
        {
            return count;
        }
        return 0;
    }

    public static List<GitCommit> ParseCommits(string stdout)
    {
        var list = new List<GitCommit>();
        if (string.IsNullOrWhiteSpace(stdout)) return list;

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

    public async Task<List<GitCommit>> GetCommitsAsync(string? targetRef = null, int max = 300)
    {
        var args = new List<string> { "log", "-n", $"{max}", "--topo-order" };
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
        return code == 0 ? ParseCommits(stdout) : new List<GitCommit>();
    }

    public async Task<List<GitCommit>> GetOutgoingCommitsAsync(string? branch = null, string? remote = null)
    {
        string format = "--format=%H%x1f%P%x1f%h%x1f%an%x1f%ae%x1f%ct%x1f%s%x1f%d";

        // 1. Try tracking upstream @{u}..HEAD
        var (code, stdout, _) = await RunGitAsync("log", "@{u}..HEAD", "--date-order", format);
        if (code == 0 && branch == null && remote == null)
        {
            return ParseCommits(stdout);
        }

        // 2. Try against remote branch (e.g. origin/<branch>..HEAD)
        string rem = remote ?? await GetDefaultRemoteAsync();
        string br = branch ?? await GetCurrentBranchAsync();
        if (!string.IsNullOrEmpty(rem) && !string.IsNullOrEmpty(br) && !br.StartsWith("HEAD"))
        {
            var (remoteCode, remoteStdout, _) = await RunGitAsync("log", $"{rem}/{br}..HEAD", "--date-order", format);
            if (remoteCode == 0)
            {
                return ParseCommits(remoteStdout);
            }
        }

        // 3. Fallback: all commits on HEAD not reachable from any remote
        var (codeFallback, stdoutFallback, _) = branch != null || remote != null
            ? await RunGitAsync("log", "HEAD", "--date-order", format)
            : await RunGitAsync("log", "HEAD", "--not", "--remotes", "--date-order", format);
        if (codeFallback == 0 && !string.IsNullOrWhiteSpace(stdoutFallback))
        {
            return ParseCommits(stdoutFallback);
        }

        return new List<GitCommit>();
    }

    public async Task<int> GetOutgoingCommitCountAsync(string? branch = null, string? remote = null)
    {
        var commits = await GetOutgoingCommitsAsync(branch, remote);
        return commits.Count;
    }

    public async Task<List<GitChange>> GetWorkingTreeChangesAsync()
    {
        var (code, stdout, _) = await RunGitAsync("status", "--porcelain=v1", "-z", "--untracked-files=all");
        return code == 0 ? ParseWorkingTreeChanges(stdout) : new List<GitChange>();
    }

    public static List<GitChange> ParseWorkingTreeChanges(string stdout)
    {
        var list = new List<GitChange>();
        var entries = stdout.Split('\0');
        for (int i = 0; i < entries.Length; i++)
        {
            string line = entries[i];
            if (line.Length < 4) continue;
            string xy = line[..2];
            string path = line[3..];
            string? originalPath = null;
            if ((xy.Contains('R') || xy.Contains('C')) && i + 1 < entries.Length) originalPath = entries[++i];
            if (xy == "??") list.Add(new GitChange("??", path, false));
            else if (xy.Contains('U') || xy is "AA" or "DD") list.Add(new GitChange(xy, path, false));
            else
            {
                if (xy[0] != ' ') list.Add(new GitChange(xy[0].ToString(), path, true, originalPath));
                if (xy[1] != ' ') list.Add(new GitChange(xy[1].ToString(), path, false));
            }
        }
        return list;
    }

    public async Task<List<GitFileEntry>> GetCommitFilesAsync(string commitHash)
    {
        var (code, stdout, _) = await RunGitAsync("diff-tree", "--root", "--no-commit-id", "--name-status", "-r", "-M", "--first-parent", "-m", "-z", commitHash);
        var list = new List<GitFileEntry>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        var fields = stdout.Split('\0');
        for (int i = 0; i + 1 < fields.Length;)
        {
            string status = fields[i++];
            string path = fields[i++];
            if ((status.StartsWith('R') || status.StartsWith('C')) && i < fields.Length) path = fields[i++];
            if (status.Length > 0 && path.Length > 0) list.Add(new GitFileEntry(status, path));
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

    public async Task<string> CompareAsync(string fromRef, string? toRef = null)
    {
        var args = new List<string> { "diff", fromRef };
        if (toRef != null) args.Add(toRef);
        args.Add("--");
        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return code == 0 ? stdout : stderr;
    }

    public async Task<bool> SwitchBranchAsync(string refName)
    {
        var args = new List<string> { "switch" };
        var remotes = await GetRemotesAsync();
        if (refName.StartsWith("refs/remotes/")) refName = refName[13..];
        if (remotes.Any(r => refName.StartsWith(r.Name + "/", StringComparison.Ordinal)))
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
            string shortRef = refName.StartsWith("refs/remotes/") ? refName[13..] : refName;
            var remotes = await GetRemotesAsync();
            var remote = remotes.FirstOrDefault(r => shortRef.StartsWith(r.Name + "/", StringComparison.Ordinal));
            if (remote == null) return (false, "Cannot determine the remote for " + refName);
            var fetch = await FetchWithDetailsAsync(remote.Name);
            if (!fetch.Success) return (false, fetch.Error + fetch.Output);
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

    public async Task<List<GitRemote>> GetRemotesAsync()
    {
        var (code, stdout, _) = await RunGitAsync("remote", "-v");
        var list = new List<GitRemote>();
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in stdout.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string name = parts[0];
                string url = parts[1];
                if (seen.Add(name))
                {
                    list.Add(new GitRemote(name, url));
                }
            }
        }
        return list;
    }

    public async Task<string> GetDefaultRemoteAsync()
    {
        var remotes = await GetRemotesAsync();
        string branch = await GetCurrentBranchAsync();
        var (code, configured, _) = await RunGitAsync("config", "--get", $"branch.{branch}.remote");
        if (code == 0 && remotes.Any(r => r.Name == configured.Trim())) return configured.Trim();
        var origin = remotes.FirstOrDefault(r => r.Name.Equals("origin", StringComparison.OrdinalIgnoreCase));
        if (origin != null) return origin.Name;
        return remotes.FirstOrDefault()?.Name ?? "origin";
    }

    public async Task<string> GetRemoteUrlAsync(string? remoteName = null, bool forPush = false)
    {
        string rem = remoteName ?? await GetDefaultRemoteAsync();
        var (code, stdout, _) = forPush
            ? await RunGitAsync("remote", "get-url", "--push", rem)
            : await RunGitAsync("remote", "get-url", rem);
        if (code == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }
        var remotes = await GetRemotesAsync();
        return remotes.FirstOrDefault(r => r.Name.Equals(rem, StringComparison.OrdinalIgnoreCase))?.Url ?? "";
    }

    public async Task<string> GetPushTargetBranchAsync(string remote)
    {
        string current = await GetCurrentBranchAsync();
        var (_, configuredRemote, _) = await RunGitAsync("config", "--get", $"branch.{current}.remote");
        var (code, merge, _) = await RunGitAsync("config", "--get", $"branch.{current}.merge");
        return code == 0 && configuredRemote.Trim() == remote && merge.Trim().StartsWith("refs/heads/")
            ? merge.Trim()[11..] : current;
    }

    public static GitProviderInfo DetectGitProvider(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new GitProviderInfo("Git Server", "Enter your username and password or access token.", "🌐");

        string lower = url.ToLowerInvariant();
        if (lower.Contains("github.com"))
        {
            return new GitProviderInfo("GitHub", "Use your GitHub username and Personal Access Token (PAT). Classic tokens (ghp_) or fine-grained tokens with 'repo' scope are accepted. Account passwords are not supported by GitHub.", "🐙");
        }
        if (lower.Contains("gitlab"))
        {
            return new GitProviderInfo("GitLab", "Use your GitLab username and Personal Access Token (glpat-) with 'read_repository' and 'write_repository' scopes, or your account password.", "🦊");
        }
        if (lower.Contains("gogs") || lower.Contains("gitea"))
        {
            return new GitProviderInfo("Gogs / Gitea", "Use your Gogs/Gitea account username and password, or an Application Access Token.", "☕");
        }
        if (lower.Contains("bitbucket.org"))
        {
            return new GitProviderInfo("Bitbucket", "Use your Bitbucket username and App Password with repository read/write permissions.", "🪣");
        }
        if (lower.Contains("azure.com") || lower.Contains("visualstudio.com"))
        {
            return new GitProviderInfo("Azure DevOps", "Use your Azure DevOps username and Personal Access Token (PAT) with Code (Read & Write) scope.", "☁");
        }

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new GitProviderInfo($"Git Server ({uri.Host})", $"Enter credentials for {uri.Authority}. Supports basic authentication, Personal Access Tokens, and self-hosted instances.", "🌐");
            }
        }
        catch { }

        return new GitProviderInfo("Custom Git Server", "Enter credentials for this repository. Supports basic authentication, Personal Access Tokens, and self-hosted instances.", "🌐");
    }

    public static bool IsAuthFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("could not read Username", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("could not read Password", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("HTTP Basic: Access denied", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("The requested URL returned error: 401", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("The requested URL returned error: 403", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Repository not found", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSslFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("SSL certificate problem", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("self signed certificate", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("certificate verify failed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPushRejectedFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("[rejected]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("fetch first", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsConflictFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Automatic merge failed", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ApproveCredentialsAsync(string url, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"protocol={uri.Scheme}");
        sb.AppendLine($"host={uri.Authority}");
        sb.AppendLine($"username={username}");
        sb.AppendLine($"password={password}");
        sb.AppendLine();

        var (code, _, _) = await RunGitWithInputAsync(sb.ToString(), "credential", "approve");
        return code == 0;
    }

    public async Task<bool> RejectCredentialsAsync(string url, string username)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var sb = new StringBuilder();
        sb.AppendLine($"protocol={uri.Scheme}");
        sb.AppendLine($"host={uri.Authority}");
        sb.AppendLine($"username={username}");
        sb.AppendLine();

        var (code, _, _) = await RunGitWithInputAsync(sb.ToString(), "credential", "reject");
        return code == 0;
    }

    public async Task<bool> SetSslVerifyAsync(bool verify, string? remoteUrl = null)
    {
        string key = remoteUrl == null ? "http.sslVerify" : $"http.{remoteUrl}.sslVerify";
        var (code, _, _) = await RunGitAsync("config", "--local", key, verify ? "true" : "false");
        return code == 0;
    }

    public async Task<(bool Success, string Output, string Error)> PullWithDetailsAsync(
        string? targetRef = null,
        bool rebase = false,
        string? remote = null)
    {
        if (targetRef?.StartsWith("refs/remotes/") == true) targetRef = targetRef[13..];
        var args = new List<string> { "pull" };
        if (rebase) args.Add("--rebase");
        else args.Add("--no-rebase");

        if (string.IsNullOrWhiteSpace(targetRef))
        {
            var (uCode, uStdout, _) = await RunGitAsync("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
            if (uCode != 0 || string.IsNullOrWhiteSpace(uStdout))
            {
                string rem = remote ?? await GetDefaultRemoteAsync();
                string curr = await GetCurrentBranchAsync();
                if (!string.IsNullOrEmpty(rem) && !string.IsNullOrEmpty(curr) && !curr.StartsWith("HEAD"))
                {
                    args.Add(rem);
                    args.Add(curr);
                }
            }
        }
        else if (targetRef.Contains('/'))
        {
            var remotes = await GetRemotesAsync();
            var matched = remotes.FirstOrDefault(r => targetRef.StartsWith(r.Name + "/"));
            if (matched != null)
            {
                args.Add(matched.Name);
                args.Add(targetRef.Substring(matched.Name.Length + 1));
            }
            else
            {
                var (localCode, localOut, localErr) = await RunGitAsync(rebase ? "rebase" : "merge", targetRef);
                return (localCode == 0, localOut, localErr);
            }
        }
        else
        {
            if (rebase)
            {
                var (rCode, rOut, rErr) = await RunGitAsync("rebase", targetRef);
                return (rCode == 0, rOut, rErr);
            }
            else
            {
                var (mCode, mOut, mErr) = await RunGitAsync("merge", targetRef);
                return (mCode == 0, mOut, mErr);
            }
        }

        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return (code == 0, stdout, stderr);
    }

    public async Task<bool> PullAsync(string refName, bool rebase = false)
    {
        var (ok, _, _) = await PullWithDetailsAsync(refName, rebase);
        return ok;
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
        var renames = (await GetWorkingTreeChangesAsync()).Where(c => c.IsStaged && c.Status == "R" && list.Contains(c.Path));
        list.AddRange(renames.Where(c => c.OriginalPath != null).Select(c => c.OriginalPath!));
        bool hasHead = !string.IsNullOrEmpty(await GetHeadCommitHashAsync());
        var args = hasHead ? new List<string> { "restore", "--staged", "--" }
            : new List<string> { "rm", "--cached", "-r", "--" };
        args.AddRange(list);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        return code == 0;
    }

    public async Task<bool> UnstageAllAsync()
    {
        bool hasHead = !string.IsNullOrEmpty(await GetHeadCommitHashAsync());
        var (code, _, _) = hasHead
            ? await RunGitAsync("restore", "--staged", ":/")
            : await RunGitAsync("rm", "--cached", "-r", "--", ":/");
        return code == 0;
    }

    public async Task<bool> DiscardFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return true;
        bool success = true;
        foreach (string path in list)
        {
            var (tracked, _, _) = await RunGitAsync("ls-files", "--error-unmatch", "--", path);
            var (code, _, _) = tracked == 0
                ? await RunGitAsync("restore", "--", path)
                : await RunGitAsync("clean", "-f", "--", path);
            success &= code == 0;
        }
        return success;
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

    public async Task<(bool Success, string Output)> CommitFilesAsync(string message, IEnumerable<string> paths, bool amend = false)
    {
        var selected = paths.Distinct().ToList();
        if (string.IsNullOrWhiteSpace(message)) return (false, "Commit message cannot be empty.");
        if (selected.Count == 0 && !amend) return (false, "Select at least one file.");
        var renameSources = (await GetWorkingTreeChangesAsync())
            .Where(c => c.IsStaged && c.Status == "R" && selected.Contains(c.Path) && c.OriginalPath != null)
            .Select(c => c.OriginalPath!).ToList();
        if (selected.Count > 0 && !await StageFilesAsync(selected)) return (false, "Could not stage the selected files.");
        selected.AddRange(renameSources);
        var args = new List<string> { "commit", "--only", "-m", message };
        if (amend) args.Add("--amend");
        if (selected.Count == 0) args.Add("--allow-empty");
        args.Add("--");
        args.AddRange(selected);
        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return (code == 0, code == 0 ? stdout : stderr);
    }

    public async Task<string> GetCommitPreviewDiffAsync(string filePath)
    {
        if (!string.IsNullOrEmpty(await GetHeadCommitHashAsync()))
        {
            var (code, stdout, _) = await RunGitAsync("diff", "HEAD", "--", filePath);
            if (code == 0 && stdout.Length > 0) return stdout;
        }
        else
        {
            string staged = await GetWorkingTreeFileDiffAsync(filePath, true);
            if (staged.Length > 0) return staged;
        }
        return await GetWorkingTreeFileDiffAsync(filePath, false);
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
            var (tracked, _, _) = await RunGitAsync("ls-files", "--error-unmatch", "--", filePath);
            if (tracked == 0) return code == 0 ? stdout : stderr;

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

    public async Task<(bool Success, string Output, string Error)> FetchWithDetailsAsync(string remote)
    {
        var (code, stdout, stderr) = await RunGitAsync("fetch", "--prune", remote);
        return (code == 0, stdout, stderr);
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

    public async Task<(bool Success, string Output, string Error)> PushWithDetailsAsync(
        string? branch = null,
        string? remote = null,
        bool forceWithLease = false,
        bool pushTags = false,
        bool setUpstream = false)
    {
        var args = new List<string> { "push" };
        if (forceWithLease) args.Add("--force-with-lease");
        if (pushTags) args.Add("--tags");

        string rem = remote ?? await GetDefaultRemoteAsync();
        string br = branch ?? await GetPushTargetBranchAsync(rem);
        if (string.IsNullOrEmpty(await GetHeadCommitHashAsync())) return (false, "", "Create a commit before pushing.");
        var (branchCode, _, _) = await RunGitAsync("check-ref-format", "refs/heads/" + br);
        if (branchCode != 0) return (false, "", "The destination branch name is invalid.");

        // Check if upstream exists
        var (uCode, _, _) = await RunGitAsync("rev-parse", "--abbrev-ref", "@{u}");
        if (uCode != 0 || setUpstream)
        {
            args.Add("-u");
        }

        if (!string.IsNullOrEmpty(rem) && !string.IsNullOrEmpty(br))
        {
            args.Add(rem);
            args.Add($"HEAD:refs/heads/{br}");
        }

        var (code, stdout, stderr) = await RunGitAsync(args.ToArray());
        return (code == 0, stdout, stderr);
    }

    public async Task<bool> PushAsync(string? branch = null)
    {
        var (ok, _, _) = await PushWithDetailsAsync(branch);
        return ok;
    }
}
