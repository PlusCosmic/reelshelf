using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Email;
using Reelshelf.Storage;
using Xunit;

namespace Reelshelf.Test.Storage;

public class StorageWarningServiceTests
{
    private const long Limit = StorageQuota.DefaultLimitBytes;
    private static readonly Guid UserId = Guid.NewGuid();

    private static Task Evaluate(StorageWarningService service, FakeStore store, StorageQuota quota)
    {
        store.LiveUsageBytes = quota.UsedBytes;
        return service.Evaluate(UserId, quota);
    }

    private static (StorageWarningService Service, FakeStore Store, FakeSender Sender) Build(string? email = "harry@example.com")
    {
        FakeStore store = new(email);
        FakeSender sender = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendOrigin"] = "https://clips.example" })
            .Build();
        StorageWarningService service = new(store, sender, configuration, NullLogger<StorageWarningService>.Instance);
        return (service, store, sender);
    }

    [Fact]
    public async Task WarnsOnceWhenCrossingTheThreshold()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, new StorageQuota(Limit * 89 / 100, Limit));
        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, new StorageQuota(Limit * 90 / 100, Limit));
        EmailMessage message = Assert.Single(sender.Sent);
        Assert.Equal("harry@example.com", message.To);
        Assert.Contains("nearly full", message.Subject);
        Assert.NotNull(store.WarnedAt);

        await Evaluate(service, store, new StorageQuota(Limit * 95 / 100, Limit));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task WarnsAgainAfterDroppingBelowAndRecrossing()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, new StorageQuota(Limit, Limit));
        await Evaluate(service, store, new StorageQuota(Limit / 2, Limit));
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, new StorageQuota(Limit * 91 / 100, Limit));
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task ConcurrentCrossing_SendsOnlyForTheClaimant()
    {
        var (service, store, sender) = Build();
        StorageQuota over = new(Limit * 92 / 100, Limit);

        // Both requests read a clean state before either claims the marker.
        store.LiveUsageBytes = over.UsedBytes;
        await Task.WhenAll(service.Evaluate(UserId, over), service.Evaluate(UserId, over));

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task StaleOverThresholdSnapshot_DoesNotSetTheMarkerOnceUsageDropped()
    {
        var (service, store, sender) = Build();

        // An upload observed 95% but a concurrent deletion has since brought live usage down to 50%.
        store.LiveUsageBytes = Limit / 2;
        await service.Evaluate(UserId, new StorageQuota(Limit * 95 / 100, Limit));

        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);

        await Evaluate(service, store, new StorageQuota(Limit * 95 / 100, Limit));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task UnlimitedTier_NeverWarns()
    {
        var (service, store, sender) = Build();

        await Evaluate(service, store, new StorageQuota(500L * 1024 * 1024 * 1024, null));

        Assert.Empty(sender.Sent);
        Assert.Null(store.WarnedAt);
    }

    [Fact]
    public async Task NoEmail_StillMarksSoTheCrossingIsNotRepeatedLater()
    {
        var (service, store, sender) = Build(email: null);

        await Evaluate(service, store, new StorageQuota(Limit, Limit));

        Assert.Empty(sender.Sent);
        Assert.NotNull(store.WarnedAt);
    }

    private sealed class FakeStore(string? email) : IStorageWarningStore
    {
        public DateTimeOffset? WarnedAt { get; private set; }

        public Task<StorageWarningState> GetState(Guid userId) =>
            Task.FromResult(new StorageWarningState(email, WarnedAt));

        /// <summary>What the database would see at the moment the marker statement runs.</summary>
        public long LiveUsageBytes { get; set; }

        public Task<bool> MarkWarned(Guid userId, long thresholdBytes)
        {
            if (WarnedAt is not null || LiveUsageBytes < thresholdBytes)
            {
                return Task.FromResult(false);
            }

            WarnedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }

        public Task ClearWarned(Guid userId, long thresholdBytes)
        {
            if (LiveUsageBytes < thresholdBytes)
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
