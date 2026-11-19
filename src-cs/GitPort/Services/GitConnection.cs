using System.Diagnostics;
using System.Text;

namespace GitPort.Services;

internal static class GitConnectionLinks
{
    public static string CleanUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            url.Any(char.IsControl))
            throw new ArgumentException("Enter an HTTP or HTTPS repository clone URL.");
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri.TrimEnd('/');
    }

    public static string RepositoryPage(string url)
    {
        string clean = CleanUrl(url);
        return clean.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? clean[..^4] : clean;
    }

    public static bool SupportsBrowser(string url)
    {
        var uri = new Uri(CleanUrl(url));
        return uri.Scheme == "https" && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase));
    }

    public static string TokenPage(string url)
    {
        var uri = new Uri(CleanUrl(url));
        string origin = uri.GetLeftPart(UriPartial.Authority);
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return origin + "/settings/tokens";
        if (uri.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase)) return origin + "/-/user_settings/personal_access_tokens";
        // Other servers differ: let the user navigate from the repository page.
        return RepositoryPage(url);
    }
}

public partial class GitService
{
    public async Task SetConnectionUrlAsync(string remote, string url, bool forPush)
    {
        url = GitConnectionLinks.CleanUrl(url);
        var args = new List<string> { "remote", "set-url" };
        if (forPush) args.Add("--push");
        args.AddRange([remote, url]);
        var (code, _, _) = await RunGitAsync(args.ToArray());
        if (code != 0) throw new InvalidOperationException("Could not update the repository's remote URL.");
    }

    public async Task<(string Username, string Password)> BrowserSignInAsync(string url, string username,
        CancellationToken cancellationToken)
    {
        url = GitConnectionLinks.CleanUrl(url);
        if (!GitConnectionLinks.SupportsBrowser(url))
            throw new InvalidOperationException("Use the repository website to sign in, then enter a password or access token here.");
        if (username.Any(char.IsControl)) throw new ArgumentException("The username contains unsupported characters.");
        var uri = new Uri(url);
        // A separate namespace prompts for a fresh account without deleting other apps' saved logins.
        string credentialNamespace = "gitport-browser-" + Guid.NewGuid().ToString("N");
        ProcessStartInfo StartInfo(string command)
        {
            var psi = new ProcessStartInfo(_gitExe)
            {
                WorkingDirectory = RepositoryPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Do not inherit GitPort's password/token headers or disabled helper settings.
            foreach (string key in psi.Environment.Keys.Where(k => k.StartsWith("GIT_CONFIG_", StringComparison.Ordinal)).ToArray())
                psi.Environment.Remove(key);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GCM_INTERACTIVE"] = "true";
            psi.Environment["GCM_GUI_PROMPT"] = "true";
            psi.Environment["GCM_NAMESPACE"] = credentialNamespace;
            psi.Environment["GCM_GITHUB_AUTHMODES"] = "browser";
            psi.Environment["GCM_GITLAB_AUTHMODES"] = "browser";
            psi.ArgumentList.Add("credential-manager");
            psi.ArgumentList.Add(command);
            return psi;
        }
        string input = $"protocol={uri.Scheme}\nhost={uri.Authority}\npath={uri.AbsolutePath.TrimStart('/')}\n" +
            (string.IsNullOrWhiteSpace(username) ? "" : $"username={username}\n") + "\n";
        async Task<(int Code, string Output)> RunAsync(string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = StartInfo(command) };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
                await process.WaitForExitAsync(token);
                await error; // Drain, but never expose helper output which may contain credentials.
                return (process.ExitCode, await output);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                throw;
            }
        }
        try
        {
            var result = await RunAsync("get", cancellationToken);
            if (result.Code != 0)
                throw new InvalidOperationException("Browser sign-in did not complete. Ensure Git Credential Manager is installed with Git for Windows, or paste a password/token instead.");
            return ParseBrowserCredentials(result.Output);
        }
        finally
        {
            // GitPort stores the returned login only when the user chooses Continue/Remember.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await RunAsync("erase", cleanup.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException) { }
        }
    }

    internal static (string Username, string Password) ParseBrowserCredentials(string output)
    {
        var fields = output.Split('\n').Select(line => line.TrimEnd('\r'));
        string username = fields.FirstOrDefault(line => line.StartsWith("username="))?[9..] ?? "";
        string password = fields.FirstOrDefault(line => line.StartsWith("password="))?[9..] ?? "";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) ||
            username.Any(char.IsControl) || password.Any(char.IsControl))
            throw new InvalidOperationException("Browser sign-in returned no usable credentials. Enter a password or token instead.");
        return (username, password);
    }
}
