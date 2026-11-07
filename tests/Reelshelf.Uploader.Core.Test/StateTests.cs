using Reelshelf.Uploader.Core.Copy;
using Reelshelf.Uploader.Core.State;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>The tray's state: the priority between T1–T8 and the words of each, as screens.html has them.</summary>
public class StateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 21, 47, 0, TimeSpan.Zero);

    private static readonly StateInputs Everything = new()
    {
        UpdateRequired = true,
        LibraryFull = new LibraryFullInfo(100, false, null),
        UnreachableRetryAt = Now.AddMinutes(2),
        NeedsBook = ("Marvel Rivals", 2),
        Paused = new PauseInfo(PauseLength.OneHour, Now.AddHours(1)),
        GameHoldsUploads = true,
        Waiting = 1,
        Current = ("Apex Legends 2026.11.06 - 21.52", 64),
        FolderName = @"Videos\NVIDIA",
        ShelvedToday = 3
    };

    [Fact]
    public void ThePriority_IsT8_T6_T7_T5_T4_T3_T2_T1()
    {
        StateInputs inputs = Everything;
        List<string> order = [];
        foreach (Func<StateInputs, StateInputs> clear in new Func<StateInputs, StateInputs>[]
                 {
                     state => state with { UpdateRequired = false },
                     state => state with { LibraryFull = null },
                     state => state with { UnreachableRetryAt = null },
                     state => state with { NeedsBook = null },
                     state => state with { Paused = null },
                     state => state with { GameHoldsUploads = false },
                     state => state with { Current = null },
                     state => state
                 })
        {
            order.Add(UploaderState.From(inputs, Now, TimeZoneInfo.Utc).Id);
            inputs = clear(inputs);
        }

        Assert.Equal(["T8", "T6", "T7", "T5", "T4", "T3", "T2", "T1"], order);
    }

    [Fact]
    public void EachState_HasTheWordsFromTheScreens()
    {
        StateInputs inputs = Everything;

        AssertState(inputs, "Update Reelshelf", "This version doesn't work with Reelshelf any more",
            "Open the Store", TrayBadge.Red);
        inputs = inputs with { UpdateRequired = false };
        AssertState(inputs, "Your library is full", "Your plan keeps 100 clips · 1 clip waiting", "See plans",
            TrayBadge.Amber);
        inputs = inputs with { LibraryFull = null };
        AssertState(inputs, "Couldn't reach Reelshelf", "Trying again in 2 minutes · 1 clip waiting", "Try now",
            TrayBadge.Red);
        inputs = inputs with { UnreachableRetryAt = null };
        AssertState(inputs, "Marvel Rivals needs a book", "2 clips are waiting", "Choose a book…", TrayBadge.Amber);
        inputs = inputs with { NeedsBook = null };
        AssertState(inputs, "Paused until 22:47", "New clips will upload when you resume", "Resume", TrayBadge.Amber);
        inputs = inputs with { Paused = null };
        AssertState(inputs with { Waiting = 2 }, "Waiting for you to finish playing…",
            "2 clips will upload when you leave the game", "Upload now", TrayBadge.Green);
        inputs = inputs with { GameHoldsUploads = false };
        AssertState(inputs with { Waiting = 2 }, "Uploading 2 clips…", "Apex Legends 2026.11.06 - 21.52 · 64%",
            "Show recent uploads", TrayBadge.Green);
        inputs = inputs with { Current = null };
        AssertState(inputs, "Watching for clips", @"Videos\NVIDIA · 3 clips on the shelf today", "Show recent uploads",
            TrayBadge.None);
    }

    [Fact]
    public void AGameWithNothingWaiting_LeavesTheTrayWatching()
    {
        StateInputs inputs = new() { GameHoldsUploads = true, Waiting = 0, FolderName = @"Videos\NVIDIA" };

        Assert.Equal(UploaderStateKind.Watching, UploaderState.From(inputs, Now).Kind);
    }

    [Fact]
    public void TheOtherPausesAndLimits_HaveTheirOwnWords()
    {
        Assert.Equal("Paused until tomorrow", UploaderState.From(new StateInputs
        {
            Paused = new PauseInfo(PauseLength.UntilTomorrow, Now.AddHours(8))
        }, Now).Title);
        Assert.Equal("Paused", UploaderState.From(new StateInputs
        {
            Paused = new PauseInfo(PauseLength.UntilResumed, null)
        }, Now).Title);
        Assert.Equal("Your recordings folder needs a book", UploaderState.From(new StateInputs
        {
            NeedsBook = ("", 1)
        }, Now).Title);
        UploaderState monthly = UploaderState.From(new StateInputs
        {
            LibraryFull = new LibraryFullInfo(1000, true, new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)),
            Waiting = 1
        }, Now, TimeZoneInfo.Utc);
        Assert.Equal("You've added a lot of clips this month", monthly.Title);
        Assert.Equal("New clips upload from 20 Nov 2026 · 1 clip waiting", monthly.Detail);
    }

    [Fact]
    public void TheTooltip_RepeatsTheFirstLine()
    {
        UploaderState state = UploaderState.From(Everything, Now);

        Assert.Equal(state.Title, state.Tooltip);
    }

    [Theory]
    [InlineData(@"C:\Users\you\Videos\NVIDIA", @"Videos\NVIDIA")]
    [InlineData(@"D:\Clips\", @"D:\Clips")]
    [InlineData("/home/you/Videos/OBS", @"Videos\OBS")]
    public void TheFolder_IsNamedByItsLastTwoFolders(string path, string name)
    {
        Assert.Equal(name, UploaderState.ShortFolderName(path));
    }

    [Fact]
    public void Copy_CountsAndFormats_AsTheCopyGuideSays()
    {
        Assert.Equal("1 clip", UploaderCopy.Clips(1));
        Assert.Equal("1,000 clips", UploaderCopy.Clips(1000));
        Assert.Equal("0:41", UploaderCopy.Length(41.2));
        Assert.Equal("2:14", UploaderCopy.Length(134));
        Assert.Equal("74 MB", UploaderCopy.Size(77_600_000));
        Assert.Equal("Apex Legends · 0:41 · 74 MB", UploaderCopy.Recent.Meta("Apex Legends", 41, 77_600_000, null));
        Assert.Equal("3 clips are on the shelf", UploaderCopy.Notifications.ShelvedTitle(3));
        Assert.Equal("Apex Legends (2), Valorant",
            UploaderCopy.Notifications.ShelvedBooks([("Apex Legends", 2), ("Valorant", 1)]));
        Assert.Equal("Which book do Marvel Rivals clips go on?",
            UploaderCopy.Notifications.NeedsBookTitle("Marvel Rivals"));
        Assert.Equal(@"2 clips are waiting in Marvel Rivals\",
            UploaderCopy.Notifications.NeedsBookDetail(2, UploaderCopy.Folder("Marvel Rivals")));
        Assert.Equal("Quit and stop 2 uploads? They'll carry on next time.", UploaderCopy.Tray.QuitWhileUploading(2));
    }

    private static void AssertState(StateInputs inputs, string title, string detail, string action, TrayBadge badge)
    {
        UploaderState state = UploaderState.From(inputs, Now, TimeZoneInfo.Utc);
        Assert.Equal(title, state.Title);
        Assert.Equal(detail, state.Detail);
        Assert.Equal(action, state.ActionLabel);
        Assert.Equal(badge, state.Badge);
    }
}
