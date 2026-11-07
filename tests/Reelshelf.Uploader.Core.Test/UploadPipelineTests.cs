using System.Buffers.Binary;
using System.Net;
using System.Text;
using Reelshelf.Uploader.Core.Ledger;
using Reelshelf.Uploader.Core.Matching;
using Reelshelf.Uploader.Core.Uploads;
using Xunit;

namespace Reelshelf.Uploader.Core.Test;

/// <summary>Uploading one file against the fake API and storage, and every answer preparing and completing can give.</summary>
public class UploadPipelineTests
{
    [Fact]
    public async Task AFile_IsHashedPreparedUploadedInPartsAndCompleted()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/Apex Legends 2026.11.06 - 21.52.10.07.mp4", apex.Id, size: 2500);

        UploadOutcome outcome = await test.Pipeline().UploadAsync(entry, test.FullPath(entry), TestUploader.Plan,
            CancellationToken.None);

        Assert.Equal(UploadOutcome.Shelved, outcome);
        FakeClip clip = Assert.Single(test.Server.Clips.Values);
        Assert.True(clip.Completed);
        Assert.Equal("Apex Legends 2026.11.06 - 21.52.10.07", clip.Title);
        Assert.Equal(apex.Id, clip.CategoryId);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(test.FullPath(entry)))),
            clip.Md5);
        Assert.Equal(entry.LastWriteUtc, clip.CreatedAt);
        // 2,500 bytes in 1,024-byte parts, one at a time.
        Assert.Equal([1024L, 1024L, 452L], clip.Parts.OrderBy(part => part.Key).Select(part => part.Value.Size));
        Assert.Equal(3, test.Server.Puts);
        Assert.Equal(1, test.Server.Requests.Count(request => request.EndsWith("/upload/parts")));

        LedgerEntry saved = test.Ledger.Find(entry.Root, entry.Path)!;
        Assert.Equal(LedgerState.Shelved, saved.State);
        Assert.Equal(clip.Id, saved.ClipId);
        Assert.Equal(3, saved.Parts.Count);
        Assert.Equal(test.Clock.UtcNow, saved.ShelvedAt);
        Assert.Equal("Bearer rsd_test", test.Server.LastAuthorization);
        Assert.Equal("uploader/1.0.3", test.Server.LastClient);
    }

    [Fact]
    public async Task ADuplicate_IsMarkedAlreadyOnTheShelf()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.PrepareAnswers.Enqueue((HttpStatusCode.Conflict, null, "A video with this MD5 hash already exists"));

        UploadOutcome outcome = await test.Pipeline().UploadAsync(entry, test.FullPath(entry), TestUploader.Plan,
            CancellationToken.None);

        Assert.Equal(UploadOutcome.Duplicate, outcome);
        Assert.Equal(LedgerState.Duplicate, test.Ledger.Find(entry.Root, entry.Path)!.State);
        Assert.Equal(0, test.Server.Puts);
    }

    [Fact]
    public async Task AFullPlan_MarksThePlanFull()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.PrepareAnswers.Enqueue((HttpStatusCode.Forbidden, "Plan Limit Reached",
            "Your plan keeps up to 100 clips and you have 100. Delete some, or upgrade to keep more."));

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.PlanFull, result.Outcome);
        Assert.Equal(LedgerState.PlanFull, result.Saved.State);
    }

    [Fact]
    public async Task AnotherRefusal_FailsTheFile()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        // A Viewer can't add clips: 403 with no plan problem.
        test.Server.PrepareAnswers.Enqueue((HttpStatusCode.Forbidden, null, null));

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.Failed, result.Outcome);
        Assert.Equal(LedgerState.Failed, result.Saved.State);
    }

    [Fact]
    public async Task ABookTakenOffTheShelf_LeavesTheFileWaitingForABook()
    {
        using TestUploader test = new();
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", Guid.NewGuid());

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.NeedsBook, result.Outcome);
        Assert.Equal(LedgerState.NeedsBook, result.Saved.State);
        Assert.Null(result.Saved.CategoryId);
    }

    [Fact]
    public async Task AClipLongerThanThePlan_IsTooLong_WithoutBeingHashedOrPrepared()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/long.mp4", apex.Id);
        File.WriteAllBytes(test.FullPath(entry), Mp4(seconds: 61.5));
        entry.Size = new FileInfo(test.FullPath(entry)).Length;

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.TooLong, result.Outcome);
        Assert.Equal(LedgerState.TooLong, result.Saved.State);
        Assert.Equal(61.5, result.Saved.LengthSeconds);
        Assert.Null(result.Saved.Md5);
        Assert.Empty(test.Server.Clips);
    }

    [Fact]
    public async Task AClipASecondOverThePlan_StillUploads()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/edge.mov", apex.Id);
        File.WriteAllBytes(test.FullPath(entry), Mp4(seconds: 60.9));
        entry.Size = new FileInfo(test.FullPath(entry)).Length;

        Assert.Equal(UploadOutcome.Shelved, (await Run(test, entry)).Outcome);
    }

    [Fact]
    public async Task AnUnreadableLength_IsLeftToTheServersProbe()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mkv", apex.Id);

        Assert.Equal(UploadOutcome.Shelved, (await Run(test, entry)).Outcome);
    }

    [Fact]
    public async Task TheProbeRefusingItsLength_MarksItTooLong_AndAnUnreadableFile_Failed()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry tooLong = test.Waiting("Apex Legends/a.mkv", apex.Id);
        LedgerEntry unreadable = test.Waiting("Apex Legends/b.mkv", apex.Id);
        test.Server.CompleteAnswers.Enqueue((HttpStatusCode.UnprocessableEntity,
            "This clip is 2:14. Your plan keeps clips up to 1:00, so trim it or upgrade for clips up to 2:00."));
        test.Server.CompleteAnswers.Enqueue((HttpStatusCode.UnprocessableEntity, "The file couldn't be read as a video."));

        UploaderPipelineResult first = await Run(test, tooLong);
        UploaderPipelineResult second = await Run(test, unreadable);

        Assert.Equal(UploadOutcome.TooLong, first.Outcome);
        Assert.Equal(LedgerState.TooLong, first.Saved.State);
        Assert.Equal(UploadOutcome.Failed, second.Outcome);
        Assert.Equal(LedgerState.Failed, second.Saved.State);
        Assert.Equal("The file couldn't be read as a video.", second.Saved.LastError);
    }

    [Fact]
    public async Task ACheckThatCouldntFinish_IsTriedAgainInAMinute_WithoutUsingAnAttempt()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.CompleteAnswers.Enqueue((HttpStatusCode.ServiceUnavailable,
            "The upload couldn't be checked just now; try again"));

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.RetryLater, result.Outcome);
        Assert.Equal(LedgerState.Uploading, result.Saved.State);
        Assert.Equal(test.Clock.UtcNow.AddMinutes(1), result.Saved.NextAttemptAt);
        Assert.Equal(0, result.Saved.Attempts);

        // The next try completes the same upload; nothing is sent twice.
        test.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(UploadOutcome.Shelved, (await Run(test, result.Saved)).Outcome);
        Assert.Single(test.Server.Clips);
        Assert.Equal(3, test.Server.Puts);
    }

    [Fact]
    public async Task AnotherUploadProtocol_FailsTheFile_AndGivesTheClipBack()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.Protocol = "tus";

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.Unsupported, result.Outcome);
        Assert.Equal(LedgerState.Failed, result.Saved.State);
        Assert.True(Assert.Single(test.Server.Clips.Values).Deleted);
        Assert.Equal(0, test.Server.Puts);
    }

    [Fact]
    public async Task AnUploadUnderWay_ResumesFromThePartsStorageHas()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id, size: 3000);
        // The first part went in before the app stopped; the second never did.
        test.Server.OnPart = number =>
        {
            if (number == 1)
            {
                throw new OperationCanceledException("The app stopped");
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            test.Pipeline().UploadAsync(entry, test.FullPath(entry), TestUploader.Plan, CancellationToken.None));
        LedgerEntry saved = test.Ledger.Find(entry.Root, entry.Path)!;
        Assert.Equal(LedgerState.Uploading, saved.State);
        Assert.Empty(saved.Parts);
        test.Server.OnPart = null;
        int putsBefore = test.Server.Puts;

        UploaderPipelineResult result = await Run(test, saved);

        Assert.Equal(UploadOutcome.Shelved, result.Outcome);
        Assert.Contains($"GET /api/clips/videos/{saved.ClipId}/upload/parts", test.Server.Requests);
        // Part 1 was in storage already, so only parts 2 and 3 were sent.
        Assert.Equal(2, test.Server.Puts - putsBefore);
        Assert.Single(test.Server.Clips);
    }

    [Fact]
    public async Task AnUploadThatIsGone_DeletesItsClip_AndStartsAgain()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.CompleteAnswers.Enqueue((HttpStatusCode.ServiceUnavailable, null));
        LedgerEntry saved = (await Run(test, entry)).Saved;
        Guid firstClip = saved.ClipId!.Value;
        test.Server.Clips[firstClip].UploadGone = true;

        UploaderPipelineResult result = await Run(test, saved);

        Assert.Equal(UploadOutcome.Shelved, result.Outcome);
        Assert.True(test.Server.Clips[firstClip].Deleted);
        Assert.NotEqual(firstClip, result.Saved.ClipId);
        Assert.Equal(2, test.Server.Clips.Count);
    }

    [Fact]
    public async Task APart_IsRetriedAfterOneTwoFourAndEightSeconds_ThenTheAttemptFails()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id, size: 500);
        foreach (HttpStatusCode? answer in new HttpStatusCode?[] { HttpStatusCode.InternalServerError, null,
                     HttpStatusCode.ServiceUnavailable, null, HttpStatusCode.BadGateway })
        {
            test.Server.PutAnswers.Enqueue(answer);
        }

        UploaderPipelineResult result = await Run(test, entry);

        Assert.Equal(UploadOutcome.RetryLater, result.Outcome);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)],
            test.Clock.Delays);
        Assert.Equal(1, result.Saved.Attempts);
        Assert.Equal(LedgerState.Uploading, result.Saved.State);
        Assert.Equal(test.Clock.UtcNow + UploadPipeline.Backoff(1), result.Saved.NextAttemptAt);

        // A part that fails then goes in carries on without using an attempt.
        test.Server.PutAnswers.Enqueue(HttpStatusCode.InternalServerError);
        test.Clock.Advance(TimeSpan.FromMinutes(5));
        UploaderPipelineResult retried = await Run(test, result.Saved);
        Assert.Equal(UploadOutcome.Shelved, retried.Outcome);
    }

    [Fact]
    public async Task NetworkFailures_BackOffUpToFiveMinutes_AndFailTheFileAfterFiveAttempts()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id);
        test.Server.ApiDown = true;
        List<TimeSpan> waits = [];

        UploaderPipelineResult result = new(UploadOutcome.RetryLater, entry);
        for (int attempt = 1; attempt <= UploadPipeline.MaxAttempts; attempt++)
        {
            result = await Run(test, result.Saved);
            if (result.Saved.NextAttemptAt is { } next)
            {
                waits.Add(next - test.Clock.UtcNow);
                test.Clock.UtcNow = next;
            }
        }

        Assert.Equal(UploadOutcome.Failed, result.Outcome);
        Assert.Equal(LedgerState.Failed, result.Saved.State);
        Assert.Equal([TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120)], waits);
        Assert.Equal(TimeSpan.FromMinutes(5), UploadPipeline.Backoff(9));

        // A failed file gets one more try when the app next starts.
        Assert.Equal(1, test.Ledger.RetryFailedOnce(entry.Root, UploadPipeline.MaxAttempts, test.Clock.UtcNow));
        test.Server.ApiDown = false;
        Assert.Equal(UploadOutcome.Shelved, (await Run(test, test.Ledger.Find(entry.Root, entry.Path)!)).Outcome);
    }

    [Fact]
    public async Task TheSpeedLimit_IsAppliedToPartBodies()
    {
        using TestUploader test = new();
        Book apex = test.AddBook("Apex Legends");
        test.Server.PartSize = 400_000;
        LedgerEntry entry = test.Waiting("Apex Legends/clip.mp4", apex.Id, size: 1_000_000);
        SlowGate gate = new(TokenBucket.FromMbps(2)); // 250,000 bytes a second

        UploaderPipelineResult result = await Run(test, entry, gate);

        Assert.Equal(UploadOutcome.Shelved, result.Outcome);
        // 1,000,000 bytes at 250,000 a second take four seconds.
        Assert.InRange(test.Clock.Delays.Sum(delay => delay.TotalSeconds), 3.9, 4.1);
    }

    private static async Task<UploaderPipelineResult> Run(TestUploader test, LedgerEntry entry, IUploadGate? gate = null)
    {
        UploadOutcome outcome = await test.Pipeline(gate).UploadAsync(entry, test.FullPath(entry), TestUploader.Plan,
            CancellationToken.None);
        return new UploaderPipelineResult(outcome, test.Ledger.Find(entry.Root, entry.Path)!);
    }

    /// <summary>A small MP4 whose movie header gives this length.</summary>
    internal static byte[] Mp4(double seconds)
    {
        byte[] mvhd = new byte[8 + 100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd, (uint)mvhd.Length);
        Encoding.ASCII.GetBytes("mvhd").CopyTo(mvhd, 4);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(8 + 12), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(8 + 16), (uint)(seconds * 1000));
        byte[] moov = new byte[8 + mvhd.Length];
        BinaryPrimitives.WriteUInt32BigEndian(moov, (uint)moov.Length);
        Encoding.ASCII.GetBytes("moov").CopyTo(moov, 4);
        mvhd.CopyTo(moov, 8);
        byte[] mdat = new byte[8 + 2000];
        BinaryPrimitives.WriteUInt32BigEndian(mdat, (uint)mdat.Length);
        Encoding.ASCII.GetBytes("mdat").CopyTo(mdat, 4);
        new Random(1).NextBytes(mdat.AsSpan(8));
        return [.. moov, .. mdat];
    }

    private sealed record UploaderPipelineResult(UploadOutcome Outcome, LedgerEntry Saved);

    private sealed class SlowGate(long? bytesPerSecond) : IUploadGate
    {
        public Task WaitUntilOpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public long? BytesPerSecond => bytesPerSecond;
    }
}
