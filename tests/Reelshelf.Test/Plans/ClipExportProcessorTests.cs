using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class ClipExportProcessorTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (ClipExportProcessor Processor, FakeExportQueue Queue, ClipExportBuilderTests.FakeStorage Storage,
        ClipExportBuilderTests.FakeVideos Videos, OverLimitSweeperTests.FakeSender Sender) Build(string reason)
    {
        FakeExportQueue queue = new(reason);
        ClipExportBuilderTests.FakeStorage storage = new();
        ClipExportBuilderTests.FakeVideos videos = new();
        OverLimitSweeperTests.FakeSender sender = new();
        OverLimitSweeperTests.FakeAccounts accounts = new(new OverLimitSweeperTests.Clock());
        ClipExportProcessor processor = new(queue,
            new ClipExportBuilder(videos, storage, NullLogger<ClipExportBuilder>.Instance), storage, accounts, sender,
            new OverLimitSweeperTests.FixedOptions(new PlanOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["FrontendOrigin"] = "https://reelshelf.test" }).Build(),
            TimeProvider.System, NullLogger<ClipExportProcessor>.Instance);
        return (processor, queue, storage, videos, sender);
    }

    [Fact]
    public async Task AGraceExport_IsMadeMarkedReadyAndMailed()
    {
        var (processor, queue, storage, videos, sender) = Build(ClipExportReasons.GraceEnded);
        videos.Bytes[queue.Clip.Id] = new byte[2_000];

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        ClipExportPart part = Assert.Single(queue.CompletedParts!);
        Assert.Equal(1, part.ClipCount);
        Assert.Single(storage.Objects);
        Assert.True(queue.ExpiresAt > DateTimeOffset.UtcNow.AddDays(27));
        Assert.Contains("deleted from Reelshelf", Assert.Single(sender.Sent).Text);
        Assert.False(await processor.ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailedAttempt_IsRetried_UntilItRunsOutOfAttempts()
    {
        var (processor, queue, storage, videos, sender) = Build(ClipExportReasons.Requested);
        videos.Bytes[queue.Clip.Id] = new byte[200_000];
        storage.FailAfterBytes = 10;

        await processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(10), queue.RetryAfter);
        Assert.Empty(storage.Objects);

        queue.Attempts = new PlanOptions().ExportMaxAttempts;
        await processor.ProcessNextAsync(CancellationToken.None);
        Assert.True(queue.FailedForGood);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ARunWhoseLeaseIsGone_StopsAndDeletesOnlyItsOwnParts()
    {
        var (processor, queue, storage, videos, sender) = Build(ClipExportReasons.GraceEnded);
        videos.Bytes[queue.Clip.Id] = new byte[2_000];
        string otherRun = ExportParts.Key(queue.Current with { LeaseId = Guid.NewGuid() }, 1);
        storage.Objects[otherRun] = [1, 2, 3];
        queue.LeaseLost = true;

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        Assert.Null(queue.CompletedParts);
        Assert.False(queue.FailedForGood);
        Assert.Null(queue.RetryAfter);
        Assert.Empty(sender.Sent);
        Assert.Equal([otherRun], storage.Objects.Keys);
    }

    [Fact]
    public async Task EachRun_WritesUnderItsOwnLease()
    {
        var (processor, queue, storage, videos, _) = Build(ClipExportReasons.Requested);
        videos.Bytes[queue.Clip.Id] = new byte[2_000];

        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal([$"exports/{UserId}/{queue.Current.Id}/{queue.Current.LeaseId}/reelshelf-clips-1.zip"],
            storage.Objects.Keys);
    }

    internal sealed class FakeExportQueue(string reason) : IClipExportStore
    {
        public ExportClipRow Clip { get; } = new()
        {
            Id = Guid.NewGuid(), Title = "Triple", GameName = "Apex", CreatedAt = DateTimeOffset.UtcNow, StorageSize = 2_000,
            VideoState = "ready"
        };

        public int Attempts { get; set; } = 1;
        public bool Waiting { get; private set; } = true;
        public IReadOnlyList<ClipExportPart>? CompletedParts { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }
        public TimeSpan? RetryAfter { get; private set; }
        public bool FailedForGood { get; private set; }

        /// <summary>Another run has claimed the export: every heartbeat, completion and failure is refused.</summary>
        public bool LeaseLost { get; set; }

        public ClipExport Current { get; private set; } = new(Guid.NewGuid(), UserId, reason,
            ClipExportStatuses.Running, [], [], 1, null, DateTimeOffset.UtcNow, null, null, Guid.NewGuid());

        public Task<ClipExport?> Claim(TimeSpan staleAfter, int maxAttempts)
        {
            Current = Current with { ClipIds = [Clip.Id], Attempts = Attempts, LeaseId = Guid.NewGuid() };
            return Task.FromResult(Waiting ? Current : null);
        }

        public Task<List<ExportClipRow>> GetExportClips(Guid userId, IReadOnlyCollection<Guid> clipIds) =>
            Task.FromResult(new List<ExportClipRow> { Clip });

        private bool Holds(Guid leaseId) => !LeaseLost && leaseId == Current.LeaseId;

        public Task<bool> Heartbeat(Guid exportId, Guid leaseId) => Task.FromResult(Holds(leaseId));

        public Task<bool> Complete(Guid exportId, Guid leaseId, IReadOnlyList<ClipExportPart> parts,
            DateTimeOffset expiresAt)
        {
            if (!Holds(leaseId))
            {
                return Task.FromResult(false);
            }

            (CompletedParts, ExpiresAt, Waiting) = (parts, expiresAt, false);
            return Task.FromResult(true);
        }

        public Task<bool> Fail(Guid exportId, Guid leaseId, string error, TimeSpan? retryAfter)
        {
            if (!Holds(leaseId))
            {
                return Task.FromResult(false);
            }

            RetryAfter = retryAfter;
            FailedForGood = retryAfter is null;
            Waiting = !FailedForGood;
            return Task.FromResult(true);
        }

        public Task<Guid?> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds) =>
            throw new NotSupportedException();
        public Task<List<ClipExport>> GetForUser(Guid userId) => throw new NotSupportedException();
        public Task<ClipExport?> Get(Guid exportId) => throw new NotSupportedException();
        public Task<List<ClipExport>> ExpireDue() => Task.FromResult(new List<ClipExport>());
    }
}
