using System.Net;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Settings;
using Reelshelf.Uploader.Core.State;
using Reelshelf.Uploader.Core.Uploads;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>The engine end to end against the fake API: full-screen games, a full library, books and notifications.</summary>
public class EngineTests
{
    [Fact]
    public async Task ANewClip_SettlesAndUploads_OntoItsBook()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/new.mp4", 2000);

        await SettleAsync(test, engine);
        Assert.Equal(UploaderStateKind.Watching, engine.State.Kind);
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));

        Assert.Equal(apex.Id, Assert.Single(test.Server.Clips.Values).CategoryId);
        Assert.Equal("Watching for clips", engine.State.Title);
        Assert.Equal(@"Videos\NVIDIA · 1 clip on the shelf today", engine.State.Detail);
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhileAGameIsFullScreen_ANewClipWaits_ThenUploadsOnceTheGameCloses()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.Game.IsGameRunning = true;
        test.WriteFile("Apex Legends/during the game.mp4", 2000);

        await SettleAsync(test, engine);
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));

        Assert.Equal(UploaderStateKind.GameRunning, engine.State.Kind);
        Assert.Equal("T3", engine.State.Id);
        Assert.Equal("Waiting for you to finish playing…", engine.State.Title);
        Assert.Equal("1 clip will upload when you leave the game", engine.State.Detail);
        Assert.Empty(test.Server.Clips);

        test.Game.IsGameRunning = false;
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AClipAlreadyUploading_FinishesItsPart_ThenWaitsForTheGame()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/clip.mp4", 3000);
        await SettleAsync(test, engine);
        List<(int Part, bool Game)> parts = [];
        int waitsDuringGame = 0;
        test.Server.OnPart = number =>
        {
            parts.Add((number, test.Game.IsGameRunning));
            // The game starts while the first part is going up.
            if (number == 1)
            {
                test.Game.IsGameRunning = true;
            }
        };
        test.Clock.OnDelay = _ =>
        {
            if (test.Game.IsGameRunning && ++waitsDuringGame == 5)
            {
                test.Game.IsGameRunning = false;
            }
        };

        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));

        Assert.Equal([(1, false), (2, false), (3, false)], parts);
        Assert.Equal(5, waitsDuringGame);
    }

    [Fact]
    public async Task UploadSlowly_KeepsUploadingDuringAGame_AtFiveMegabitsWithoutALimit()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        test.Server.PartSize = 1_000_000;
        using UploaderEngine engine = await StartAsync(test, settings => settings.FullScreen = FullScreenMode.UploadSlowly);
        test.Game.IsGameRunning = true;
        test.WriteFile("Apex Legends/clip.mp4", 1_250_000);
        await SettleAsync(test, engine);
        test.Clock.Delays.Clear();

        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));

        // 1,250,000 bytes at 5 Mbit/s (625,000 bytes a second): two seconds.
        Assert.InRange(test.Clock.Delays.Sum(delay => delay.TotalSeconds), 1.9, 2.1);
    }

    [Fact]
    public async Task AFullLibrary_PausesUploads_UntilThePlanHasRoom()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/one.mp4", 500);
        test.WriteFile("Apex Legends/two.mp4", 500);
        await SettleAsync(test, engine);
        test.Server.PrepareAnswers.Enqueue((HttpStatusCode.Forbidden, "Plan Limit Reached", "Your plan keeps up to 100 clips"));
        test.Server.ClipCount = 100;

        Assert.Equal(UploadOutcome.PlanFull, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));

        Assert.Equal("T6", engine.State.Id);
        Assert.Equal("Your library is full", engine.State.Title);
        Assert.Equal("Your plan keeps 100 clips · 2 clips waiting", engine.State.Detail);
        Assert.Equal(["N3 100:False"], test.Notifier.Sent.Where(sent => sent.StartsWith("N3")));

        // Checked again every 30 minutes; once there is room, both upload.
        test.Server.ClipCount = 50;
        test.Clock.Advance(TimeSpan.FromMinutes(29));
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal("T6", engine.State.Id);
        test.Clock.Advance(TimeSpan.FromMinutes(2));
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal(2, test.Server.Clips.Count);
    }

    [Fact]
    public async Task AnUnmatchedFolder_AsksOnceForABook_AndItsClipsUploadOnceOneIsChosen()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        Book rivals = test.AddBook("Overwatch 2");
        using UploaderEngine engine = await StartAsync(test);
        List<string> asked = [];
        engine.BookNeeded += (_, folder) => asked.Add(folder);
        test.WriteFile("Marvel Rivals 2/one.mp4", 500);
        await SettleAsync(test, engine);
        test.WriteFile("Marvel Rivals 2/two.mp4", 500);
        await SettleAsync(test, engine);

        Assert.Equal(["Marvel Rivals 2"], asked);
        Assert.Equal(["N2 Marvel Rivals 2:1", "N2 Marvel Rivals 2:2"], test.Notifier.Sent);
        Assert.Equal("T5", engine.State.Id);
        Assert.Equal("Marvel Rivals 2 needs a book", engine.State.Title);
        Assert.Equal("2 clips are waiting", engine.State.Detail);
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));

        engine.ChooseBookForFolder("Marvel Rivals 2", rivals.Id);
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
        Assert.All(test.Server.Clips.Values, clip => Assert.Equal(rivals.Id, clip.CategoryId));
        Assert.Equal(rivals.Id, test.SettingsStore.Load().SavedBookFor("marvel rivals 2"));
    }

    [Fact]
    public async Task ShelvedClips_AreNotifiedTogether_AtMostEveryTenMinutes_AndNotDuringAGame()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        test.AddBook("Valorant");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/a.mp4", 300);
        test.WriteFile("Apex Legends/b.mp4", 300);
        test.WriteFile("VALORANT/c.mp4", 300);
        await SettleAsync(test, engine);
        while (await engine.UploadNextAsync(CancellationToken.None) is not null)
        {
        }

        await engine.TickAsync(CancellationToken.None);
        Assert.Equal(["N1 Apex Legends:2, Valorant:1"], test.Notifier.Sent);

        test.WriteFile("Apex Legends/d.mp4", 300);
        await SettleAsync(test, engine);
        await engine.UploadNextAsync(CancellationToken.None);
        test.Game.IsGameRunning = true;
        test.Clock.Advance(TimeSpan.FromMinutes(11));
        await engine.TickAsync(CancellationToken.None);
        Assert.Single(test.Notifier.Sent);

        test.Game.IsGameRunning = false;
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal("N1 Apex Legends:1", test.Notifier.Sent[^1]);
    }

    [Fact]
    public async Task ARevokedToken_SignsTheAppOut_AndAnOldVersion_AsksForAnUpdate()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        bool signedOut = false;
        engine.SignedOut += (_, _) => signedOut = true;
        test.WriteFile("Apex Legends/a.mp4", 300);
        await SettleAsync(test, engine);
        test.Server.PrepareAnswers.Enqueue((HttpStatusCode.Unauthorized, null, null));

        Assert.Equal(UploadOutcome.SignedOut, await engine.UploadNextAsync(CancellationToken.None));
        Assert.True(signedOut);
        Assert.False(engine.IsSignedIn);
        Assert.Null(test.Credentials.Token);
        Assert.Contains("N5", test.Notifier.Sent);
        Assert.Equal(LedgerState.Waiting, test.Ledger.Recent().Single().State);

        test.Credentials.WriteToken("rsd_again");
        engine.OnSignedIn();
        test.Server.PrepareAnswers.Enqueue(((HttpStatusCode)426, null, null));
        Assert.Equal(UploadOutcome.UpdateRequired, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal("T8", engine.State.Id);
        Assert.Equal("Update Reelshelf", engine.State.Title);
        Assert.Equal(["N4"], test.Notifier.Sent.Where(sent => sent == "N4"));
    }

    [Fact]
    public async Task AnUnsupportedUploadProtocol_AsksForAnUpdate()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/a.mp4", 300);
        await SettleAsync(test, engine);
        test.Server.Protocol = "tus";

        Assert.Equal(UploadOutcome.Unsupported, await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal("T8", engine.State.Id);
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheApiCantBeReached_TheTraySaysWhenItTriesAgain()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/a.mp4", 300);
        await SettleAsync(test, engine);
        test.Server.ApiDown = true;

        Assert.Equal(UploadOutcome.RetryLater, await engine.UploadNextAsync(CancellationToken.None));

        Assert.Equal("T7", engine.State.Id);
        Assert.Equal("Couldn't reach Reelshelf", engine.State.Title);
        Assert.Equal("Trying again in 15 seconds · 1 clip waiting", engine.State.Detail);
    }

    [Fact]
    public async Task Pausing_HoldsUploads_UntilResumed()
    {
        using TestUploader test = new();
        test.AddBook("Apex Legends");
        using UploaderEngine engine = await StartAsync(test);
        test.WriteFile("Apex Legends/a.mp4", 300);
        await SettleAsync(test, engine);

        engine.Pause(PauseLength.UntilResumed);
        Assert.Null(await engine.UploadNextAsync(CancellationToken.None));
        Assert.Equal("T4", engine.State.Id);
        Assert.Equal("Paused", engine.State.Title);

        engine.Pause(PauseLength.OneHour);
        test.Clock.Advance(TimeSpan.FromMinutes(61));
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcome.Shelved, await engine.UploadNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ChoosingAFolder_BaselinesWhatIsThere_AndTheFolderMatchesItsBooks()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        Book valorant = test.AddBook("Valorant");
        test.WriteFile("Apex Legends/old.mp4", 100);
        test.WriteFile("VALORANT/old.mp4", 100);
        test.WriteFile("Marvel Rivals/old.mp4", 100);
        using UploaderEngine engine = test.Engine();
        await engine.RefreshBooksAsync(CancellationToken.None);

        engine.SetRecordingsFolder(test.Recordings);
        await SettleAsync(test, engine);

        Assert.Empty(test.Ledger.Recent());
        Assert.Equal(
            [("Apex Legends", apex.Name), ("Marvel Rivals", (string?)null), ("VALORANT", valorant.Name)],
            engine.FolderMatches().Select(match => (match.Folder, match.Book?.Name)));
    }

    private static async Task<UploaderEngine> StartAsync(TestUploader test, Action<UploaderSettings>? configure = null)
    {
        UploaderEngine engine = test.Engine(configure);
        engine.SetRecordingsFolder(test.Recordings);
        await engine.TickAsync(CancellationToken.None);
        return engine;
    }

    /// <summary>Lets new files settle: a rescan, then checks over more than five seconds.</summary>
    private static async Task SettleAsync(TestUploader test, UploaderEngine engine)
    {
        test.Clock.Advance(UploaderEngine.RescanEvery);
        await engine.TickAsync(CancellationToken.None);
        for (int i = 0; i < 4; i++)
        {
            test.Clock.Advance(TimeSpan.FromSeconds(2));
            await engine.TickAsync(CancellationToken.None);
        }
    }
}
