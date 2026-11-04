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
    public async Task APendingRecord_IsCopied_ThenDeleted_ThenPurged_InThatOrder()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal(
        [
            $"upload {record.Id}/record.json application/json",
            $"upload {record.Id}/video.mp4 video/mp4",
            $"delete {ClipId} r2",
            $"purge {ClipId} v4"
        ], harness.Log);
        Assert.Equal((ClipEvidenceStatuses.Preserved, CdnPurgeResults.Purged), (record.Status, record.CdnPurge));
        Assert.Equal([$"{record.Id}/record.json", $"{record.Id}/video.mp4"], harness.Store.Files[record.Id]);
        Assert.Contains("\"owner\"", harness.Uploaded[$"{record.Id}/record.json"]);
    }

    [Fact]
    public async Task AFailedRecord_IsRetriedFromWhereItStopped()
    {
        Harness harness = new() { FailDeletes = 1 };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(ClipEvidenceStatuses.Pending, record.Status);
        Assert.Equal("Bunny is down", record.LastError);
        Assert.Null(record.MediaDeletedAt);

        harness.Log.Clear();
        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        // The copy was saved before the delete failed, so it isn't made twice.
        Assert.Equal([$"delete {ClipId} r2", $"purge {ClipId} v4"], harness.Log);
        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
    }

    [Fact]
    public async Task ARecordOutOfAttempts_IsFailed_ButItsVideoIsStillDeletedAndPurged()
    {
        Harness harness = new(maxAttempts: 2) { FailUploads = int.MaxValue, FailDeletes = 1 };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);
        await harness.Processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal((ClipEvidenceStatuses.Pending, 2), (record.Status, record.Attempts));
        Assert.Empty(harness.Log);

        // Out of attempts: failed, and the takedown is claimed in the same pass. Its first delete fails too, and is
        // retried with no limit on attempts.
        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(ClipEvidenceStatuses.Failed, record.Status);
        Assert.Null(record.MediaDeletedAt);
        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal([$"delete {ClipId} r2", $"purge {ClipId} v4"], harness.Log);
        Assert.Equal((ClipEvidenceStatuses.Failed, CdnPurgeResults.Purged), (record.Status, record.CdnPurge));
        Assert.NotNull(record.MediaDeletedAt);
        Assert.Null(record.Files);
        Assert.Equal(2, record.Attempts);
        Assert.False(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        // It is swept like a preserved one.
        harness.Store.SweepDue = true;
        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal($"sweep {ClipId}", harness.Log[^2]);
    }

    [Fact]
    public async Task AStaleProcessor_WritesNothing_OnceItsClaimIsTakenOver()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        // Another processor claims the record while this one is deleting the video.
        harness.OnDelete = () => record.ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));

        Assert.Equal($"delete {ClipId} r2", harness.Log[^1]);
        Assert.Null(record.MediaDeletedAt);
        Assert.Null(record.LastError);
        Assert.Equal(ClipEvidenceStatuses.Pending, record.Status);
        Assert.NotNull(record.ClaimedAt);
    }

    [Fact]
    public async Task AClipWithNoFinishedVideo_KeepsItsRecordAlone()
    {
        Harness harness = new() { HasVideo = false };
        ClipEvidence record = harness.Store.Add(Record());

        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal([$"{record.Id}/record.json"], harness.Store.Files[record.Id]);
        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
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
    public async Task APreservedRecord_IsSweptOnce()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        record.Status = ClipEvidenceStatuses.Preserved;
        record.MediaDeletedAt = DateTimeOffset.UtcNow;
        record.CdnPurge = CdnPurgeResults.Purged;
        harness.Store.SweepDue = true;

        Assert.True(await harness.Processor.ProcessNextAsync(CancellationToken.None));
        Assert.Equal([$"sweep {ClipId}", $"purge {ClipId} v4"], harness.Log);
        Assert.False(await harness.Processor.ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EvidencePastItsRetention_IsDeletedWithItsFiles()
    {
        Harness harness = new();
        ClipEvidence record = harness.Store.Add(Record());
        harness.Store.Expired.Add(record.Id);

        await harness.Processor.ExpireAsync();

        Assert.Equal([$"delete prefix {record.Id}/"], harness.Log);
        Assert.Empty(harness.Store.Records);
    }

    private static ClipEvidence Record() => new()
    {
        Id = Guid.NewGuid(),
        ClipId = ClipId,
        Reason = ContentReportReasons.ChildSafety,
        Note = "reported to the IWF",
        RemovedAt = DateTimeOffset.UtcNow,
        Clip = $$"""{"id":"{{ClipId}}","title":"clip","video_host":"r2","video_id":"{{ClipId}}","video_version":3,"video_state":"ready","created_at":"2026-10-26T12:00:00+00:00"}""",
        Owner = """{"id":"00000000-0000-0000-0000-000000000001","username":"owner","identities":[]}""",
        DeleteAfter = DateTimeOffset.UtcNow.AddDays(365)
    };

    private sealed class Harness : IEvidenceStorage, IExportVideoSource, IEvidenceMediaDeleter, ICdnPurger
    {
        public Harness(int maxAttempts = 5)
        {
            Processor = new ClipEvidenceProcessor(Store, this, this, this, this,
                new StaticOptions(new ModerationOptions { EvidenceMaxAttempts = maxAttempts }),
                NullLogger<ClipEvidenceProcessor>.Instance);
        }

        public FakeStore Store { get; } = new();
        public ClipEvidenceProcessor Processor { get; }
        public List<string> Log { get; } = [];
        public Dictionary<string, string> Uploaded { get; } = [];
        public bool HasVideo { get; init; } = true;
        public int FailUploads { get; set; }
        public int FailDeletes { get; set; }
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

        public Task<ExportVideo?> OpenAsync(ExportClipRow clip, CancellationToken cancellationToken) =>
            Task.FromResult(HasVideo ? new ExportVideo(new MemoryStream(Encoding.UTF8.GetBytes("video"))) : null);

        public Task DeleteAsync(EvidenceClip clip)
        {
            if (FailDeletes-- > 0)
            {
                throw new HttpRequestException("Bunny is down");
            }

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
        public bool SweepDue { get; set; }

        public ClipEvidence Add(ClipEvidence record)
        {
            Records.Add(record);
            return record;
        }

        public Task<List<ClipEvidence>> FailExhausted(TimeSpan staleAfter, int maxAttempts)
        {
            List<ClipEvidence> failed = Records
                .Where(r => r.Status == ClipEvidenceStatuses.Pending && r.Attempts >= maxAttempts && r.ClaimedAt is null)
                .ToList();
            failed.ForEach(r => r.Status = ClipEvidenceStatuses.Failed);
            return Task.FromResult(failed);
        }

        public Task<ClipEvidence?> Claim(TimeSpan staleAfter, int maxAttempts)
        {
            ClipEvidence? record = Records.FirstOrDefault(r =>
                r.Status == ClipEvidenceStatuses.Pending && r.Attempts < maxAttempts && r.ClaimedAt is null);
            if (record is not null)
            {
                record.Attempts++;
                record.ClaimedAt = Now();
            }

            return Task.FromResult(record);
        }

        public Task<ClipEvidence?> ClaimTakedown(TimeSpan staleAfter)
        {
            ClipEvidence? record = Records.FirstOrDefault(r => r.Status == ClipEvidenceStatuses.Failed
                && (r.MediaDeletedAt is null || r.CdnPurge is null) && r.ClaimedAt is null);
            if (record is not null)
            {
                record.ClaimedAt = Now();
            }

            return Task.FromResult(record);
        }

        public Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter)
        {
            ClipEvidence? record = SweepDue
                ? Records.FirstOrDefault(r => r.Status != ClipEvidenceStatuses.Pending && r.MediaDeletedAt is not null
                                              && r.CdnPurge is not null && r.ClaimedAt is null)
                : null;
            if (record is not null)
            {
                record.ClaimedAt = Now();
            }

            return Task.FromResult(record);
        }

        public Task<bool> SaveFiles(ClipEvidenceClaim claim, IReadOnlyList<string> keys) => Write(claim, record =>
        {
            Files[record.Id] = keys;
            record.Files = string.Join(",", keys);
        });

        public Task<bool> MarkMediaDeleted(ClipEvidenceClaim claim) =>
            Write(claim, record => record.MediaDeletedAt = DateTimeOffset.UtcNow);

        public Task<bool> SaveCdnPurge(ClipEvidenceClaim claim, string result) =>
            Write(claim, record => record.CdnPurge = result);

        public Task<bool> MarkPreserved(ClipEvidenceClaim claim) => Write(claim, record =>
        {
            record.Status = ClipEvidenceStatuses.Preserved;
            record.ClaimedAt = null;
            record.LastError = null;
        });

        public Task<bool> MarkTakenDown(ClipEvidenceClaim claim) => Write(claim, record =>
        {
            record.Status = record.Files is null ? ClipEvidenceStatuses.Failed : ClipEvidenceStatuses.Preserved;
            record.ClaimedAt = null;
        });

        public Task<bool> MarkSwept(ClipEvidenceClaim claim) => Write(claim, record =>
        {
            SweepDue = false;
            record.ClaimedAt = null;
        });

        public Task<bool> Fail(ClipEvidenceClaim claim, string error, TimeSpan retryAfter) => Write(claim, record =>
        {
            record.LastError = error;
            record.ClaimedAt = null;
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
