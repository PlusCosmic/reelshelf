using Reelshelf.Uploader.Windows;
using Xunit;

namespace Reelshelf.Uploader.Test;

/// <summary>The Windows parts behind Core's interfaces. Windows only.</summary>
public class WindowsPlatformTests
{
    [Fact]
    public void TheToken_RoundTripsThroughCredentialManager()
    {
        // A credential of its own, so a real sign-in on the machine is never touched.
        WindowsCredentialStore store = new($"Reelshelf test {Guid.NewGuid():N}");
        try
        {
            Assert.Null(store.ReadToken());

            store.WriteToken("rsd_first");
            store.WriteToken("rsd_second-token_with-every-character");

            Assert.Equal("rsd_second-token_with-every-character", store.ReadToken());
        }
        finally
        {
            store.DeleteToken();
        }

        Assert.Null(store.ReadToken());
        // Deleting what isn't there is fine.
        store.DeleteToken();
    }

    [Fact]
    public void TheFullScreenCheck_AnswersWithoutAGame()
    {
        // CI runs no full-screen game, so the shell reports a desktop that accepts notifications (or none at all).
        Assert.False(WindowsGameActivity.Query());
        using WindowsGameActivity activity = new();
        Assert.False(activity.IsGameRunning);
    }

    [Fact]
    public async Task WithoutAPackage_StartingWithWindowsIsUnavailable()
    {
        // Test runs aren't packaged, so there is no startup task to turn on.
        Assert.Equal(StartupTaskService.StartupState.Unavailable, await StartupTaskService.GetAsync());
    }

    [Theory]
    [InlineData("action=ChooseBook&folder=Marvel%20Rivals", ToastAction.ChooseBook, "Marvel Rivals")]
    [InlineData("action=SeePlans", ToastAction.SeePlans, null)]
    public void AToastsArguments_SayWhatToDo(string arguments, ToastAction action, string? folder)
    {
        Assert.Equal((action, folder), ToastNotifier.Parse(arguments));
    }

    [Fact]
    public void UnknownToastArguments_DoNothing()
    {
        Assert.Null(ToastNotifier.Parse("dismiss"));
        Assert.Null(ToastNotifier.Parse("action=Explode"));
    }
}
