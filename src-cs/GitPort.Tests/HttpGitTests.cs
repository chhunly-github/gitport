using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GitPort.Services;

namespace GitPort.Tests;

public sealed class HttpGitTests
{
    [Fact]
    public async Task PlainHttp_FetchPushPull_WithSessionAndEncryptedProfileLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), "GitPortHttp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var git = new GitService(root);
            Assert.Equal(0, (await git.RunGitAsync("init", "--bare", "server.git")).ExitCode);
            var bare = new GitService(Path.Combine(root, "server.git"));
            await bare.RunGitAsync("config", "http.receivepack", "true");
            await using var server = new SmartHttpServer(root);
            string url = $"http://127.0.0.1:{server.Port}/server.git";
            string local = Path.Combine(root, "local");
            Directory.CreateDirectory(local);
            var profiles = new ProfileService(Path.Combine(root, "profiles.json"));
            git = new GitService(local, profiles: profiles);
            await git.RunGitAsync("init", "-b", "main");
            await git.RunGitAsync("config", "user.name", "HTTP Test");
            await git.RunGitAsync("config", "user.email", "http@example.test");
            await git.RunGitAsync("config", "commit.gpgSign", "false");
            await git.RunGitAsync("remote", "add", "gogs", url);
            var denied = await git.FetchWithDetailsAsync("gogs");
            Assert.False(denied.Success);
            Assert.True(GitService.IsAuthFailure(denied.Error), denied.Error);
            await git.SetCredentialsAsync(url, "test-user", "test-password", false);
            var fetch = await git.FetchWithDetailsAsync("gogs");
            Assert.True(fetch.Success, fetch.Error);
            await File.WriteAllTextAsync(Path.Combine(local, "hello.txt"), "hello HTTP\n");
            Assert.True(await git.StageAllAsync());
            Assert.True((await git.CommitAsync("HTTP initial")).Success);
            var push = await git.PushWithDetailsAsync("main", "gogs");
            Assert.True(push.Success, push.Error);
            var pull = await git.PullWithDetailsAsync();
            Assert.True(pull.Success, pull.Error);
            Assert.False(File.Exists(Path.Combine(root, "profiles.json")));
            await git.SetCredentialsAsync(url, "test-user", "test-password", true);
            var restarted = new GitService(local, profiles: new ProfileService(Path.Combine(root, "profiles.json")));
            await restarted.ResolveProfileAsync();
            var afterRestart = await restarted.FetchWithDetailsAsync("gogs");
            Assert.True(afterRestart.Success, afterRestart.Error);
            Assert.Equal(0, (await restarted.RunGitAsync("config", "--local", "--get-regexp", "http.*sslVerify")).StdOut.Length);
            Assert.DoesNotContain("test-password", await File.ReadAllTextAsync(Path.Combine(local, ".git", "config")));
            // A different repository path must not receive this profile's Authorization header.
            var unrelated = await restarted.RunGitAsync("ls-remote", $"http://127.0.0.1:{server.Port}/other.git");
            Assert.NotEqual(0, unrelated.ExitCode);
            Assert.False(server.LeakedAuthorization);
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "GitPortHttp_"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected fixture directory.");
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    // A loopback Git smart HTTP server using Git's own CGI backend. No Gogs installation or real account required.
    private sealed class SmartHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(45));
        private readonly Task _loop;
        private readonly string _root;
        public int Port { get; }
        public bool LeakedAuthorization { get; private set; }

        public SmartHttpServer(string root)
        {
            _root = root;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Serve();
        }

        private async Task Serve()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await Handle(client.GetStream());
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        private async Task Handle(NetworkStream stream)
        {
            var buffer = new List<byte>();
            var one = new byte[1];
            while (buffer.Count < 65536 && await stream.ReadAsync(one, _stop.Token) > 0)
            {
                buffer.Add(one[0]);
                if (buffer.Count >= 4 && buffer.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
            }
            var lines = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n");
            var request = lines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
            }
            string auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-user:test-password"));
            bool authorized = headers.GetValueOrDefault("Authorization") == auth;
            if (request[1].StartsWith("/other.git") && headers.ContainsKey("Authorization")) LeakedAuthorization = true;
            if (!authorized)
            {
                await Reply(stream, "401 Unauthorized", "WWW-Authenticate: Basic realm=\"GitPort Test\"\r\n", []);
                return;
            }
            int length = int.TryParse(headers.GetValueOrDefault("Content-Length"), out var size) ? size : 0;
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, _stop.Token);
            var uri = new Uri("http://localhost" + request[1]);
            var psi = new ProcessStartInfo(GitService.FindGitExecutable())
            {
                WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            psi.ArgumentList.Add("http-backend");
            psi.Environment["GIT_PROJECT_ROOT"] = _root;
            psi.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
            psi.Environment["REMOTE_USER"] = "test-user";
            psi.Environment["REQUEST_METHOD"] = request[0];
            psi.Environment["PATH_INFO"] = uri.AbsolutePath;
            psi.Environment["QUERY_STRING"] = uri.Query.TrimStart('?');
            psi.Environment["CONTENT_TYPE"] = headers.GetValueOrDefault("Content-Type", "");
            psi.Environment["CONTENT_LENGTH"] = length.ToString();
            psi.Environment["HTTP_GIT_PROTOCOL"] = headers.GetValueOrDefault("Git-Protocol", "");
            using var backend = Process.Start(psi)!;
            using var output = new MemoryStream();
            var reading = backend.StandardOutput.BaseStream.CopyToAsync(output, _stop.Token);
            var errors = backend.StandardError.ReadToEndAsync(_stop.Token);
            await backend.StandardInput.BaseStream.WriteAsync(body, _stop.Token);
            backend.StandardInput.Close();
            await backend.WaitForExitAsync(_stop.Token);
            await reading;
            Assert.True(backend.ExitCode == 0, await errors);
            byte[] response = output.ToArray();
            int split = -1;
            for (int i = 0; i + 3 < response.Length; i++)
                if (response[i] == 13 && response[i + 1] == 10 && response[i + 2] == 13 && response[i + 3] == 10) { split = i; break; }
            Assert.True(split >= 0, "CGI response has no header separator.");
            string responseHeaders = Encoding.ASCII.GetString(response, 0, split);
            await Reply(stream, "200 OK", responseHeaders + "\r\n", response[(split + 4)..]);
        }

        private async Task Reply(NetworkStream stream, string status, string headers, byte[] body)
        {
            byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{headers}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, _stop.Token);
            await stream.WriteAsync(body, _stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _stop.Dispose();
        }
    }
}
