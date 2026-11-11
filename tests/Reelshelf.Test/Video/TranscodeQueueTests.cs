using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipTranscription;
using Reelshelf.Core;
using Reelshelf.Video;
using Reelshelf.Video.Transcoding;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>
/// Runs the transcode queue against a real Postgres, through <see cref="TranscodeQueue"/> as the worker endpoints
/// call it: the video pipeline's migrations on a minimal <c>clip</c> table in a schema of the test's own, dropped afterwards.
/// The statements share one closed connection, as they do in a request scope. Legend detection and transcription
/// are configured off, so the follow-ups queue nothing; their queueing is covered by their own tests.
/// </summary>
public class TranscodeQueueTests
{
    [PostgresFact]
    public async Task FakeWorker_ClaimsReportsProgressAndCompletes_AndTheClipEndsReady()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        Guid? runId = await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode,
            new TranscodeRunInput(SourceKey: $"uploads/{clipId}", TrimStartSeconds: 2.5));
        Assert.NotNull(runId);

        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((runId.Value, clipId, "transcode", 1, 1), (job.RunId, job.ClipId, job.Type, job.VideoVersion, job.Attempt));
        Assert.Equal($"uploads/{clipId}", job.Input.GetProperty("source_key").GetString());
        Assert.Equal(2.5, job.Input.GetProperty("trim_start_seconds").GetDouble());
        Assert.False(job.Input.TryGetProperty("trim_end_seconds", out _));
        // The storage presigns the job's URLs for the version it writes, living past the stale timeout.
        Assert.Equal($"get transcode {clipId} v1 from v1 for {TimeSpan.FromHours(2)}", job.SourceUrl);
        Assert.Equal("put av1.mp4", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(job.UploadUrls)["av1.mp4"]);
        Assert.Null(await test.Queue.ClaimAsync());

        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 40, true));
        Assert.Equal(("processing", 40), await test.ClipStateAsync(clipId));
        // A delayed report never takes the percentage back.
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 30, false));
        Assert.Equal(("processing", 40), await test.ClipStateAsync(clipId));
        Assert.True(await test.Connection.ExecuteScalarAsync<bool>(
            "SELECT thumbnails_ready_at IS NOT NULL FROM transcode_run WHERE id = @RunId", new { job.RunId }));

        using JsonDocument outputs = JsonDocument.Parse("""
            {"codecs": "av01.0.08M.08, opus", "width": 1920, "height": 1080,
             "files": ["av1.mp4", "thumbnail.jpg", "thumbnail_1.jpg", "thumbnail_2.jpg", "thumbnail_3.jpg",
                       "thumbnail_4.jpg", "thumbnail_5.jpg", "preview.mp4"]}
            """);
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 31.6, 12_345_678));

        ClipAfter clip = await test.ClipAsync(clipId);
        Assert.Equal(("ready", 100, 32, 12_345_678L, (int?)null),
            (clip.VideoState, clip.EncodeProgress, clip.Length, clip.StorageSize, clip.VideoStatus));
        // The quota counts the AV1 output alone, and the clip's sources are typed with its codecs and height.
        Assert.Equal((1, "av01.0.08M.08,opus", 1080, (long?)null),
            (clip.VideoVersion, clip.VideoCodecs, clip.VideoHeight, clip.FileSize));
        Assert.Equal([clipId], test.Storage.DeletedSources);
        Assert.Empty(test.Storage.DeletedVersions);

        (string status, string output) = await test.Connection.QuerySingleAsync<(string, string)>(
            "SELECT status, output::text FROM transcode_run WHERE id = @RunId", new { job.RunId });
        Assert.Equal("succeeded", status);
        using JsonDocument stored = JsonDocument.Parse(output);
        Assert.Equal(8, stored.RootElement.GetProperty("outputs").GetProperty("files").GetArrayLength());
        Assert.Equal(12_345_678, stored.RootElement.GetProperty("size_bytes").GetInt64());

        // The worker retrying a completion whose response it lost is accepted; anything else on the job is not.
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 31.6, 12_345_678));
        Assert.Equal(TranscodeReportOutcome.LeaseLost,
            await test.Queue.CompleteAsync(job.RunId, Guid.NewGuid(), outputs.RootElement, 31.6, 12_345_678));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 50, false));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.FailAsync(job.RunId, job.LeaseId, "late", false));
        Assert.Equal(TranscodeReportOutcome.NotFound, await test.Queue.ReportProgressAsync(Guid.NewGuid(), job.LeaseId, 50, false));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task Trim_MovesTheReadyClipToItsNewVersion_AndDeletesTheOldOne()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Trim,
            new TranscodeRunInput(TrimStartSeconds: 1, TrimEndSeconds: 9), videoVersion: 2);

        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        // A trim reads the current version and writes the next.
        Assert.Equal($"get trim {clipId} v2 from v1 for {TimeSpan.FromHours(2)}", job.SourceUrl);

        // The clip keeps playing its current version while the trim runs.
        await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 50, true);
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);

        using JsonDocument outputs = JsonDocument.Parse("""{"codecs": "av01.0.05M.08,opus", "height": 720}""");
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 8, 4_000_000));

        ClipAfter clip = await test.ClipAsync(clipId);
        Assert.Equal(("ready", 8, 4_000_000L, 2, "av01.0.05M.08,opus", 720),
            (clip.VideoState, clip.Length, clip.StorageSize, clip.VideoVersion, clip.VideoCodecs, clip.VideoHeight));
        Assert.Equal([(clipId, 1)], test.Storage.DeletedVersions);
        Assert.Empty(test.Storage.DeletedSources);
    }

    [PostgresFact]
    public async Task Audio_MovesTheReadyClipToItsNewVersion_WithAacCodecs_AndDeletesTheOldOne()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Audio, new TranscodeRunInput(), videoVersion: 2);

        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal($"get audio {clipId} v2 from v1 for {TimeSpan.FromHours(2)}", job.SourceUrl);

        await test.Queue.ReportProgressAsync(job.RunId, job.LeaseId, 50, true);
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);

        using JsonDocument outputs = JsonDocument.Parse("""{"codecs": "av01.0.08M.08,mp4a.40.2", "height": 1080}""");
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 30, 15_000_000));

        ClipAfter clip = await test.ClipAsync(clipId);
        Assert.Equal(("ready", 2, "av01.0.08M.08,mp4a.40.2", 1080),
            (clip.VideoState, clip.VideoVersion, clip.VideoCodecs, clip.VideoHeight));
        Assert.Equal([(clipId, 1)], test.Storage.DeletedVersions);
        Assert.Empty(test.Storage.DeletedSources);
    }

    [PostgresFact]
    public async Task AacBackfill_QueuesReadyOpusClipsOnTheirNextVersion_UpToTheBatch()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        TranscodeStatements statements = new(test.Connection);
        Guid opus = await test.AddClipAsync("ready");
        Guid opusTrimmed = await test.AddClipAsync("ready");
        Guid aac = await test.AddClipAsync("ready");
        Guid silent = await test.AddClipAsync("ready");
        Guid encoding = await test.AddClipAsync("processing");
        Guid busy = await test.AddClipAsync("ready");
        Guid gaveUp = await test.AddClipAsync("ready");
        await test.Connection.ExecuteAsync("""
            UPDATE clip SET video_codecs = CASE
                WHEN id = @Aac THEN 'av01.0.08M.08,mp4a.40.2'
                WHEN id = @Silent THEN 'av01.0.08M.08'
                ELSE 'av01.0.08M.08,opus' END,
              video_version = CASE WHEN id = @OpusTrimmed THEN 3 ELSE 1 END
            """, new { Aac = aac, Silent = silent, OpusTrimmed = opusTrimmed });
        // One with a job already waiting, and one whose audio job failed on its current version.
        await test.Queue.EnqueueAsync(busy, TranscodeRunTypes.Thumbnails, new TranscodeRunInput());
        await test.Connection.ExecuteAsync("""
            INSERT INTO transcode_run (clip_id, type, input, video_version, priority, status)
            VALUES (@GaveUp, 'audio', '{}', 2, 2, 'failed')
            """, new { GaveUp = gaveUp });

        Assert.Equal(1, await statements.EnqueueAacBackfillAsync(1, 2));
        Assert.Equal(1, await statements.EnqueueAacBackfillAsync(5, 2));
        Assert.Equal(0, await statements.EnqueueAacBackfillAsync(5, 2));

        List<(Guid ClipId, int VideoVersion, int Priority)> queued = (await test.Connection.QueryAsync<(Guid, int, int)>(
            "SELECT clip_id, video_version, priority FROM transcode_run WHERE type = 'audio' AND status = 'pending'")).ToList();
        Assert.Equal([(opus, 2, 2), (opusTrimmed, 4, 2)], queued.OrderBy(run => run.VideoVersion));
    }

    [PostgresFact]
    public async Task Trims_FinishingOutOfOrder_LeaveTheClipOnTheNewerVersion_AndDeleteTheOlderTrimsOutputs()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Trim, new TranscodeRunInput(TrimEndSeconds: 9), videoVersion: 2);
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Trim, new TranscodeRunInput(TrimEndSeconds: 5), videoVersion: 3);
        ClaimedTranscodeRun older = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        ClaimedTranscodeRun newer = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((2, 3), (older.VideoVersion, newer.VideoVersion));

        using JsonDocument newerOutputs = JsonDocument.Parse("""{"codecs": "av01.0.05M.08,opus", "height": 720}""");
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(newer.RunId, newer.LeaseId, newerOutputs.RootElement, 5, 2_000_000));
        Assert.Equal([(clipId, 1)], test.Storage.DeletedVersions);

        using JsonDocument olderOutputs = JsonDocument.Parse("""{"codecs": "av01.0.08M.08,opus", "height": 1080}""");
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(older.RunId, older.LeaseId, olderOutputs.RootElement, 9, 4_000_000));

        // The older trim still succeeds, but the clip stays on the newer version and only the older trim's files go.
        Assert.Equal("succeeded", await test.RunStatusAsync(older.RunId));
        ClipAfter clip = await test.ClipAsync(clipId);
        Assert.Equal(("ready", 5, 2_000_000L, 3, "av01.0.05M.08,opus", 720),
            (clip.VideoState, clip.Length, clip.StorageSize, clip.VideoVersion, clip.VideoCodecs, clip.VideoHeight));
        Assert.Equal([(clipId, 1), (clipId, 2)], test.Storage.DeletedVersions);
        Assert.Empty(test.Storage.DeletedSources);

        // A repeat of the superseded report changes and deletes nothing more.
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.CompleteAsync(older.RunId, older.LeaseId, olderOutputs.RootElement, 9, 4_000_000));
        Assert.Equal(3, (await test.ClipAsync(clipId)).VideoVersion);
        Assert.Equal([(clipId, 1), (clipId, 2)], test.Storage.DeletedVersions);
    }

    [PostgresFact]
    public async Task Completion_WithOutputsItCantUse_StillReadiesTheClip()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());

        using JsonDocument outputs = JsonDocument.Parse("""{"codecs": "av01\" onerror=\"x", "height": -5}""");
        await test.Queue.CompleteAsync(job.RunId, job.LeaseId, outputs.RootElement, 5, 50);

        ClipAfter clip = await test.ClipAsync(clipId);
        Assert.Equal(("ready", 1, (string?)null, (int?)null),
            (clip.VideoState, clip.VideoVersion, clip.VideoCodecs, clip.VideoHeight));
    }

    [PostgresFact]
    public async Task Enqueue_KeepsOneActiveTranscodePerClip()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");

        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a")));
        Assert.Null(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
        // Other job types are not limited.
        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("a")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            test.Queue.EnqueueAsync(clipId, "remux", new TranscodeRunInput()));

        // Once the running transcode ends, the clip can have another.
        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeRunTypes.Transcode, job.Type);
        Assert.Null(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
        await test.Queue.CompleteAsync(job.RunId, job.LeaseId, null, 10, 100);
        Assert.NotNull(await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("b")));
    }

    [PostgresFact]
    public async Task Claim_HandsAPoolOnlyItsJobTypes_AndLeavesTheRestForOtherWorkers()
    {
        await using TestQueue test = await TestQueue.CreateAsync(
            workerJobTypes: new() { ["aws"] = "transcode, trim,thumbnails" });
        Guid ready = await test.AddClipAsync("ready");
        Guid uploaded = await test.AddClipAsync("uploaded");

        // The backfill's audio job is older, but an overflow worker would pay to upload the whole video again.
        Guid? audio = await test.Queue.EnqueueAsync(ready, TranscodeRunTypes.Audio, new TranscodeRunInput(), videoVersion: 2);
        Guid? transcode = await test.Queue.EnqueueAsync(uploaded, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        await test.Connection.ExecuteAsync("UPDATE transcode_run SET priority = 0, created_at = now() - interval '1 minute' WHERE id = @Id",
            new { Id = audio });

        Assert.Equal(transcode, (await test.Queue.ClaimAsync("AWS"))?.RunId);
        // Only the audio job waits now: the pool gets nothing, so its workers go idle and stop.
        Assert.Null(await test.Queue.ClaimAsync("aws"));
        Assert.Equal((0, "pending"), await test.Connection.QuerySingleAsync<(int, string)>(
            "SELECT attempts, status FROM transcode_run WHERE id = @Id", new { Id = audio }));

        // A pool with no entry claims every type.
        Assert.Equal(audio, (await test.Queue.ClaimAsync("default"))?.RunId);
    }

    [PostgresFact]
    public async Task Claim_DoesNotReclaimAStaleJobOfATypeThePoolMayNotTake()
    {
        await using TestQueue test = await TestQueue.CreateAsync(workerJobTypes: new() { ["aws"] = "transcode" });
        Guid clipId = await test.AddClipAsync("ready");
        Guid? audio = await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Audio, new TranscodeRunInput(), videoVersion: 2);
        Assert.Equal(audio, (await test.Queue.ClaimAsync("default"))?.RunId);
        await test.Connection.ExecuteAsync(
            "UPDATE transcode_run SET heartbeat_at = now() - interval '1 hour' WHERE id = @Id", new { Id = audio });

        Assert.Null(await test.Queue.ClaimAsync("aws"));
        Assert.Equal(2, (await test.Queue.ClaimAsync("default"))?.Attempt);
    }

    [PostgresFact]
    public async Task Claim_GoesByPriority_ThenAge()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid first = await test.AddClipAsync("ready");
        Guid second = await test.AddClipAsync("uploaded");
        Guid third = await test.AddClipAsync("uploaded");

        Guid? thumbnails = await test.Queue.EnqueueAsync(first, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("a"));
        Guid? older = await test.Queue.EnqueueAsync(second, TranscodeRunTypes.Transcode, new TranscodeRunInput("b"));
        Guid? newer = await test.Queue.EnqueueAsync(third, TranscodeRunTypes.Transcode, new TranscodeRunInput("c"));
        await test.Connection.ExecuteAsync("UPDATE transcode_run SET created_at = now() - interval '1 minute' WHERE id = @Id",
            new { Id = older });

        Assert.Equal(older, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Equal(newer, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Equal(thumbnails, (await test.Queue.ClaimAsync())?.RunId);
        Assert.Null(await test.Queue.ClaimAsync());
    }

    [PostgresFact]
    public async Task RetryableFailure_WaitsTheRetryDelay_ThenIsClaimedUnderANewLease()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 40, false));
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.FailAsync(first.RunId, first.LeaseId, "R2 timed out", true));
        // Repeating the report is harmless.
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.FailAsync(first.RunId, first.LeaseId, "R2 timed out", true));
        Assert.Equal("pending", await test.RunStatusAsync(first.RunId));
        Assert.NotEqual("encode_failed", (await test.ClipStateAsync(clipId)).State);
        // Waiting to retry, it has no heartbeat or progress, but keeps the lease so the repeat above is recognised.
        (DateTime? heartbeat, int? progress, Guid? lease) = await test.Connection.QuerySingleAsync<(DateTime?, int?, Guid?)>(
            "SELECT heartbeat_at, progress, lease_id FROM transcode_run WHERE id = @RunId", new { first.RunId });
        Assert.Null(heartbeat);
        Assert.Null(progress);
        Assert.Equal(first.LeaseId, lease);
        Assert.Null(await test.Queue.ClaimAsync());

        await test.Connection.ExecuteAsync("UPDATE transcode_run SET available_at = now() - interval '1 second'");
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(2, second.Attempt);
        Assert.NotEqual(first.LeaseId, second.LeaseId);

        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.FailAsync(first.RunId, first.LeaseId, "again", true));
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.CompleteAsync(second.RunId, second.LeaseId, null, 5, 50));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task StaleJob_IsReclaimed_AndItsFirstWorkerCannotReportOverTheSecond()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        // A progress report is a heartbeat: a job quiet for a while that then reports is not reclaimed.
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 10, false));
        Assert.Null(await test.Queue.ClaimAsync());

        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((first.RunId, 2), (second.RunId, second.Attempt));

        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 90, false));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.CompleteAsync(first.RunId, first.LeaseId, null, 5, 50));
        Assert.Equal("processing", (await test.ClipStateAsync(clipId)).State);

        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.CompleteAsync(second.RunId, second.LeaseId, null, 7, 70));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task Transcode_ThatRunsOutOfAttempts_LeavesItsClipEncodeFailed()
    {
        await using TestQueue test = await TestQueue.CreateAsync(maxAttempts: 2);

        // Its worker goes quiet on the last attempt: the next claim gives up on it.
        Guid quiet = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(quiet, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Equal(2, (await test.Queue.ClaimAsync())?.Attempt);
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        Assert.Null(await test.Queue.ClaimAsync());
        Assert.Equal("failed", await test.RunStatusAsync(job.RunId));
        Assert.StartsWith("Gave up after 2 attempts", await test.Connection.QuerySingleAsync<string>(
            "SELECT error FROM transcode_run WHERE id = @RunId", new { job.RunId }));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(quiet)).State);

        // A retryable failure on the last attempt fails the job there and then.
        Guid failing = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(failing, TranscodeRunTypes.Transcode, new TranscodeRunInput("b"));
        ClaimedTranscodeRun attempt = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.FailAsync(attempt.RunId, attempt.LeaseId, "worker ran out of disk", true);
        await test.Connection.ExecuteAsync("UPDATE transcode_run SET available_at = now() - interval '1 second'");
        attempt = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeReportOutcome.Applied,
            await test.Queue.FailAsync(attempt.RunId, attempt.LeaseId, "worker ran out of disk", true));
        Assert.Equal("failed", await test.RunStatusAsync(attempt.RunId));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(failing)).State);
    }

    [PostgresFact]
    public async Task NonRetryableFailure_FailsAtOnce_AndOnlyATranscodeFailsItsClip()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid uploaded = await test.AddClipAsync("uploaded");
        Guid ready = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(uploaded, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));
        await test.Queue.EnqueueAsync(ready, TranscodeRunTypes.Thumbnails, new TranscodeRunInput("b"));

        ClaimedTranscodeRun transcode = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        ClaimedTranscodeRun thumbnails = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.FailAsync(transcode.RunId, transcode.LeaseId, new string('x', 10_000), false);
        await test.Queue.ReportProgressAsync(thumbnails.RunId, thumbnails.LeaseId, 50, true);
        await test.Queue.FailAsync(thumbnails.RunId, thumbnails.LeaseId, "no such output", false);

        Assert.Equal("failed", await test.RunStatusAsync(transcode.RunId));
        Assert.Equal(TranscodeQueue.MaxErrorLength, await test.Connection.QuerySingleAsync<int>(
            "SELECT length(error) FROM transcode_run WHERE id = @RunId", new { transcode.RunId }));
        Assert.Equal("encode_failed", (await test.ClipStateAsync(uploaded)).State);
        Assert.Equal("failed", await test.RunStatusAsync(thumbnails.RunId));
        Assert.Equal("ready", (await test.ClipStateAsync(ready)).State);
        Assert.Null(await test.Queue.ClaimAsync());
    }

    [PostgresFact]
    public async Task Release_RefundsTheAttempt_AndPutsTheJobAndItsClipBack()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 40, true);
        Assert.Equal(("processing", 40), await test.ClipStateAsync(clipId));

        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReleaseAsync(first.RunId, first.LeaseId));
        RunAfter run = await test.RunAsync(first.RunId);
        Assert.Equal(("pending", 0, (Guid?)null, false, (int?)null, true),
            (run.Status, run.Attempts, run.LeaseId, run.HasHeartbeat, run.Progress, run.Available));
        Assert.Equal(("uploaded", 0), await test.ClipStateAsync(clipId));

        // Repeating the release is accepted and changes nothing; the released lease can report nothing else.
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReleaseAsync(first.RunId, first.LeaseId));
        Assert.Equal(0, (await test.RunAsync(first.RunId)).Attempts);
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReleaseAsync(first.RunId, Guid.NewGuid()));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReportProgressAsync(first.RunId, first.LeaseId, 50, false));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.CompleteAsync(first.RunId, first.LeaseId, null, 5, 50));
        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.FailAsync(first.RunId, first.LeaseId, "late", true));
        Assert.Equal(TranscodeReportOutcome.NotFound, await test.Queue.ReleaseAsync(Guid.NewGuid(), first.LeaseId));

        // Claimable straight away, as a first attempt under a new lease.
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal((first.RunId, 1), (second.RunId, second.Attempt));
        Assert.NotEqual(first.LeaseId, second.LeaseId);
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.CompleteAsync(second.RunId, second.LeaseId, null, 5, 50));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
        // The completion keeps the count of releases next to what the worker reported.
        Assert.Equal(("1", "5"), await test.Connection.QuerySingleAsync<(string, string)>(
            "SELECT output ->> 'release_count', output ->> 'length_seconds' FROM transcode_run WHERE id = @RunId",
            new { first.RunId }));
    }

    [PostgresFact]
    public async Task Release_UnderAStaleLease_IsRefused_AndLeavesTheNewWorkersJob()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        ClaimedTranscodeRun first = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.BackdateHeartbeatAsync(TimeSpan.FromHours(2));
        ClaimedTranscodeRun second = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        await test.Queue.ReportProgressAsync(second.RunId, second.LeaseId, 30, true);

        Assert.Equal(TranscodeReportOutcome.LeaseLost, await test.Queue.ReleaseAsync(first.RunId, first.LeaseId));
        RunAfter run = await test.RunAsync(second.RunId);
        Assert.Equal(("running", 2, second.LeaseId), (run.Status, run.Attempts, run.LeaseId));
        Assert.Equal(("processing", 30), await test.ClipStateAsync(clipId));
    }

    [PostgresFact]
    public async Task Release_OfAJobOtherThanATranscode_LeavesItsReadyClipReady()
    {
        await using TestQueue test = await TestQueue.CreateAsync();
        Guid clipId = await test.AddClipAsync("ready");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Trim, new TranscodeRunInput(TrimEndSeconds: 5), videoVersion: 2);

        ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
        Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReleaseAsync(job.RunId, job.LeaseId));

        RunAfter run = await test.RunAsync(job.RunId);
        Assert.Equal(("pending", 0), (run.Status, run.Attempts));
        Assert.Equal("ready", (await test.ClipStateAsync(clipId)).State);
    }

    [PostgresFact]
    public async Task Releases_PastTheCap_UseAttempts_SoABouncingJobStillEndsEncodeFailed()
    {
        await using TestQueue test = await TestQueue.CreateAsync(maxAttempts: 2, maxReleases: 2);
        Guid clipId = await test.AddClipAsync("uploaded");
        await test.Queue.EnqueueAsync(clipId, TranscodeRunTypes.Transcode, new TranscodeRunInput("a"));

        // Two releases are free; every one after that keeps the attempt its claim counted.
        int[] attemptsAfterRelease = [0, 0, 1, 2];
        foreach (int expected in attemptsAfterRelease)
        {
            ClaimedTranscodeRun job = Assert.IsType<ClaimedTranscodeRun>(await test.Queue.ClaimAsync());
            Assert.Equal(TranscodeReportOutcome.Applied, await test.Queue.ReleaseAsync(job.RunId, job.LeaseId));
            Assert.Equal(expected, (await test.RunAsync(job.RunId)).Attempts);
        }

        Assert.Null(await test.Queue.ClaimAsync());
        Assert.Equal("encode_failed", (await test.ClipStateAsync(clipId)).State);
    }

    private sealed class RunAfter
    {
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public Guid? LeaseId { get; set; }
        public bool HasHeartbeat { get; set; }
        public int? Progress { get; set; }
        public bool Available { get; set; }
    }

    private sealed class ClipAfter
    {
        public string VideoState { get; set; } = "";
        public int? EncodeProgress { get; set; }
        public int? Length { get; set; }
        public long? StorageSize { get; set; }
        public int? VideoStatus { get; set; }
        public int VideoVersion { get; set; }
        public string? VideoCodecs { get; set; }
        public int? VideoHeight { get; set; }
        public long? FileSize { get; set; }
    }

    /// <summary>Hands out URLs that say what they were presigned for, and records what it was asked to delete.</summary>
    private sealed class FakeStorage : ITranscodeStorage
    {
        public List<Guid> DeletedSources { get; } = [];
        public List<(Guid ClipId, int Version)> DeletedVersions { get; } = [];

        public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime) =>
            new($"get {jobType} {clipId} v{videoVersion} from v{currentVersion} for {lifetime}",
                new Dictionary<string, string> { ["av1.mp4"] = "put av1.mp4" });

        public Task DeleteSourceAsync(Guid clipId)
        {
            DeletedSources.Add(clipId);
            return Task.CompletedTask;
        }

        public Task DeleteVersionAsync(Guid clipId, int videoVersion)
        {
            DeletedVersions.Add((clipId, videoVersion));
            return Task.CompletedTask;
        }

        public Task DeleteAllVersionsAsync(Guid clipId) =>
            throw new InvalidOperationException("The queue never deletes every version");
    }

    private sealed class TestQueue : IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly string _schema;

        private TestQueue(NpgsqlDataSource dataSource, NpgsqlConnection connection, string schema, TranscodeQueue queue,
            FakeStorage storage)
        {
            _dataSource = dataSource;
            Connection = connection;
            _schema = schema;
            Queue = queue;
            Storage = storage;
        }

        /// <summary>A separate connection for arranging and checking rows.</summary>
        public NpgsqlConnection Connection { get; }

        public TranscodeQueue Queue { get; }

        public FakeStorage Storage { get; }

        /// <param name="storage">The storage the queue uses instead of <see cref="Storage"/>, e.g. R2 over <see cref="FakeR2Client"/>.</param>
        public static async Task<TestQueue> CreateAsync(int maxAttempts = 3, int maxReleases = 10,
            ITranscodeStorage? storage = null, Dictionary<string, string?>? workerJobTypes = null)
        {
            // The API sets this at startup; the statements' row classes rely on it.
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            string schema = $"transcode_test_{Guid.NewGuid():N}";
            await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
            {
                await admin.OpenAsync();
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
            }

            NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
                $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await TestClipSchema.CreateAsync(connection);

            // One closed connection shared by the statements, as the request scope shares one in the API.
            NpgsqlConnection scoped = dataSource.CreateConnection();
            ClipsStatements clips = new(scoped);
            ClipVideoUpdateHandler handler = new(
                clips,
                new LegendDetectionService(null!, null!, null!, null!, null!,
                    new FixedOptions<LegendDetectionOptions>(new LegendDetectionOptions { AutoDetect = false }),
                    null!, NullLogger<LegendDetectionService>.Instance),
                new ClipTranscriptionService(null!, null!, null!, null!, null!,
                    new FixedOptions<ClipTranscriptionOptions>(new ClipTranscriptionOptions { AutoQueue = false }),
                    null!, NullLogger<ClipTranscriptionService>.Instance),
                NullLogger<ClipVideoUpdateHandler>.Instance);
            FakeStorage fake = new();
            TranscodeQueue queue = new(
                new TranscodeStatements(scoped),
                handler,
                storage ?? fake,
                new FixedOptions<TranscodingOptions>(new TranscodingOptions
                {
                    MaxAttempts = maxAttempts, MaxReleases = maxReleases, WorkerJobTypes = workerJobTypes ?? []
                }),
                NullLogger<TranscodeQueue>.Instance);
            return new TestQueue(dataSource, connection, schema, queue, fake);
        }

        public async Task<Guid> AddClipAsync(string videoState)
        {
            Guid clipId = Guid.NewGuid();
            await Connection.ExecuteAsync("""
                INSERT INTO clip (id, owner_id, video_id, game_category_id, title, video_state, video_host, file_size)
                VALUES (@ClipId, @OwnerId, @ClipId, @GameCategoryId, 'Clip', @VideoState, 'r2', 50000000)
                """, new
            {
                ClipId = clipId, OwnerId = Guid.NewGuid(), GameCategoryId = Guid.NewGuid(),
                VideoState = videoState
            });
            return clipId;
        }

        public async Task<(string State, int? Progress)> ClipStateAsync(Guid clipId) =>
            await Connection.QuerySingleAsync<(string, int?)>(
                "SELECT video_state, encode_progress FROM clip WHERE id = @ClipId", new { ClipId = clipId });

        public async Task<ClipAfter> ClipAsync(Guid clipId) =>
            await Connection.QuerySingleAsync<ClipAfter>("""
                SELECT video_state, encode_progress, length, storage_size, video_status, video_version, video_codecs,
                       video_height, file_size
                FROM clip WHERE id = @ClipId
                """, new { ClipId = clipId });

        public async Task<RunAfter> RunAsync(Guid runId) =>
            await Connection.QuerySingleAsync<RunAfter>("""
                SELECT status, attempts, lease_id, heartbeat_at IS NOT NULL AS has_heartbeat, progress,
                       available_at <= now() AS available
                FROM transcode_run WHERE id = @RunId
                """, new { RunId = runId });

        public async Task<string> RunStatusAsync(Guid runId) =>
            await Connection.QuerySingleAsync<string>("SELECT status FROM transcode_run WHERE id = @RunId", new { RunId = runId });

        public async Task BackdateHeartbeatAsync(TimeSpan by) =>
            await Connection.ExecuteAsync(
                "UPDATE transcode_run SET heartbeat_at = heartbeat_at - @By WHERE status = 'running'", new { By = by });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
            }
            finally
            {
                await Connection.DisposeAsync();
                await _dataSource.DisposeAsync();
            }
        }
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
