using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Billing;
using Reelshelf.Email;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class OverLimitSweeperTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-01T09:00:00Z");

    private sealed class World
    {
        public Clock Time { get; } = new();
        public FakeAccounts Accounts { get; }
        public FakePlans Plans { get; } = new();
        public FakeExports Exports { get; } = new();
        public FakeDeleter Deleter { get; } = new();
        public FakeSender Sender { get; } = new();

        public World()
        {
            Accounts = new FakeAccounts(Time);
            Deleter.Accounts = Accounts;
        }

        public OverLimitSweeper Sweeper => new(Accounts, Plans, Exports, Deleter, Sender,
            new FixedOptions(new PlanOptions()),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["FrontendOrigin"] = "https://reelshelf.test" }).Build(),
            Time, NullLogger<OverLimitSweeper>.Instance);

        /// <summary>A Free account with <paramref name="count"/> clips, a day apart, the newest last.</summary>
        public void HasClips(int count, AccountTier tier = AccountTier.Free)
        {
            Accounts.Clips = Enumerable.Range(0, count)
                .Select(day => new OwnedClip(Guid.NewGuid(), $"clip {day}", Start.AddDays(-count + day), null, "Apex"))
                .ToList();
            Plans.Tier = tier;
            Plans.Count = () => Accounts.Clips.Count;
        }

        public Task SweepAsync() => Sweeper.SweepAccountAsync(UserId, CancellationToken.None);
    }

    [Fact]
    public async Task WithinTheLimit_NothingHappens()
    {
        World world = new();
        world.HasClips(100);

        await world.SweepAsync();

        Assert.Null(world.Accounts.Since);
        Assert.Empty(world.Sender.Sent);
    }

    [Fact]
    public async Task GoingOver_StartsTheGrace_AndMailsTheOwnerOnce()
    {
        World world = new();
        world.HasClips(130);

        await world.SweepAsync();
        await world.SweepAsync();

        Assert.Equal(Start, world.Accounts.Since);
        EmailMessage mail = Assert.Single(world.Sender.Sent);
        Assert.Contains("30 clips are over its limit", mail.Text);
        Assert.Contains("15 November 2026", mail.Text);
        Assert.Contains("https://reelshelf.test/plan", mail.Text);
    }

    [Fact]
    public async Task ThreeDaysBeforeTheEnd_TheOwnerIsWarnedOnce()
    {
        World world = new();
        world.HasClips(130);
        await world.SweepAsync();

        world.Time.Advance(TimeSpan.FromDays(10));
        await world.SweepAsync();
        Assert.Single(world.Sender.Sent);

        world.Time.Advance(TimeSpan.FromDays(1));
        await world.SweepAsync();
        await world.SweepAsync();
        Assert.Equal(2, world.Sender.Sent.Count);
        Assert.Contains("will be deleted on 15 November 2026", world.Sender.Sent[1].Subject);
    }

    [Fact]
    public async Task WhenTheGraceEnds_TheClipsOverAreExported_ThenDeletedOnceTheExportIsReady()
    {
        World world = new();
        world.HasClips(103);
        List<Guid> oldest = world.Accounts.Clips.Take(3).Select(clip => clip.Id).ToList();
        await world.SweepAsync();
        world.Time.Advance(TimeSpan.FromDays(14));

        await world.SweepAsync();
        ClipExport export = Assert.Single(world.Exports.All);
        Assert.Equal(ClipExportReasons.GraceEnded, export.Reason);
        Assert.Equal(oldest.Order(), export.ClipIds.Order());
        Assert.Empty(world.Deleter.Deleted);

        // Still being made: nothing is deleted, and no second export starts.
        await world.SweepAsync();
        Assert.Single(world.Exports.All);
        Assert.Empty(world.Deleter.Deleted);

        world.Exports.SetStatus(export.Id, ClipExportStatuses.Ready);
        await world.SweepAsync();
        Assert.Equal(oldest.Order(), world.Deleter.Deleted.Order());
        Assert.Null(world.Accounts.Since);
    }

    [Fact]
    public async Task AFailedExport_DeletesNothing()
    {
        World world = new();
        world.HasClips(101);
        await world.SweepAsync();
        world.Time.Advance(TimeSpan.FromDays(15));
        await world.SweepAsync();
        world.Exports.SetStatus(world.Exports.All[0].Id, ClipExportStatuses.Failed);

        await world.SweepAsync();

        Assert.Empty(world.Deleter.Deleted);
        Assert.Single(world.Exports.All);
        Assert.NotNull(world.Accounts.Since);
    }

    [Fact]
    public async Task UpgradingDuringTheGrace_EndsIt_AndNothingIsDeleted()
    {
        World world = new();
        world.HasClips(130);
        await world.SweepAsync();

        world.Plans.Tier = AccountTier.Plus;
        world.Time.Advance(TimeSpan.FromDays(20));
        await world.SweepAsync();

        Assert.Null(world.Accounts.Since);
        Assert.Empty(world.Exports.All);
        Assert.Empty(world.Deleter.Deleted);
    }

    [Fact]
    public async Task AClipPickedAfterTheExport_IsKept_AndTheOneItDisplacedIsExportedNext()
    {
        World world = new();
        world.HasClips(101);
        OwnedClip oldest = world.Accounts.Clips[0];
        OwnedClip secondOldest = world.Accounts.Clips[1];
        await world.SweepAsync();
        world.Time.Advance(TimeSpan.FromDays(14));
        await world.SweepAsync();
        world.Exports.SetStatus(world.Exports.All[0].Id, ClipExportStatuses.Ready);

        // The owner picks the oldest clip after it was exported; the next oldest is over the limit instead.
        world.Accounts.Clips[0] = oldest with { KeptAt = world.Time.GetUtcNow() };
        await world.SweepAsync();
        Assert.Empty(world.Deleter.Deleted);
        Assert.Equal([secondOldest.Id], world.Exports.All[^1].ClipIds);

        world.Exports.SetStatus(world.Exports.All[^1].Id, ClipExportStatuses.Ready);
        await world.SweepAsync();
        Assert.Equal([secondOldest.Id], world.Deleter.Deleted);
    }

    internal sealed class FakeAccounts(Clock time) : IPlanEnforcementStore
    {
        public List<OwnedClip> Clips { get; set; } = [];
        public DateTimeOffset? Since { get; private set; }
        public DateTimeOffset? WarnedAt { get; private set; }

        public Task<List<Guid>> GetAccountsToCheck(int smallestLimit) => Task.FromResult(new List<Guid> { UserId });

        public Task<OverLimitState> GetState(Guid userId) =>
            Task.FromResult(new OverLimitState("harry@example.com", Since, WarnedAt));

        public Task<bool> StartGrace(Guid userId)
        {
            if (Since is not null)
            {
                return Task.FromResult(false);
            }

            Since = Now;
            WarnedAt = null;
            return Task.FromResult(true);
        }

        public Task EndGrace(Guid userId)
        {
            Since = null;
            WarnedAt = null;
            return Task.CompletedTask;
        }

        public Task<bool> MarkGraceWarned(Guid userId)
        {
            if (WarnedAt is not null)
            {
                return Task.FromResult(false);
            }

            WarnedAt = Now;
            return Task.FromResult(true);
        }

        public Task<List<OwnedClip>> GetOwnedClips(Guid userId) => Task.FromResult(Clips.ToList());

        public Task<bool> SetKept(Guid userId, Guid clipId, bool keep) => throw new NotSupportedException();

        private DateTimeOffset Now => time.GetUtcNow();
    }

    internal sealed class FakePlans : IAccountPlans
    {
        public AccountTier Tier { get; set; } = AccountTier.Free;
        public Func<int> Count { get; set; } = () => 0;

        public Task<AccountPlan> GetPlan(Guid userId) =>
            Task.FromResult(new AccountPlan(Tier, TierLimits.For(Tier), Count(), 0, 0));
    }

    internal sealed class FakeExports : IClipExportStore
    {
        public List<ClipExport> All { get; } = [];
        public void SetStatus(Guid id, string status)
        {
            int index = All.FindIndex(export => export.Id == id);
            All[index] = All[index] with { Status = status };
        }

        public Task<Guid> Create(Guid userId, string reason, IReadOnlyCollection<Guid> clipIds)
        {
            ClipExport export = new(Guid.NewGuid(), userId, reason, ClipExportStatuses.Pending, clipIds.ToList(), [], 0,
                null, Start.AddDays(14).AddSeconds(All.Count), null, null);
            All.Add(export);
            return Task.FromResult(export.Id);
        }

        public Task<List<ClipExport>> GetForUser(Guid userId) =>
            Task.FromResult(All.Where(export => export.UserId == userId).ToList());

        public Task<ClipExport?> Get(Guid exportId) => Task.FromResult(All.FirstOrDefault(export => export.Id == exportId));
        public Task<ClipExport?> Claim(TimeSpan staleAfter) => throw new NotSupportedException();
        public Task Heartbeat(Guid exportId) => throw new NotSupportedException();
        public Task Complete(Guid exportId, IReadOnlyList<ClipExportPart> parts, DateTimeOffset expiresAt) =>
            throw new NotSupportedException();
        public Task Fail(Guid exportId, string error, TimeSpan? retryAfter) => throw new NotSupportedException();
        public Task<List<ClipExport>> ExpireDue() => throw new NotSupportedException();
        public Task<List<ExportClipRow>> GetExportClips(Guid userId, IReadOnlyCollection<Guid> clipIds) =>
            throw new NotSupportedException();
    }

    internal sealed class FakeDeleter : IOwnedClipDeleter
    {
        public List<Guid> Deleted { get; } = [];
        public FakeAccounts? Accounts { get; set; }

        public Task<bool> DeleteAsync(Guid clipId, Guid ownerId)
        {
            Deleted.Add(clipId);
            Accounts?.Clips.RemoveAll(clip => clip.Id == clipId);
            return Task.FromResult(true);
        }
    }

    internal sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = Start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    internal sealed class FixedOptions(PlanOptions value) : Microsoft.Extensions.Options.IOptionsMonitor<PlanOptions>
    {
        public PlanOptions CurrentValue => value;
        public PlanOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PlanOptions, string?> listener) => null;
    }
}
