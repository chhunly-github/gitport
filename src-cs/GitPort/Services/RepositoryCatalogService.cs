using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using GitPort.Models;

namespace GitPort.Services;

public class RepositoryCatalogService
{
    private readonly string _settingsFilePath;
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);

    public RepositoryCatalogService(string? customPath = null)
    {
        _settingsFilePath = customPath ?? FindSettingsFile();
        Load();
    }

    private static string FindSettingsFile()
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.GetFullPath(Path.Combine(appDir, @"data\profiles\default\settings.properties"));
    }

    public void Load()
    {
        _properties.Clear();
        if (!File.Exists(_settingsFilePath)) return;

        foreach (var line in File.ReadAllLines(_settingsFilePath, Encoding.UTF8))
        {
            string t = line.Trim();
            if (t.StartsWith('#') || string.IsNullOrEmpty(t)) continue;

            int eq = t.IndexOf('=');
            if (eq > 0)
            {
                string key = t.Substring(0, eq).Trim();
                string val = t.Substring(eq + 1).Trim();
                // Unescape Java properties
                val = val.Replace(@"\:", ":").Replace(@"\\", @"\");
                _properties[key] = val;
            }
        }
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sb = new StringBuilder();
            sb.AppendLine("#GitPort - settings");
            sb.AppendLine($"#{DateTime.Now}");

            foreach (var kvp in _properties.OrderBy(k => k.Key))
            {
                // Escape key/value
                string val = kvp.Value.Replace(@"\", @"\\").Replace(":", @"\:");
                sb.AppendLine($"{kvp.Key}={val}");
            }

            File.WriteAllText(_settingsFilePath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Ignore write errors if permissions restricted
        }
    }

    public List<string> GetRepositories()
    {
        if (!_properties.TryGetValue("repos", out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw.Split(new[] { @"\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => p.Trim())
                  .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    public List<string> GetRawRepositories()
    {
        if (!_properties.TryGetValue("repos", out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw.Split(new[] { @"\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(p => p.Trim())
                  .Where(p => !string.IsNullOrEmpty(p))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    public List<string> GetMissingRepositories()
    {
        return GetRawRepositories().Where(p => !Directory.Exists(p)).ToList();
    }

    public List<string> PruneMissingRepositories()
    {
        var raw = GetRawRepositories();
        var missing = raw.Where(p => !Directory.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            var valid = raw.Where(p => Directory.Exists(p)).ToList();
            _properties["repos"] = string.Join(@"\n", valid);
            if (_properties.TryGetValue("lastRepo", out string? last) && missing.Contains(last, StringComparer.OrdinalIgnoreCase))
            {
                _properties["lastRepo"] = valid.Count > 0 ? valid[0] : "";
            }
            Save();
        }
        return missing;
    }

    public string GetLastRepo()
    {
        if (_properties.TryGetValue("lastRepo", out string? last) && !string.IsNullOrWhiteSpace(last))
        {
            return last;
        }
        var repos = GetRepositories();
        return repos.Count > 0 ? repos[0] : "";
    }

    public void SetLastRepo(string path)
    {
        _properties["lastRepo"] = path;
        Save();
    }

    public ObservableCollection<RepoGroupItem> BuildGroups(string activeRepoPath)
    {
        var groups = new ObservableCollection<RepoGroupItem>();
        var repos = GetRepositories();

        // 1. Custom groups from settings
        var groupList = new List<(string Id, string Name)>();
        if (_properties.TryGetValue("groups", out string? rawGroups) && !string.IsNullOrWhiteSpace(rawGroups))
        {
            foreach (var gId in rawGroups.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string id = gId.Trim();
                string name = _properties.TryGetValue($"group.{id}.name", out string? gName) ? gName : id;
                groupList.Add((id, name));
            }
        }

        // Always include ungrouped
        groupList.Add(("ungrouped", "Repositories"));

        foreach (var (gId, gName) in groupList)
        {
            var grp = new RepoGroupItem { Id = gId, Name = gName };

            foreach (var p in repos)
            {
                string pGroup = GetRepoGroup(p);
                if (string.Equals(pGroup, gId, StringComparison.OrdinalIgnoreCase))
                {
                    string displayName = GetRepoName(p);
                    bool isActive = string.Equals(p, activeRepoPath, StringComparison.OrdinalIgnoreCase);

                    grp.Repositories.Add(new RepoItem
                    {
                        Path = p,
                        Name = displayName,
                        GroupId = gId,
                        IsActive = isActive,
                        CurrentBranch = GetFastBranch(p)
                    });
                }
            }

            if (grp.Repositories.Count > 0 || gId == "ungrouped")
            {
                groups.Add(grp);
            }
        }

        return groups;
    }

    public void AddRepository(string path, string? group = null)
    {
        var repos = GetRepositories();
        if (!repos.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            repos.Add(path);
            _properties["repos"] = string.Join(@"\n", repos);
            if (!string.IsNullOrEmpty(group))
            {
                SetRepoGroup(path, group);
            }
            Save();
        }
    }

    public void RemoveRepository(string path)
    {
        var repos = GetRawRepositories();
        repos.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _properties["repos"] = string.Join(@"\n", repos);
        if (_properties.TryGetValue("lastRepo", out string? last) && string.Equals(last, path, StringComparison.OrdinalIgnoreCase))
        {
            var valid = repos.Where(p => Directory.Exists(p)).ToList();
            _properties["lastRepo"] = valid.Count > 0 ? valid[0] : "";
        }
        Save();
    }

    public void CreateGroup(string name)
    {
        string id = Guid.NewGuid().ToString("N");
        string previous = _properties.TryGetValue("groups", out string? g) ? g : "";
        _properties["groups"] = string.IsNullOrEmpty(previous) ? id : $"{previous},{id}";
        _properties[$"group.{id}.name"] = name;
        Save();
    }

    public void RenameRepository(string path, string name)
    {
        string key = Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=');
        _properties[$"repo.name.{key}"] = name;
        Save();
    }

    private string GetRepoGroup(string path)
    {
        string key = Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=');
        return _properties.TryGetValue($"repo.group.{key}", out string? g) ? g : "ungrouped";
    }

    private void SetRepoGroup(string path, string groupId)
    {
        string key = Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=');
        _properties[$"repo.group.{key}"] = groupId;
        Save();
    }

    private string GetRepoName(string path)
    {
        string key = Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=');
        if (_properties.TryGetValue($"repo.name.{key}", out string? custom) && !string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }
        string? name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public static string GetFastBranch(string repoPath)
    {
        try
        {
            string gitPath = Path.Combine(repoPath, ".git");
            if (File.Exists(gitPath))
            {
                string text = File.ReadAllText(gitPath).Trim();
                if (text.StartsWith("gitdir:"))
                {
                    string target = text.Substring(7).Trim();
                    gitPath = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(repoPath, target));
                }
            }
            if (Directory.Exists(gitPath))
            {
                string headFile = Path.Combine(gitPath, "HEAD");
                if (File.Exists(headFile))
                {
                    string headContent = File.ReadAllText(headFile).Trim();
                    if (headContent.StartsWith("ref: refs/heads/"))
                    {
                        return headContent.Substring(16).Trim();
                    }
                    if (headContent.Length >= 7)
                    {
                        return headContent.Substring(0, 7);
                    }
                }
            }
        }
        catch { }
        return "";
    }
}
