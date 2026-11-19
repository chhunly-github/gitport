using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitPort.Dialogs;
using GitPort.Services;

namespace GitPort.Tests;

public class ConnectionRecoveryTests
{
    [Fact]
    public async Task BrowserHelper_ReturnsCredentialsCleansUpAndCanBeCancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "GitPortBrowserTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Exercise the process boundary without opening a browser or contacting a real account.
            string source = Path.Combine(root, "Helper.cs");
            string helper = Path.Combine(root, "Helper.exe");
            await File.WriteAllTextAsync(source, """
                using System;
                using System.IO;
                using System.Threading;
                class Helper
                {
                    static int Main(string[] args)
                    {
                        string root = AppDomain.CurrentDomain.BaseDirectory;
                        string input = Console.In.ReadToEnd();
                        if (args.Length != 2 || args[0] != "credential-manager") return 3;
                        if (Environment.GetEnvironmentVariable("GCM_GITHUB_AUTHMODES") != "browser" ||
                            Environment.GetEnvironmentVariable("GCM_INTERACTIVE") != "true") return 4;
                        File.AppendAllText(Path.Combine(root, "calls.txt"), args[1] + "\n");
                        if (args[1] == "erase") return 0;
                        if (File.Exists(Path.Combine(root, "wait"))) Thread.Sleep(60000);
                        if (File.Exists(Path.Combine(root, "fail"))) return 1;
                        Console.Write("username=test-user\npassword=test-browser-token==\n\n");
                        return 0;
                    }
                }
                """);
            var compile = new System.Diagnostics.ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string arg in new[] { "/nologo", "/target:exe", "/out:" + helper, source }) compile.ArgumentList.Add(arg);
            using (var compiler = System.Diagnostics.Process.Start(compile)!)
            {
                string output = await compiler.StandardOutput.ReadToEndAsync();
                await compiler.WaitForExitAsync();
                Assert.True(compiler.ExitCode == 0, output);
            }
            var git = new GitService(root, gitExe: helper);
            var result = await git.BrowserSignInAsync("https://github.com/team/repo.git", "", CancellationToken.None);
            Assert.Equal(("test-user", "test-browser-token=="), result);
            Assert.Equal(new[] { "get", "erase" }, await File.ReadAllLinesAsync(Path.Combine(root, "calls.txt")));
            await File.WriteAllTextAsync(Path.Combine(root, "fail"), "");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                git.BrowserSignInAsync("https://github.com/team/repo.git", "", CancellationToken.None));
            Assert.DoesNotContain("test-browser-token", failure.Message);
            await File.WriteAllTextAsync(Path.Combine(root, "wait"), "");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                git.BrowserSignInAsync("https://github.com/team/repo.git", "", cancel.Token));
            Assert.Equal("erase", (await File.ReadAllLinesAsync(Path.Combine(root, "calls.txt"))).Last());
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "GitPortBrowserTest_"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected helper test directory.");
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CorrectingPushUrl_DoesNotChangeFetchOrOtherRemotes()
    {
        string root = Path.Combine(Path.GetTempPath(), "GitPortRemoteTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var git = new GitService(root);
            Assert.Equal(0, (await git.RunGitAsync("init")).ExitCode);
            await git.RunGitAsync("remote", "add", "origin", "https://example.test/team/fetch.git");
            await git.RunGitAsync("remote", "add", "other", "https://example.test/team/other.git");
            await git.SetConnectionUrlAsync("origin", "https://example.test/team/corrected.git", forPush: true);
            Assert.Equal("https://example.test/team/corrected.git", await git.GetRemoteUrlAsync("origin", forPush: true));
            Assert.Equal("https://example.test/team/fetch.git", await git.GetRemoteUrlAsync("origin"));
            Assert.Equal("https://example.test/team/other.git", await git.GetRemoteUrlAsync("other"));
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "GitPortRemoteTest_"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected remote test directory.");
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("fatal: repository 'https://example.test/team/missing.git/' not found")]
    [InlineData("remote: Invalid username or token.")]
    [InlineData("password authentication is not supported")]
    [InlineData("The requested URL returned error: 404")]
    [InlineData("token expired")]
    public void AccessErrors_OpenAccountRecovery(string error) => Assert.True(GitService.IsAuthFailure(error));

    [Fact]
    public void Links_RemoveCredentialsAndUseCorrectServer()
    {
        Assert.Equal("https://github.com/team/repo.git", GitConnectionLinks.CleanUrl("https://user:secret@github.com/team/repo.git?token=secret#fragment"));
        Assert.Equal("http://gogs.local:3000/team/repo", GitConnectionLinks.RepositoryPage("http://gogs.local:3000/team/repo.git"));
        Assert.Equal("https://github.com/settings/tokens", GitConnectionLinks.TokenPage("https://github.com/team/repo.git"));
        Assert.True(GitConnectionLinks.SupportsBrowser("https://github.com/team/repo.git"));
        Assert.False(GitConnectionLinks.SupportsBrowser("https://github.com.example.test/team/repo.git"));
        Assert.False(GitConnectionLinks.SupportsBrowser("http://gogs.local:3000/team/repo.git"));
        Assert.Throws<ArgumentException>(() => GitConnectionLinks.CleanUrl("file:///C:/private"));
        Assert.Throws<ArgumentException>(() => GitConnectionLinks.CleanUrl("https://example.test/repo\npassword=x"));
        Assert.Equal(("developer", "token=a=b"), GitService.ParseBrowserCredentials("username=developer\r\npassword=token=a=b\r\n"));
        Assert.Throws<InvalidOperationException>(() => GitService.ParseBrowserCredentials("username=developer\n"));
    }

    [Fact]
    public async Task CorrectingPushUrl_PreservesFetchAndOtherRemotes()
    {
        string root = Path.Combine(Path.GetTempPath(), "GitPortRecovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var git = new GitService(root);
            Assert.Equal(0, (await git.RunGitAsync("init")).ExitCode);
            await git.RunGitAsync("remote", "add", "origin", "https://example.test/team/read.git");
            await git.RunGitAsync("remote", "add", "backup", "https://backup.test/team/repo.git");
            await git.SetConnectionUrlAsync("origin", "https://example.test/team/write.git", forPush: true);
            Assert.Equal("https://example.test/team/read.git", await git.GetRemoteUrlAsync("origin"));
            Assert.Equal("https://example.test/team/write.git", await git.GetRemoteUrlAsync("origin", forPush: true));
            Assert.Equal("https://backup.test/team/repo.git", await git.GetRemoteUrlAsync("backup"));
            await git.SetConnectionUrlAsync("origin", "https://example.test/team/corrected.git", forPush: false);
            Assert.Equal("https://example.test/team/corrected.git", await git.GetRemoteUrlAsync("origin"));
            Assert.Equal("https://example.test/team/write.git", await git.GetRemoteUrlAsync("origin", forPush: true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RecoveryDialog_ScrollsLongErrorsAndClearsSecretWhenUrlChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new CredentialsDialog("https://github.com/team/repo.git", "developer",
                    customErrorMessage: string.Join("\n", Enumerable.Repeat("remote: Repository not found. Check your account access.", 500)),
                    git: new GitService(Path.GetTempPath()));
                Assert.True(((Button)dialog.FindName("BtnBrowserLogin")).IsEnabled);
                var secret = (PasswordBox)dialog.FindName("TxtPassword");
                secret.Password = "old-secret";
                ((TextBox)dialog.FindName("TxtRemoteUrl")).Text = "http://gogs.local:3000/team/repo.git";
                Assert.Empty(secret.Password);
                Assert.False(((Button)dialog.FindName("BtnBrowserLogin")).IsEnabled);
                Assert.Equal(ScrollBarVisibility.Auto, ((TextBox)dialog.FindName("TxtConnectionError")).VerticalScrollBarVisibility);
                var content = (FrameworkElement)dialog.Content;
                foreach (var size in new[] { new Size(634, 701), new Size(544, 411) })
                {
                    content.Measure(size);
                    content.Arrange(new Rect(new Point(), size));
                    content.UpdateLayout();
                    var button = (Button)dialog.FindName("BtnLogin");
                    var position = button.TranslatePoint(new Point(), content);
                    Assert.True(position.Y + button.ActualHeight <= size.Height);
                    Assert.True(position.X + button.ActualWidth <= size.Width);
                    if (size.Height > 500)
                    {
                        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        var directory = new DirectoryInfo(AppContext.BaseDirectory);
                        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
                        if (directory != null)
                        {
                            string reports = Path.Combine(directory.FullName, "reports");
                            Directory.CreateDirectory(reports);
                            using var file = File.Create(Path.Combine(reports, "connection-recovery.png"));
                            encoder.Save(file);
                        }
                    }
                }
                dialog.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
