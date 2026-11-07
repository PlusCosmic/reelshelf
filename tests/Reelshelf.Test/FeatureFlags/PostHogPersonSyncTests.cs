using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
using Reelshelf.Test.Billing;
using Xunit;

namespace Reelshelf.Test.FeatureFlags;

public class PostHogPersonSyncTests
{
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();

    [Fact]
    public async Task TheFirstPassIdentifiesEveryAccountWithItsTier()
    {
        FakePersonStore store = new();
        store.Accounts.Add(new PostHogPersonAccount(Ada, UserRole.Admin, null));
        store.Accounts.Add(new PostHogPersonAccount(Ben, UserRole.Editor, AccountTier.Plus));
        FakeStore billing = new();
        billing.Subscriptions.Add(Subscription(Ben, AccountTier.Pro));
        FakePeople people = new();

        int sent = await Sync(store, billing, people).RunAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Dictionary<Guid, FeatureFlagSubject> identified = people.Identified.ToDictionary(subject => subject.AccountId);
        Assert.Equal(new FeatureFlagSubject(Ada, UserRole.Admin, AccountTier.Free, null), identified[Ada]);
        // The better of the granted tier and the subscription, as AccountTiers.Resolve has it.
        Assert.Equal(new FeatureFlagSubject(Ben, UserRole.Editor, AccountTier.Pro, AccountTier.Plus), identified[Ben]);
        Assert.Equal(2, store.Synced.Count);
    }

    [Fact]
    public async Task OnlyAccountsWhosePropertiesChangedAreSentAgain()
    {
        FakePersonStore store = new();
        store.Accounts.Add(new PostHogPersonAccount(Ada, UserRole.Admin, null));
        store.Accounts.Add(new PostHogPersonAccount(Ben, UserRole.Editor, null));
        FakePeople people = new();
        PostHogPersonSync sync = Sync(store, new FakeStore(), people);
        await sync.RunAsync(CancellationToken.None);
        people.Identified.Clear();

        store.Accounts[1] = new PostHogPersonAccount(Ben, UserRole.Editor, AccountTier.Unlimited);
        int sent = await sync.RunAsync(CancellationToken.None);

        Assert.Equal(1, sent);
        FeatureFlagSubject subject = Assert.Single(people.Identified);
        Assert.Equal(Ben, subject.AccountId);
        Assert.Equal(AccountTier.Unlimited, subject.Tier);
    }

    [Fact]
    public async Task AFailedIdentifyRecordsNothing()
    {
        FakePersonStore store = new();
        store.Accounts.Add(new PostHogPersonAccount(Ada, UserRole.Admin, null));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Sync(store, new FakeStore(), new FakePeople { Fail = true }).RunAsync(CancellationToken.None));

        Assert.Empty(store.Synced);
    }

    [Fact]
    public async Task TheClientSendsOneBatchOfIdentifyEventsWithTheProjectToken()
    {
        RecordingHandler handler = new(HttpStatusCode.OK);
        PostHogPeopleClient client = new(new HttpClient(handler), Settings());

        await client.IdentifyAsync([new FeatureFlagSubject(Ada, UserRole.Admin, AccountTier.Free, null)],
            CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://eu.i.posthog.com/batch/", request.RequestUri!.ToString());
        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("phc_x", body.RootElement.GetProperty("api_key").GetString());
        JsonElement identify = Assert.Single(body.RootElement.GetProperty("batch").EnumerateArray());
        Assert.Equal("$identify", identify.GetProperty("event").GetString());
        Assert.Equal(Ada.ToString(), identify.GetProperty("distinct_id").GetString());
        JsonElement set = identify.GetProperty("properties").GetProperty("$set");
        Assert.Equal("admin", set.GetProperty("role").GetString());
        Assert.Equal("free", set.GetProperty("tier").GetString());
        Assert.Equal("none", set.GetProperty("granted_tier").GetString());
    }

    [Fact]
    public async Task TheClientDeletesAPersonByDistinctIdWithThePersonalApiKey()
    {
        RecordingHandler handler = new(HttpStatusCode.Accepted);
        PostHogPeopleClient client = new(new HttpClient(handler), Settings());

        await client.DeleteAsync(Ada, CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://eu.posthog.com/api/projects/12345/persons/bulk_delete/", request.RequestUri!.ToString());
        Assert.Equal("Bearer phx_personal", request.Headers.Authorization!.ToString());
        using JsonDocument body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(Ada.ToString(), Assert.Single(body.RootElement.GetProperty("distinct_ids").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task TheClientThrowsWhenPostHogRefuses()
    {
        PostHogPeopleClient client = new(new HttpClient(new RecordingHandler(HttpStatusCode.Forbidden)), Settings());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteAsync(Ada, CancellationToken.None));
    }

    private static PostHogPersonSync Sync(IPostHogPersonStore store, IBillingStore billing, IPostHogPeople people) =>
        new(store, billing, people, NullLogger<PostHogPersonSync>.Instance);

    private static PostHogSettings Settings() => new("phc_x", "phx_y", "12345", "phx_personal", PostHogSettings.DefaultHostUrl,
        PostHogSettings.DefaultApiHostUrl, PostHogSettings.DefaultPersonSyncInterval);

    private static BillingSubscription Subscription(Guid userId, AccountTier tier) =>
        new($"sub_{userId:N}", userId, "active", "pro_monthly", tier, null, false, null, null);

    private sealed class FakePersonStore : IPostHogPersonStore
    {
        public List<PostHogPersonAccount> Accounts { get; } = [];
        public Dictionary<Guid, string> Synced { get; } = [];

        public Task<List<PostHogPersonAccount>> GetAccounts() => Task.FromResult(Accounts.ToList());

        public Task<Dictionary<Guid, string>> GetSynced() => Task.FromResult(new Dictionary<Guid, string>(Synced));

        public Task MarkSynced(IReadOnlyCollection<(Guid AccountId, string Properties)> synced)
        {
            foreach ((Guid accountId, string properties) in synced)
            {
                Synced[accountId] = properties;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakePeople : IPostHogPeople
    {
        public bool Fail { get; init; }
        public List<FeatureFlagSubject> Identified { get; } = [];

        public Task IdentifyAsync(IReadOnlyList<FeatureFlagSubject> subjects, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("PostHog is down");
            }

            Identified.AddRange(subjects);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}
