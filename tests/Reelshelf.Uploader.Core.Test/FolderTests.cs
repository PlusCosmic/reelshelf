using Reelshelf.Uploader.Core.Files;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Settings;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>Watching the recordings folder: the baseline, settling, rescans, what counts as a clip, and ledger skips.</summary>
public class FolderTests
{
    [Fact]
    public void FilesAlreadyInTheFolder_AreBaselined_AndNeverUploaded()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        test.WriteFile("Apex Legends/old.mp4", 100);
        test.WriteFile("old root clip.MKV", 100);
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => true);

        int baselined = monitor.Use(test.Recordings, baseline: true);
        monitor.Rescan();
        test.Clock.Advance(TimeSpan.FromSeconds(10));
        List<LedgerEntry> added = monitor.Settle([apex], new UploaderSettings());

        Assert.Equal(2, baselined);
        Assert.Empty(added);
        Assert.Equal(0, monitor.Settling);
        Assert.Equal(2, test.Ledger.Count(monitor.Root!, LedgerState.Baseline));
        Assert.Empty(test.Ledger.Recent());
    }

    [Fact]
    public void AFileStillBeingWritten_SettlesFiveSecondsAfterItStopsGrowing()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => true);
        monitor.Use(test.Recordings, baseline: true);

        string path = test.WriteFile("Apex Legends/clip.mp4", 10, test.Clock.UtcNow);
        monitor.MarkDirty("Apex Legends/clip.mp4");

        // The recorder writes for 20 seconds; the check runs every 2.
        for (int second = 0; second < 20; second += 2)
        {
            Assert.Empty(monitor.Settle([apex], new UploaderSettings()));
            using (FileStream stream = new(path, FileMode.Append))
            {
                stream.Write(new byte[1000]);
            }

            File.SetLastWriteTimeUtc(path, test.Clock.UtcNow.UtcDateTime);
            test.Clock.Advance(FileSettler.CheckEvery);
        }

        // It stopped: still settling at 2 and 4 seconds, ready once 5 have passed.
        Assert.Empty(monitor.Settle([apex], new UploaderSettings()));
        test.Clock.Advance(FileSettler.CheckEvery);
        Assert.Empty(monitor.Settle([apex], new UploaderSettings()));
        test.Clock.Advance(FileSettler.CheckEvery);
        Assert.Empty(monitor.Settle([apex], new UploaderSettings()));
        test.Clock.Advance(FileSettler.CheckEvery);
        LedgerEntry entry = Assert.Single(monitor.Settle([apex], new UploaderSettings()));

        Assert.Equal(10_010, entry.Size);
        Assert.Equal(LedgerState.Waiting, entry.State);
        Assert.Equal(apex.Id, entry.CategoryId);
    }

    [Fact]
    public void AFileARecorderStillHolds_WaitsUntilItOpensForReading()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        bool locked = true;
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => !locked);
        monitor.Use(test.Recordings, baseline: true);
        test.WriteFile("Apex Legends/clip.mp4", 10);
        monitor.MarkDirty("Apex Legends/clip.mp4");

        monitor.Settle([apex], new UploaderSettings());
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(monitor.Settle([apex], new UploaderSettings()));

        locked = false;
        Assert.Single(monitor.Settle([apex], new UploaderSettings()));
    }

    [Fact]
    public void ARescan_FindsAFileWhoseEventWasMissed()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => true);
        monitor.Use(test.Recordings, baseline: true);

        // No watcher event arrives for it.
        test.WriteFile("Apex Legends/missed.mp4", 10);
        Assert.Equal(0, monitor.Settling);

        monitor.Rescan();
        monitor.Settle([apex], new UploaderSettings());
        test.Clock.Advance(TimeSpan.FromSeconds(6));
        LedgerEntry entry = Assert.Single(monitor.Settle([apex], new UploaderSettings()));
        Assert.Equal("Apex Legends/missed.mp4", entry.Path);
    }

    [Fact]
    public void AFileTheLedgerHas_IsNeverLookedAtAgain_UnlessItChanges()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => true);
        monitor.Use(test.Recordings, baseline: true);
        string path = test.WriteFile("Apex Legends/clip.mp4", 10);
        monitor.Rescan();
        monitor.Settle([apex], new UploaderSettings());
        test.Clock.Advance(TimeSpan.FromSeconds(6));
        LedgerEntry entry = Assert.Single(monitor.Settle([apex], new UploaderSettings()));
        entry.State = LedgerState.Shelved;
        test.Ledger.Save(entry);

        monitor.Rescan();
        monitor.MarkDirty("Apex Legends/clip.mp4");
        Assert.Equal(0, monitor.Settling);

        // Replaced by a different file of the same name: that one is new.
        File.WriteAllBytes(path, new byte[20]);
        File.SetLastWriteTimeUtc(path, test.Clock.UtcNow.AddSeconds(-30).UtcDateTime);
        monitor.Rescan();
        Assert.Equal(1, monitor.Settling);
    }

    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("Apex Legends/clip.MOV", true)]
    [InlineData("a/b/clip.Mkv", true)]
    [InlineData("clip.webm", true)]
    [InlineData("clip.avi", false)]
    [InlineData("clip.mp4.part", false)]
    [InlineData(".hidden.mp4", false)]
    [InlineData(".thumbnails/clip.mp4", false)]
    [InlineData("Apex/.cache/clip.mp4", false)]
    public void OnlyVideos_OutsideDotFolders_AreCandidates(string path, bool candidate)
    {
        Assert.Equal(candidate, VideoFiles.IsCandidate(path));
    }

    [Fact]
    public void AScan_SkipsHiddenFilesAndDotFolders()
    {
        using TestUploader test = new();
        test.WriteFile("Apex Legends/clip.mp4", 10);
        test.WriteFile(".tmp/clip.mp4", 10);
        test.WriteFile("Apex Legends/.partial.mp4", 10);
        test.WriteFile("notes.txt", 10);

        List<ScannedFile> files = FolderScanner.Scan(test.Recordings);

        Assert.Equal(["Apex Legends/clip.mp4"], files.Select(file => file.RelativePath));
        Assert.Equal(["Apex Legends"], FolderScanner.FoldersWithVideos(test.Recordings));
    }

    [Fact]
    public void ClipsWithoutABook_WaitAsNeedsBook_UntilTheFolderGetsOne()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        FolderMonitor monitor = new(test.Ledger, test.Clock, _ => true);
        monitor.Use(test.Recordings, baseline: true);
        test.WriteFile("Marvel Rivals/one.mp4", 10);
        test.WriteFile("straight in.mp4", 10);
        monitor.Rescan();
        monitor.Settle([apex], new UploaderSettings());
        test.Clock.Advance(TimeSpan.FromSeconds(6));

        List<LedgerEntry> added = monitor.Settle([apex], new UploaderSettings());

        Assert.All(added, entry => Assert.Equal(LedgerState.NeedsBook, entry.State));
        Book rivals = test.AddBook("Marvel Rivals");
        Assert.Equal(1, monitor.Rematch([apex, rivals], new UploaderSettings()));
        Assert.Equal(1, monitor.Rematch([apex, rivals], new UploaderSettings { DefaultBookId = apex.Id }));
        Assert.Empty(test.Ledger.InState(monitor.Root!, LedgerState.NeedsBook));
    }

    [Fact]
    public void Settings_RoundTrip_AndMatchFoldersIgnoringCase()
    {
        using TestUploader test = new();
        Guid book = Guid.NewGuid();
        test.SettingsStore.Save(new UploaderSettings
        {
            RecordingsFolder = test.Recordings,
            FolderBooks = { ["Marvel Rivals"] = book },
            DefaultBookId = book,
            FullScreen = FullScreenMode.UploadSlowly,
            SpeedLimitMbps = 10
        });

        UploaderSettings loaded = test.SettingsStore.Load();

        Assert.Equal(test.Recordings, loaded.RecordingsFolder);
        Assert.Equal(book, loaded.SavedBookFor("marvel rivals"));
        Assert.Equal(FullScreenMode.UploadSlowly, loaded.FullScreen);
        Assert.Equal(10, loaded.SpeedLimitMbps);
        Assert.Contains("\"uploadSlowly\"", File.ReadAllText(Path.Combine(test.Home, "settings.json")),
            StringComparison.OrdinalIgnoreCase);
    }
}
