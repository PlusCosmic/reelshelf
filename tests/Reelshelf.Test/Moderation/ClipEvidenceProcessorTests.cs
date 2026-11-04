using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Moderation;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Moderation;

public class ClipEvidenceProcessorTests
{
    private static readonly Guid ClipId = Guid.NewGuid();

    [Fact]
    public async Task APendingRecord_IsQuarantinedAndPurged_ThenCopied_ThenDeleted_InThatOrder()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal(
        [
            $"quarantine {record.Id} {ClipId}",
            $"purge {ClipId} v4",
            $"upload {record.Id}/record.json application/json",
            $"open quarantine {record.Id} clips/{ClipId}/v3/av1.mp4",
            $"upload {record.Id}/video.mp4 video/mp4",
            $"delete {ClipId} r2",
            $"delete quarantine {record.Id}"
        ], harness.Log);
        Assert.Equal((ClipEvidenceStatuses.Preserved, CdnPurgeResults.Purged), (record.Status, record.CdnPurge));
        Assert.Equal([$"{record.Id}/record.json", $"{record.Id}/video.mp4"], harness.Store.Files[record.Id]);
        Assert.Contains("\"owner\"", harness.Uploaded[$"{record.Id}/record.json"]);
        Assert.Equal("quarantined av1", harness.Uploaded[$"{record.Id}/video.mp4"]);
    }

    [Fact]
    public async Task WhileTheEvidenceBucketFails_TheClipIsQuarantinedAndPurged_ButNothingIsDeleted()
    {
        Harness harness = new() { FailUploads = int.MaxValue };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal([$"quarantine {record.Id} {ClipId}", $"purge {ClipId} v4"], harness.Log);
        Assert.Equal((ClipEvidenceStatuses.Pending, CdnPurgeResults.Purged), (record.Status, record.CdnPurge));
        Assert.Null(record.MediaDeletedAt);
        Assert.Equal("the evidence bucket is down", record.LastError);
    }

    [Fact]
    public async Task PreservingIsRetriedPastMaxAttempts_WithBackOff_AndTheQuarantineIsKeptUntilItWorks()
    {
        Harness harness = new(maxAttempts: 2) { FailUploads = int.MaxValue };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(ClipEvidenceStatuses.Pending, record.Status);
        await harness.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(ClipEvidenceStatuses.Failed, record.Status);
        for (int attempt = 3; attempt <= 8; attempt++)
        {
            Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
            Assert.Equal((ClipEvidenceStatuses.Failed, attempt), (record.Status, record.Attempts));
        }

        // Quarantined once; nothing deleted, the quarantine included.
        Assert.Equal([$"quarantine {record.Id} {ClipId}", $"purge {ClipId} v4"], harness.Log);
        Assert.Null(record.MediaDeletedAt);
        Assert.Equal([5, 10, 20, 40, 60, 60, 60, 60], harness.Store.Delays.Select(delay => delay.TotalMinutes));

        harness.FailUploads = 0;
        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal([$"delete {ClipId} r2", $"delete quarantine {record.Id}"], harness.Log[^2..]);
        Assert.Equal((ClipEvidenceStatuses.Preserved, (string?)null), (record.Status, record.LastError));
        Assert.False(await harness.Processor.ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ABunnyClip_IsRetriedWithoutLimit_AndStaysAtBunnyUntilPreserved()
    {
        Harness harness = new(maxAttempts: 1) { FailBunnyReads = 3 };
        ClipEvidence record = harness.Store.Add(Record(host: "bunny"));

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await harness.Processor.ProcessNextAsync(CancellationToken.None);
        }

        Assert.Equal(ClipEvidenceStatuses.Failed, record.Status);
        Assert.Equal("Bunny is down", record.LastError);
        Assert.DoesNotContain(harness.Log, line => line.StartsWith("delete", StringComparison.Ordinal));

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
        Assert.Equal("bunny video", harness.Uploaded[$"{record.Id}/video.mp4"]);
        Assert.Equal([$"delete {ClipId} bunny", $"delete quarantine {record.Id}"], harness.Log[^2..]);
    }

    [Fact]
    public async Task AnR2ClipThatNeverFinishedEncoding_KeepsItsUploadedSource()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record(state: "uploaded"));

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal([$"{record.Id}/record.json", $"{record.Id}/source"], harness.Store.Files[record.Id]);
        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
    }

    [Fact]
    public async Task AClipWithNoVideo_KeepsItsRecordAlone()
    {
        Harness harness = new() { HasVideo = false };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal([$"{record.Id}/record.json"], harness.Store.Files[record.Id]);
        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
    }

    [Fact]
    public async Task AStaleProcessor_WritesNothing_OnceItsClaimIsTakenOver()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        // Another processor claims the record while this one is deleting the video.
        harness.OnDelete = () => record.ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        // The deletes ran (the copy exists, and they are safe to repeat), but nothing is recorded or failed.
        Assert.Equal([$"delete {ClipId} r2", $"delete quarantine {record.Id}"], harness.Log[^2..]);
        Assert.Null(record.MediaDeletedAt);
        Assert.Null(record.LastError);
        Assert.Equal(ClipEvidenceStatuses.Pending, record.Status);
        Assert.NotNull(record.ClaimedAt);
    }

    [Fact]
    public async Task WithoutACloudflareToken_TheMissingPurgeIsRecorded()
    {
        Harness harness = new() { PurgeResult = CdnPurgeResults.NotConfigured };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal((ClipEvidenceStatuses.Preserved, CdnPurgeResults.NotConfigured), (record.Status, record.CdnPurge));
    }

    [Fact]
    public async Task APreservedRecord_IsSweptOnce_ByDeleting()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        record.Status = ClipEvidenceStatuses.Preserved;
        record.CdnPurge = CdnPurgeResults.Purged;
        harness.Store.SweepDue = true;

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal([$"sweep {ClipId}", $"purge {ClipId} v4"], harness.Log);
        Assert.False(await harness.Processor.ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARecordNotYetPreserved_IsSweptIntoTheQuarantine()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        record.Status = ClipEvidenceStatuses.Failed;
        record.CdnPurge = CdnPurgeResults.Purged;
        record.ClaimedAt = DateTimeOffset.UnixEpoch; // waiting out its back-off, so only the sweep is due
        harness.Store.SweepDue = true;
        harness.Store.IgnoreClaimForSweep = true;

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal([$"quarantine {record.Id} {ClipId}", $"purge {ClipId} v4"], harness.Log);
    }

    [Fact]
    public async Task EvidencePastItsRetention_IsDeletedWithItsFiles()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        harness.Store.Expired.Add(record.Id);

        await harness.Processor.ExpireAsync();

        Assert.Equal([$"delete prefix {record.Id}/", $"delete quarantine {record.Id}"], harness.Log);
        Assert.Empty(harness.Store.Records);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 10)]
    [InlineData(6, 20)]
    [InlineData(8, 60)]
    [InlineData(1000, 60)]
    public void TheRetryDelay_DoublesPastMaxAttempts_UpToTheCap(int attempts, double minutes)
    {
        Assert.Equal(minutes, ClipEvidenceProcessor.RetryDelay(attempts, new ModerationOptions()).TotalMinutes);
    }

    private static ClipEvidence Record(string host = "r2", string state = "ready") => new()
    {
        Id = Guid.NewGuid(),
        ClipId = ClipId,
        Reason = ContentReportReasons.ChildSafety,
        Note = "reported to the IWF",
        RemovedAt = DateTimeOffset.UtcNow,
        Clip = $$"""{"id":"{{ClipId}}","title":"clip","video_host":"{{host}}","video_id":"{{ClipId}}","video_version":3,"video_state":"{{state}}","created_at":"2026-10-26T12:00:00+00:00"}""",
        Owner = """{"id":"00000000-0000-0000-0000-000000000001","username":"owner","identities":[]}""",
        DeleteAfter = DateTimeOffset.UtcNow.AddDays(365)
    };

    private sealed class Harness
        : IEvidenceStorage, IEvidenceQuarantine, IExportVideoSource, IEvidenceMediaDeleter, ICdnPurger
    {
        public Harness(int maxAttempts = 5)
        {
            Processor = new ClipEvidenceProcessor(Store, this, this, this, this, this,
                new StaticOptions(new ModerationOptions { EvidenceMaxAttempts = maxAttempts }),
                NullLogger<ClipEvidenceProcessor>.Instance);
        }

        public FakeStore Store { get; } = new();
        public ClipEvidenceProcessor Processor { get; }
        public List<string> Log { get; } = [];
        public Dictionary<string, string> Uploaded { get; } = [];
        public bool HasVideo { get; init; } = true;
        public int FailUploads { get; set; }
        public int FailBunnyReads { get; set; }
        public Action? OnDelete { get; set; }
        public string PurgeResult { get; init; } = CdnPurgeResults.Purged;

        public bool IsConfigured => true;

        public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            if (FailUploads-- > 0)
            {
                throw new HttpRequestException("the evidence bucket is down");
            }

            using StreamReader reader = new(content);
            Uploaded[key] = await reader.ReadToEndAsync(cancellationToken);
            Log.Add($"upload {key} {contentType}");
        }

        public Task DeletePrefixAsync(string prefix)
        {
            Log.Add($"delete prefix {prefix}");
            return Task.CompletedTask;
        }

        public Task QuarantineAsync(Guid evidenceId, Guid clipId, CancellationToken cancellationToken)
        {
            Log.Add($"quarantine {evidenceId} {clipId}");
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenAsync(Guid evidenceId, string originalKey, CancellationToken cancellationToken)
        {
            Log.Add($"open quarantine {evidenceId} {originalKey}");
            string? content = !HasVideo ? null
                : originalKey.EndsWith("av1.mp4", StringComparison.Ordinal) ? "quarantined av1"
                : originalKey.StartsWith("uploads/", StringComparison.Ordinal) ? "quarantined source"
                : null;
            return Task.FromResult<Stream?>(content is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }

        Task IEvidenceQuarantine.DeleteAsync(Guid evidenceId)
        {
            Log.Add($"delete quarantine {evidenceId}");
            return Task.CompletedTask;
        }

        public Task<ExportVideo?> OpenAsync(ExportClipRow clip, CancellationToken cancellationToken)
        {
            if (FailBunnyReads-- > 0)
            {
                throw new HttpRequestException("Bunny is down");
            }

            return Task.FromResult(HasVideo
                ? new ExportVideo(new MemoryStream(Encoding.UTF8.GetBytes("bunny video")))
                : null);
        }

        public Task DeleteAsync(EvidenceClip clip)
        {
            Log.Add($"delete {clip.Id} {clip.VideoHost}");
            OnDelete?.Invoke();
            return Task.CompletedTask;
        }

        public Task SweepAsync(Guid clipId)
        {
            Log.Add($"sweep {clipId}");
            return Task.CompletedTask;
        }

        public Task<string> PurgeClipAsync(Guid clipId, int maxVersion, CancellationToken cancellationToken)
        {
            Log.Add($"purge {clipId} v{maxVersion}");
            return Task.FromResult(PurgeResult);
        }
    }

    /// <summary>
    /// Keeps the records in memory with the statements' rules, less the time-based ones (retry delays and staleness).
    /// </summary>
    private sealed class FakeStore : IClipEvidenceStore
    {
        private long _clock;

        public List<ClipEvidence> Records { get; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> Files { get; } = [];
        public List<Guid> Expired { get; } = [];
        public List<TimeSpan> Delays { get; } = [];
        public bool SweepDue { get; set; }

        public ClipEvidence Add(ClipEvidence record)
        {
            Records.Add(record);
            return record;
        }

        public Task<ClipEvidence?> Claim(TimeSpan staleAfter)
        {
            ClipEvidence? record = Records.FirstOrDefault(r =>
                r.Status is ClipEvidenceStatuses.Pending or ClipEvidenceStatuses.Failed && r.ClaimedAt is null);
            if (record is not null)
            {
                record.Attempts++;
                record.ClaimedAt = Now();
            }

            return Task.FromResult(record);
        }

        public Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter)
        {
            ClipEvidence? record = SweepDue
                ? Records.FirstOrDefault(r => r.CdnPurge is not null && (r.ClaimedAt is null || IgnoreClaimForSweep))
                : null;
            if (record is not null)
            {
                record.ClaimedAt = Now();
            }

            return Task.FromResult(record);
        }

        /// <summary>Stands in for a record whose back-off keeps it from <see cref="Claim"/> but not from the sweep.</summary>
        public bool IgnoreClaimForSweep { get; set; }

        public Task<bool> SaveCdnPurge(ClipEvidenceClaim claim, string result) =>
            Write(claim, record => record.CdnPurge = result);

        public Task<bool> SaveFiles(ClipEvidenceClaim claim, IReadOnlyList<string> keys) => Write(claim, record =>
        {
            Files[record.Id] = keys;
            record.Files = string.Join(",", keys);
        });

        public Task<bool> MarkMediaDeleted(ClipEvidenceClaim claim) =>
            Write(claim, record => record.MediaDeletedAt = DateTimeOffset.UtcNow);

        public Task<bool> MarkPreserved(ClipEvidenceClaim claim) => Write(claim, record =>
        {
            Assert.NotNull(record.Files);
            Assert.NotNull(record.MediaDeletedAt);
            record.Status = ClipEvidenceStatuses.Preserved;
            record.ClaimedAt = null;
            record.LastError = null;
        });

        public Task<bool> MarkSwept(ClipEvidenceClaim claim) => Write(claim, record =>
        {
            SweepDue = false;
            record.ClaimedAt = null;
        });

        public Task<bool> Fail(ClipEvidenceClaim claim, string error, TimeSpan retryAfter, bool needsAttention = false) =>
            Write(claim, record =>
            {
                record.LastError = error;
                record.ClaimedAt = null;
                Delays.Add(retryAfter);
                if (needsAttention && record.Status == ClipEvidenceStatuses.Pending)
                {
                    record.Status = ClipEvidenceStatuses.Failed;
                }
            });

        public Task<List<Guid>> GetExpired(int limit) => Task.FromResult(Expired.ToList());

        public Task Delete(Guid id)
        {
            Records.RemoveAll(record => record.Id == id);
            return Task.CompletedTask;
        }

        private DateTimeOffset Now() => DateTimeOffset.UnixEpoch.AddTicks(++_clock);

        private Task<bool> Write(ClipEvidenceClaim claim, Action<ClipEvidence> write)
        {
            ClipEvidence record = Records.Single(r => r.Id == claim.Id);
            if (record.ClaimedAt != claim.ClaimedAt)
            {
                return Task.FromResult(false);
            }

            write(record);
            return Task.FromResult(true);
        }
    }

    private sealed class StaticOptions(ModerationOptions value) : IOptionsMonitor<ModerationOptions>
    {
        public ModerationOptions CurrentValue => value;
        public ModerationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ModerationOptions, string?> listener) => null;
    }
}
