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
        Assert.Equal("Bunny is down", harness.Store.LastError);
        Assert.Null(record.MediaDeletedAt);

        harness.Log.Clear();
        await harness.Processor.ProcessNextAsync(CancellationToken.None);

        // The copy was saved before the delete failed, so it isn't made twice.
        Assert.Equal([$"delete {ClipId} r2", $"purge {ClipId} v4"], harness.Log);
        Assert.Equal(ClipEvidenceStatuses.Preserved, record.Status);
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
        public Harness()
        {
            Processor = new ClipEvidenceProcessor(Store, this, this, this, this,
                new StaticOptions(new ModerationOptions()), NullLogger<ClipEvidenceProcessor>.Instance);
        }

        public FakeStore Store { get; } = new();
        public ClipEvidenceProcessor Processor { get; }
        public List<string> Log { get; } = [];
        public Dictionary<string, string> Uploaded { get; } = [];
        public bool HasVideo { get; init; } = true;
        public int FailDeletes { get; set; }
        public string PurgeResult { get; init; } = CdnPurgeResults.Purged;

        public bool IsConfigured => true;

        public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
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

    private sealed class FakeStore : IClipEvidenceStore
    {
        public List<ClipEvidence> Records { get; } = [];
        public Dictionary<Guid, IReadOnlyList<string>> Files { get; } = [];
        public List<Guid> Expired { get; } = [];
        public string? LastError { get; private set; }
        public bool SweepDue { get; set; }

        public ClipEvidence Add(ClipEvidence record)
        {
            Records.Add(record);
            return record;
        }

        public Task<ClipEvidence?> Claim(TimeSpan staleAfter, int maxAttempts)
        {
            ClipEvidence? record = Records.FirstOrDefault(r => r.Status == ClipEvidenceStatuses.Pending);
            if (record is not null)
            {
                record.Attempts++;
            }

            return Task.FromResult(record);
        }

        public Task<ClipEvidence?> ClaimSweep(TimeSpan sweepAfter, TimeSpan staleAfter)
        {
            ClipEvidence? record = SweepDue
                ? Records.FirstOrDefault(r => r.Status == ClipEvidenceStatuses.Preserved)
                : null;
            return Task.FromResult(record);
        }

        public Task SaveFiles(Guid id, IReadOnlyList<string> keys)
        {
            Files[id] = keys;
            Find(id).Files = string.Join(",", keys);
            return Task.CompletedTask;
        }

        public Task MarkMediaDeleted(Guid id)
        {
            Find(id).MediaDeletedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task SaveCdnPurge(Guid id, string result)
        {
            Find(id).CdnPurge = result;
            return Task.CompletedTask;
        }

        public Task MarkPreserved(Guid id)
        {
            Find(id).Status = ClipEvidenceStatuses.Preserved;
            return Task.CompletedTask;
        }

        public Task MarkSwept(Guid id)
        {
            SweepDue = false;
            return Task.CompletedTask;
        }

        public Task Fail(Guid id, string error, TimeSpan retryAfter)
        {
            LastError = error;
            return Task.CompletedTask;
        }

        public Task<List<Guid>> GetExpired(int limit) => Task.FromResult(Expired.ToList());

        public Task Delete(Guid id)
        {
            Records.RemoveAll(record => record.Id == id);
            return Task.CompletedTask;
        }

        private ClipEvidence Find(Guid id) => Records.Single(record => record.Id == id);
    }

    private sealed class StaticOptions(ModerationOptions value) : IOptionsMonitor<ModerationOptions>
    {
        public ModerationOptions CurrentValue => value;
        public ModerationOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ModerationOptions, string?> listener) => null;
    }
}
