using System.Windows;
using System.Windows.Controls;
using GitPort.Dialogs;
using GitPort.Services;

namespace GitPort.Tests;

public class PushOutputTests
{
    [Fact]
    public void Diagnostics_KeepServerErrorsAfterManyTransferUpdates()
    {
        string progress = string.Concat(Enumerable.Range(1, 1000)
            .Select(i => $"Writing objects: {i}/1000, 12 MiB\r"));
        string messages = PushOutput.Diagnostics(progress +
            "remote: Resolving deltas: 100% (100/100)\nremote: Storage quota exceeded; upgrade your account\nerror: failed to push some refs");
        Assert.DoesNotContain("Writing objects", messages);
        Assert.DoesNotContain("Resolving deltas", messages);
        Assert.Contains("Storage quota exceeded", messages);
        Assert.Contains("failed to push", messages);
        Assert.Contains("upgrade", PushOutput.Guidance(messages));
        Assert.Contains("token", PushOutput.Guidance("Authentication failed"));
        Assert.Contains("pull", PushOutput.Guidance("[rejected] main (fetch first)"));
    }

    [Fact]
    public void SmallPushWindow_ReservesMessageSpaceAndKeepsActionsVisible()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new PushDialog(new GitService(System.IO.Path.GetTempPath()), "main");
                var messages = (TextBox)dialog.FindName("TxtPushMessages");
                messages.Text = string.Join("\n", Enumerable.Repeat("remote: Storage quota exceeded. Upgrade your account to continue.", 1000));
                ((TextBlock)dialog.FindName("TxtPushGuidance")).Text = PushOutput.Guidance(messages.Text);
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(664, 411));
                content.Arrange(new Rect(0, 0, 664, 411));
                content.UpdateLayout();
                Assert.True(messages.ActualHeight >= 50);
                Assert.Equal(ScrollBarVisibility.Auto, messages.VerticalScrollBarVisibility);
                var push = (Button)dialog.FindName("BtnPush");
                Point position = push.TranslatePoint(new Point(), content);
                Assert.True(position.X >= 0 && position.X + push.ActualWidth <= 664);
                Assert.True(position.Y >= 0 && position.Y + push.ActualHeight <= 411);
                Assert.True(push.ActualHeight > 0);
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
