using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Billing;
using Reelshelf.Email;
using Reelshelf.Plans;
using Xunit;

namespace Reelshelf.Test.Plans;

public class ClipLimitWarningServiceTests
{
    private const int Limit = 1_000;
    private static readonly Guid UserId = Guid.NewGuid();

    private static AccountPlan Plan(int clipCount, AccountTier tier = AccountTier.Plus) =>
        new(tier, TierLimits.For(tier), clipCount, 0, 0);

    private static Task Evaluate(ClipLimitWarningService service, FakeStore store, AccountPlan plan)
    {
        store.LiveClipCount = plan.ClipCount;
        return service.Evaluate(UserId, plan);
    }

    private static (ClipLimitWarningService Service, FakeStore Store, FakeSender Sender) Build(string? email = "harry@example.com")
    {
        FakeStore store = new(email);
        FakeSender sender = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendOrigin"] = "https://clips.example" })
            .Build();
        ClipLimitWarningService service = new(store, sender, configuration, NullLogger<ClipLimitWarningService>.Instance);
        return (service, store, sender);
    }

    [Fact]
    public async Task WarnsOnceWhenCrossingTheThreshold()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, Plan(899));
        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, Plan(900));
        EmailMessage message = Assert.Single(sender.Sent);
        Assert.Equal("harry@example.com", message.To);
        Assert.Contains("nearly full", message.Subject);
        Assert.Contains("900 of the 1,000 clips", message.Text);
        Assert.NotNull(store.WarnedAt);

        await Evaluate(service, store, Plan(950));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task WarnsAgainAfterDroppingBelowAndRecrossing()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, Plan(Limit));
        await Evaluate(service, store, Plan(Limit / 2));
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, Plan(910));
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task ConcurrentCrossing_SendsOnlyForTheClaimant()
    {
        var (service, store, sender) = Build();
        AccountPlan over = Plan(920);

        // Both requests read a clean state before either claims the marker.
        store.LiveClipCount = over.ClipCount;
        await Task.WhenAll(service.Evaluate(UserId, over), service.Evaluate(UserId, over));

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task StaleOverThresholdSnapshot_DoesNotSetTheMarkerOnceUsageDropped()
    {
        var (service, store, sender) = Build();

        // An upload observed 95% but a concurrent deletion has since brought the live count down to 50%.
        store.LiveClipCount = Limit / 2;
        await service.Evaluate(UserId, Plan(950));

        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, Plan(950));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Unlimited_NeverWarns()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, Plan(50_000, AccountTier.Unlimited));

        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);
    }

    [Fact]
    public async Task NoEmail_StillMarksSoTheCrossingIsNotRepeatedLater()
    {
        var (service, store, sender) = Build(email: null);

        await Evaluate(service, store, Plan(Limit));

        Assert.Empty(sender.Sent);
        Assert.NotNull(store.WarnedAt);
    }

    private sealed class FakeStore(string? email) : IClipLimitWarningStore
    {
        public DateTimeOffset? WarnedAt { get; private set; }

        public Task<ClipLimitWarningState> GetState(Guid userId) =>
            Task.FromResult(new ClipLimitWarningState(email, WarnedAt));

        /// <summary>What the database would see at the moment the marker statement runs.</summary>
        public int LiveClipCount { get; set; }

        public Task<bool> MarkWarned(Guid userId, int thresholdClips)
        {
            if (WarnedAt is not null || LiveClipCount < thresholdClips)
            {
                return Task.FromResult(false);
            }

            WarnedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task ClearWarned(Guid userId, int thresholdClips)
        {
            if (LiveClipCount < thresholdClips)
            {
                WarnedAt = null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
