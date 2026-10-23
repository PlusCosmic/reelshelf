using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reelshelf.Billing;
using EventUtility = Stripe.EventUtility;
using Xunit;

namespace Reelshelf.Test.Billing;

/// <summary>The webhook as mapped, served by Kestrel on a loopback port with a fake Stripe behind it.</summary>
public class BillingWebhookEndpointTests
{
    private const string WebhookSecret = "whsec_test_secret";

    private static readonly Guid UserId = Guid.NewGuid();

    private static string Payload(string type = "customer.subscription.updated") => $$$$"""
        {"id":"evt_1","type":"{{{{type}}}}","data":{"object":{"id":"sub_1","object":"subscription","customer":"cus_1"}}}
        """;

    private static string Sign(string payload, string secret = WebhookSecret)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = EventUtility.ComputeSignature(secret, timestamp.ToString(), payload);
        return $"t={timestamp},v1={signature}";
    }

    [Fact]
    public async Task ASignedEvent_RecordsTheSubscription()
    {
        await using Api api = await Api.StartAsync();
        string payload = Payload();

        HttpResponseMessage response = await api.PostAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", Assert.Single(api.Store.Subscriptions).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("t=1,v1=nonsense")]
    public async Task AnUnsignedOrBadlySignedEvent_IsRefused(string? signature)
    {
        await using Api api = await Api.StartAsync();

        HttpResponseMessage response = await api.PostAsync(Payload(), signature);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.Store.Subscriptions);
    }

    [Fact]
    public async Task AnEventSignedWithAnotherSecret_IsRefused()
    {
        await using Api api = await Api.StartAsync();
        string payload = Payload();

        HttpResponseMessage response = await api.PostAsync(payload, Sign(payload, "whsec_someone_else"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WhenBillingIsOff_TheWebhookIsNotThere()
    {
        await using Api api = await Api.StartAsync(billingOn: false);
        string payload = Payload();

        HttpResponseMessage response = await api.PostAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class Api : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Api(WebApplication app, HttpClient client, FakeStore store)
        {
            _app = app;
            _client = client;
            Store = store;
        }

        public FakeStore Store { get; }

        public static async Task<Api> StartAsync(bool billingOn = true)
        {
            FakeStripe stripe = new();
            stripe.Subscriptions["sub_1"] = new StripeSubscriptionState("sub_1", "cus_1", "active", "plus_monthly",
                null, false, null, null, null);
            FakeStore store = new();
            store.Users.Add(UserId);
            store.Customers[UserId] = "cus_1";

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddAuthorization();
            if (billingOn)
            {
                BillingOptions options = new() { SecretKey = "rk_test", WebhookSecret = WebhookSecret };
                builder.Services.AddSingleton(options);
                builder.Services.AddSingleton(new BillingService(stripe, store, options,
                    new MemoryCache(new MemoryCacheOptions()), NullLogger<BillingService>.Instance));
            }

            WebApplication app = builder.Build();
            app.MapGroup("/api").MapBillingEndpoints();
            await app.StartAsync();

            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            return new Api(app, new HttpClient { BaseAddress = new Uri(address) }, store);
        }

        public async Task<HttpResponseMessage> PostAsync(string payload, string? signature)
        {
            HttpRequestMessage request = new(HttpMethod.Post, "/api/billing/webhook")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            if (signature is not null)
            {
                request.Headers.Add("Stripe-Signature", signature);
            }

            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
