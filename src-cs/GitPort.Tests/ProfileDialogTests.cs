using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GitPort.Dialogs;
using GitPort.Services;

namespace GitPort.Tests;

public class ProfileDialogTests
{
    [Fact]
    public void ProfileEditor_LoadsIdentityAndRenders_WithIsolatedDrafts()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "GitPortProfileUi_" + Guid.NewGuid().ToString("N"));
                var store = new ProfileService(Path.Combine(root, "profiles.json"));
                var profile = new GitProfile
                {
                    Name = "Office · Gogs", AuthorName = "Example Developer", AuthorEmail = "dev@example.test",
                    Username = "developer", RemoteMatch = "http://gogs.local:3000/team"
                };
                store.Profiles.Add(profile);
                store.Assign(root, profile);
                var dialog = new ProfilesDialog(store, root, "http://gogs.local:3000/team/repository.git");
                Assert.Equal(profile.AuthorName, ((TextBox)dialog.FindName("TxtAuthor")).Text);
                Assert.True(((CheckBox)dialog.FindName("ChkAssign")).IsChecked);
                ((TextBox)dialog.FindName("TxtAuthor")).Text = "Draft only";
                Assert.Equal("Example Developer", profile.AuthorName);
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(740, 560));
                content.Arrange(new Rect(0, 0, 740, 560));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(740, 560, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
                if (directory != null)
                {
                    string reports = Path.Combine(directory.FullName, "reports");
                    Directory.CreateDirectory(reports);
                    using var output = File.Create(Path.Combine(reports, "profile-editor.png"));
                    encoder.Save(output);
                }
                dialog.Close();

                const string originalUrl = "http://gogs.local:3000/team/first.git";
                profile.Credentials[originalUrl] = CredentialProtection.Protect("Authorization: Basic " +
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("developer:example-token")));
                var git = new GitService(root, profiles: store);
                var login = new CredentialsDialog("http://gogs.local:3000/team/new.git", git: git);
                var accounts = (ComboBox)login.FindName("SavedAccounts");
                Assert.Equal(2, accounts.Items.Count);
                accounts.SelectedIndex = 1;
                Assert.Same(profile, login.SelectedProfile);
                Assert.Equal("developer", login.Username);
                Assert.Equal("example-token", login.Password);
                Assert.Equal(Visibility.Collapsed, ((TextBox)login.FindName("TxtPasswordVisible")).Visibility);
                var loginContent = (FrameworkElement)login.Content;
                loginContent.Measure(new Size(544, 541));
                loginContent.Arrange(new Rect(0, 0, 544, 541));
                loginContent.UpdateLayout();
                if (directory != null)
                {
                    var loginBitmap = new RenderTargetBitmap(544, 541, 96, 96, PixelFormats.Pbgra32);
                    loginBitmap.Render(loginContent);
                    var loginEncoder = new PngBitmapEncoder();
                    loginEncoder.Frames.Add(BitmapFrame.Create(loginBitmap));
                    using var output = File.Create(Path.Combine(directory.FullName, "reports", "saved-account-picker.png"));
                    loginEncoder.Save(output);
                }
                accounts.SelectedIndex = 0;
                Assert.Null(login.SelectedProfile);
                Assert.Equal("", login.Password);
                login.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Profile editor did not initialize.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
