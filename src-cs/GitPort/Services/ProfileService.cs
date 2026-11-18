using System.IO;
using System.Text.Json;

namespace GitPort.Services;

public sealed class GitProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string AuthorName { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string Username { get; set; } = "";
    public string SshKeyPath { get; set; } = "";
    public string RemoteMatch { get; set; } = "";
    public Dictionary<string, string> Credentials { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class ProfileService
{
    private sealed class Settings
    {
        public List<GitProfile> Profiles { get; set; } = new();
        public Dictionary<string, string> Repositories { get; set; } = new();
    }

    private readonly string _path;
    private Settings _settings;
    public List<GitProfile> Profiles => _settings.Profiles;

    public ProfileService(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "data", "profiles.json");
        // Do not silently replace unreadable settings with an empty profile list.
        _settings = File.Exists(_path)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings()
            : new Settings();
        _settings.Repositories = new(_settings.Repositories, StringComparer.OrdinalIgnoreCase);
    }

    public GitProfile? Resolve(string repository, string remoteUrl)
    {
        if (_settings.Repositories.TryGetValue(NormalizePath(repository), out var id))
            return Profiles.FirstOrDefault(p => p.Id == id);
        return Profiles.Where(p => MatchesRemote(p.RemoteMatch, remoteUrl))
            .OrderByDescending(p => p.RemoteMatch.Trim().Length).FirstOrDefault();
    }

    public bool IsAssigned(string repository) => _settings.Repositories.ContainsKey(NormalizePath(repository));

    public void Assign(string repository, GitProfile? profile)
    {
        string key = NormalizePath(repository);
        if (profile == null) _settings.Repositories.Remove(key);
        else _settings.Repositories[key] = profile.Id;
    }

    public void Remove(GitProfile profile)
    {
        Profiles.Remove(profile);
        foreach (var key in _settings.Repositories.Where(p => p.Value == profile.Id).Select(p => p.Key).ToList())
            _settings.Repositories.Remove(key);
    }

    public void Save()
    {
        WriteSettings(_settings);
    }

    public void Apply(List<GitProfile> profiles, string? repository, GitProfile? assigned)
    {
        var updated = new Settings
        {
            Profiles = profiles,
            Repositories = _settings.Repositories.Where(p => profiles.Any(profile => profile.Id == p.Value))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)
        };
        if (repository != null)
        {
            string key = NormalizePath(repository);
            if (assigned == null) updated.Repositories.Remove(key);
            else updated.Repositories[key] = assigned.Id;
        }
        WriteSettings(updated);
        _settings = updated;
    }

    private void WriteSettings(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool MatchesRemote(string match, string url)
    {
        match = match.Trim().TrimEnd('/');
        if (match.Length == 0 || !TryRemoteUri(url, out var remote)) return false;
        if (!match.Contains("://")) return string.Equals(match, remote.Authority, StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(match, UriKind.Absolute, out var rule)) return false;
        string path = remote.AbsolutePath.TrimEnd('/');
        string prefix = rule.AbsolutePath.TrimEnd('/');
        return rule.Scheme == remote.Scheme && rule.Authority.Equals(remote.Authority, StringComparison.OrdinalIgnoreCase)
            && (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    private static bool TryRemoteUri(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) && !string.IsNullOrEmpty(uri.Host)) return true;
        // Git's scp-style SSH form: git@host:owner/repo.git.
        int colon = url.IndexOf(':');
        return colon > 0 && Uri.TryCreate("ssh://" + url[..colon] + "/" + url[(colon + 1)..], UriKind.Absolute, out uri!);
    }
}
