using System.Net;
using System.Text;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reelshelf.Billing;
using Reelshelf.Ops.Costs;
using Stripe;
using Xunit;

namespace Reelshelf.Test.Ops;

/// <summary>
/// The Costs section's providers, each read through a fake of its HTTP API (AWS through the SDK and Stripe through
/// <see cref="StripeBilling"/> on a fake transport), and <see cref="OpsCosts"/>' statuses and refresh timing.
/// </summary>
public class OpsCostsTests
{
    /// <summary>Thursday 12 November 2026, 15:00 UTC: this month runs from 1 November, the 30 days from 14 October.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void TheWindowCoversLastMonthAndThe30Days()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), CostWindow.ReadStart(Today));
        Assert.Equal(new DateOnly(2026, 10, 14), CostWindow.DailyStart(Today));
        // Early in a month after a short one, the 30 days reach back past last month's first.
        Assert.Equal(new DateOnly(2026, 1, 31), CostWindow.ReadStart(new DateOnly(2026, 3, 1)));
        // Late in a month after a short one, last month's first comes first.
        Assert.Equal(new DateOnly(2026, 2, 1), CostWindow.ReadStart(new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void ABreakdownKeepsTheLargestAndAddsUpTheRest()
    {
        List<CostEntry> entries = Enumerable.Range(1, 15)
            .Select(i => new CostEntry(Today, $"service-{i:00}", i))
            .Append(new CostEntry(Today, "rounds to nothing", 0.001m))
            .ToList();
        List<OpsCostItem> items = CostWindow.Breakdown(entries);
        Assert.Equal(CostWindow.BreakdownSize, items.Count);
        Assert.Equal("service-15", items[0].Name);
        Assert.Equal(new OpsCostItem("Other", 1 + 2 + 3 + 4), items[^1]);
    }

    [Fact]
    public async Task Aws_ReadsCostExplorerDailyByService_InUsEast1_AcrossPages()
    {
        FakeHttp http = new(request =>
        {
            string body = request.Body!;
            return body.Contains("\"NextPageToken\":\"page-2\"")
                ? Json(CostExplorerPage(null, ("2026-11-12", "Amazon CloudFront", "0.25")))
                : Json(CostExplorerPage("page-2",
                    ("2026-10-02", "Amazon Simple Storage Service", "10.5"),
                    ("2026-11-01", "Amazon Simple Storage Service", "3.10"),
                    ("2026-11-01", "Amazon Elastic Container Service", "1.40"),
                    ("2026-11-11", "Tax", "0")));
        });
        AwsCostSource source = new(Options(new OpsCostsOptions
        {
            Aws = new OpsCostsAwsOptions { AccessKeyId = "AKIAEXAMPLE", SecretAccessKey = "secret" }
        }), new FakeAwsHttpClientFactory(http));

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal("USD", costs.Currency);
        Assert.Equal(4.75m, costs.MonthToDate);
        Assert.Equal(10.5m, costs.LastMonth);
        Assert.Equal(30, costs.Daily.Count);
        Assert.Equal(new OpsCostDay(new DateOnly(2026, 10, 14), 0m), costs.Daily[0]);
        Assert.Equal(new OpsCostDay(new DateOnly(2026, 11, 1), 4.5m),
            costs.Daily.Single(d => d.Date == new DateOnly(2026, 11, 1)));
        Assert.Equal(["Amazon Simple Storage Service", "Amazon Elastic Container Service", "Amazon CloudFront"],
            costs.Breakdown.Select(item => item.Name));

        Assert.Equal(2, http.Requests.Count);
        FakeRequest first = http.Requests[0];
        Assert.Equal("ce.us-east-1.amazonaws.com", first.Uri.Host);
        Assert.Equal("AWSInsightsIndexService.GetCostAndUsage", first.Headers["X-Amz-Target"]);
        Assert.Contains("/us-east-1/ce/aws4_request", first.Headers["Authorization"]);
        Assert.Contains("\"Start\":\"2026-10-01\"", first.Body);
        Assert.Contains("\"End\":\"2026-11-13\"", first.Body);
        Assert.Contains("\"Granularity\":\"DAILY\"", first.Body);
        Assert.Contains("\"Key\":\"SERVICE\"", first.Body);
        Assert.Contains("UnblendedCost", first.Body);
    }

    [Fact]
    public async Task Aws_WithHalfAKey_SaysSo_AndARefusalNamesItsCode()
    {
        AwsCostSource half = new(Options(new OpsCostsOptions { Aws = new OpsCostsAwsOptions { AccessKeyId = "AKIA" } }));
        Assert.True(half.IsConfigured);
        CostSourceException missing =
            await Assert.ThrowsAsync<CostSourceException>(() => half.ReadAsync(Today, CancellationToken.None));
        Assert.Contains("needs both", missing.Message);

        FakeHttp http = new(_ => (HttpStatusCode.BadRequest,
            "{\"__type\":\"AccessDeniedException\",\"message\":\"User is not authorized to perform: ce:GetCostAndUsage\"}"));
        AwsCostSource refused = new(Options(new OpsCostsOptions
        {
            Aws = new OpsCostsAwsOptions { AccessKeyId = "AKIAEXAMPLE", SecretAccessKey = "secret" }
        }), new FakeAwsHttpClientFactory(http));
        CostSourceException denied =
            await Assert.ThrowsAsync<CostSourceException>(() => refused.ReadAsync(Today, CancellationToken.None));
        Assert.Contains("AccessDeniedException", denied.Message);
        Assert.DoesNotContain("secret", denied.Message);
    }

    [Fact]
    public async Task OpenAi_ReadsTheCostsApiByLineItem_FollowingPages()
    {
        long oct2 = Unix(new DateOnly(2026, 10, 2));
        long nov3 = Unix(new DateOnly(2026, 11, 3));
        FakeHttp http = new(request => request.Uri.Query.Contains("page=page_2")
            ? Json($$"""
                     {"object":"page","has_more":false,"next_page":null,"data":[
                       {"object":"bucket","start_time":{{nov3}},"end_time":{{nov3 + 86400}},"results":[
                         {"object":"organization.costs.result","amount":{"value":0.5,"currency":"usd"},"line_item":"text-embedding-3-small, input","project_id":null}]}]}
                     """)
            : Json($$"""
                     {"object":"page","has_more":true,"next_page":"page_2","data":[
                       {"object":"bucket","start_time":{{oct2}},"end_time":{{oct2 + 86400}},"results":[
                         {"object":"organization.costs.result","amount":{"value":7.25,"currency":"usd"},"line_item":"gpt-5.5, input","project_id":"proj_1"}]},
                       {"object":"bucket","start_time":{{nov3}},"end_time":{{nov3 + 86400}},"results":[
                         {"object":"organization.costs.result","amount":{"value":"2.125","currency":"usd"},"line_item":"gpt-5.5, input","project_id":"proj_1"}]}]}
                     """));
        OpenAiCostSource source = new(Options(new OpsCostsOptions { OpenAi = new() { AdminKey = "sk-admin-x" } }),
            new FakeHttpClientFactory(http));

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal("USD", costs.Currency);
        Assert.Equal(2.625m, costs.MonthToDate);
        Assert.Equal(7.25m, costs.LastMonth);
        Assert.Equal("gpt-5.5, input", costs.Breakdown[0].Name);
        Assert.Equal(2, http.Requests.Count);
        FakeRequest first = http.Requests[0];
        Assert.Equal("/v1/organization/costs", first.Uri.AbsolutePath);
        Assert.Contains($"start_time={Unix(new DateOnly(2026, 10, 1))}", first.Uri.Query);
        Assert.Contains("bucket_width=1d", first.Uri.Query);
        Assert.Contains("group_by=line_item", first.Uri.Query);
        Assert.Equal("Bearer sk-admin-x", first.Headers["Authorization"]);
    }

    [Fact]
    public async Task Anthropic_ReadsTheCostReportInCents_ByModel()
    {
        FakeHttp http = new(request => request.Uri.Query.Contains("page=page_2")
            ? Json("""
                   {"data":[{"starting_at":"2026-11-12T00:00:00Z","ending_at":"2026-11-13T00:00:00Z","results":[
                     {"amount":"25","currency":"USD","description":"Web search","cost_type":"web_search","model":null}]}],
                    "has_more":false,"next_page":null}
                   """)
            : Json("""
                   {"data":[
                     {"starting_at":"2026-10-20T00:00:00Z","ending_at":"2026-10-21T00:00:00Z","results":[
                       {"amount":"12345.6","currency":"USD","description":"Claude Sonnet 5.5 Usage - Input Tokens","cost_type":"tokens","model":"claude-sonnet-5-5"}]},
                     {"starting_at":"2026-11-02T00:00:00Z","ending_at":"2026-11-03T00:00:00Z","results":[
                       {"amount":"150","currency":"USD","description":"Claude Sonnet 5.5 Usage - Output Tokens","cost_type":"tokens","model":"claude-sonnet-5-5"}]}],
                    "has_more":true,"next_page":"page_2"}
                   """));
        AnthropicCostSource source = new(
            Options(new OpsCostsOptions { Anthropic = new() { AdminKey = "sk-ant-admin01-x" } }),
            new FakeHttpClientFactory(http));

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal(1.75m, costs.MonthToDate);
        Assert.Equal(123.456m, costs.LastMonth);
        Assert.Equal(["claude-sonnet-5-5", "web_search"], costs.Breakdown.Select(item => item.Name));
        FakeRequest first = http.Requests[0];
        Assert.Equal("/v1/organizations/cost_report", first.Uri.AbsolutePath);
        Assert.Contains("starting_at=2026-10-01T00%3A00%3A00Z", first.Uri.Query);
        Assert.Contains("group_by[]=description", Uri.UnescapeDataString(first.Uri.Query));
        Assert.Equal("sk-ant-admin01-x", first.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", first.Headers["anthropic-version"]);
        Assert.Contains("page=page_2", http.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task OpenRouter_WithAManagementKey_ReadsActivityByModel_AndTodayFromTheAppsKeys()
    {
        FakeHttp http = new(request => request.Uri.AbsolutePath switch
        {
            "/api/v1/activity" => Json("""
                                       {"data":[
                                         {"date":"2026-10-20","model":"google/gemini-3.5-transcribe","usage":1.5,"requests":10},
                                         {"date":"2026-11-02","model":"google/gemini-3.5-transcribe","usage":2.0,"requests":10},
                                         {"date":"2026-11-02","model":"typesafe/jev-1.14","usage":0.75,"requests":3}]}
                                       """),
            "/api/v1/credits" => Json("""{"data":{"total_credits":100.5,"total_usage":25.75}}"""),
            "/api/v1/key" => Json(request.Headers["Authorization"] == "Bearer sk-or-transcribe"
                ? """{"data":{"label":"t","usage":40,"usage_daily":0.4,"usage_monthly":2.4}}"""
                : """{"data":{"label":"l","usage":9,"usage_daily":0.1,"usage_monthly":0.8}}"""),
            _ => (HttpStatusCode.NotFound, "{}")
        });
        IConfiguration configuration = Configuration(
            ("ClipTranscription:OpenRouterApiKey", "sk-or-transcribe"),
            ("LegendDetection:Providers:openrouter:ApiKey", "sk-or-legends"),
            // The same key twice counts once.
            ("ClipSummary:Providers:openrouter:ApiKey", "sk-or-legends"));
        OpenRouterCostSource source = new(
            Options(new OpsCostsOptions { OpenRouter = new() { ManagementKey = "sk-or-mgmt" } }), configuration,
            new FakeHttpClientFactory(http));

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal(2.75m + 0.5m, costs.MonthToDate);
        Assert.Null(costs.LastMonth);
        Assert.Equal(74.75m, costs.Balance);
        Assert.Equal(0.5m, costs.Daily.Single(d => d.Date == Today).Amount);
        Assert.Equal(["google/gemini-3.5-transcribe", "typesafe/jev-1.14"], costs.Breakdown.Select(item => item.Name));
        Assert.Equal(2, http.Requests.Count(r => r.Uri.AbsolutePath == "/api/v1/key"));
        Assert.All(http.Requests.Where(r => r.Uri.AbsolutePath != "/api/v1/key"),
            r => Assert.Equal("Bearer sk-or-mgmt", r.Headers["Authorization"]));
    }

    [Fact]
    public async Task OpenRouter_WithOnlyTheAppsKey_GivesThisMonthAndToday()
    {
        FakeHttp http = new(_ => Json("""{"data":{"usage":40,"usage_daily":0.4,"usage_monthly":2.4}}"""));
        OpenRouterCostSource source = new(Options(new OpsCostsOptions()),
            Configuration(("ClipTranscription:OpenRouterApiKey", "sk-or-transcribe")), new FakeHttpClientFactory(http));
        Assert.True(source.IsConfigured);

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal(2.4m, costs.MonthToDate);
        Assert.Null(costs.LastMonth);
        Assert.Equal([new OpsCostDay(Today, 0.4m)], costs.Daily);
        Assert.Empty(costs.Breakdown);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Stripe_AddsUpGrossAndNet_LeavingOutPayouts()
    {
        long oct = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long nov = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        FakeHttp http = new(_ => Json($$"""
                                       {"object":"list","url":"/v1/balance_transactions","has_more":false,"data":[
                                         {"id":"txn_1","object":"balance_transaction","amount":1000,"fee":59,"net":941,"currency":"gbp","created":{{oct}},"reporting_category":"charge","type":"charge","status":"available"},
                                         {"id":"txn_2","object":"balance_transaction","amount":-900,"fee":0,"net":-900,"currency":"gbp","created":{{oct}},"reporting_category":"payout","type":"payout","status":"available"},
                                         {"id":"txn_3","object":"balance_transaction","amount":500,"fee":32,"net":468,"currency":"gbp","created":{{nov}},"reporting_category":"charge","type":"charge","status":"pending"},
                                         {"id":"txn_4","object":"balance_transaction","amount":-50,"fee":0,"net":-50,"currency":"gbp","created":{{nov}},"reporting_category":"fee","type":"stripe_fee","status":"available"}]}
                                       """));
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IStripeBilling>(new StripeBilling(StripeClient(http)))
            .BuildServiceProvider();
        StripeRevenueSource source = new(services);
        Assert.True(source.IsConfigured);

        ProviderCosts costs = await source.ReadAsync(Today, CancellationToken.None);

        Assert.Equal("GBP", costs.Currency);
        Assert.Equal(new OpsRevenue(5m, 4.18m, 10m, 9.41m), costs.Revenue);
        Assert.Equal(5m, costs.MonthToDate);
        Assert.Equal(10m, costs.LastMonth);
        Assert.Contains($"created[gte]={new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()}",
            Uri.UnescapeDataString(http.Requests[0].Uri.Query));
    }

    [Fact]
    public async Task Stripe_WhenTheRestrictedKeyCantReadBalances_SaysWhatToGrant()
    {
        FakeHttp http = new(_ => (HttpStatusCode.Forbidden,
            """{"error":{"message":"The provided key 'rk_test_***' does not have the required permissions for this endpoint.","type":"invalid_request_error"}}"""));
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IStripeBilling>(new StripeBilling(StripeClient(http)))
            .BuildServiceProvider();

        CostSourceException refused = await Assert.ThrowsAsync<CostSourceException>(
            () => new StripeRevenueSource(services).ReadAsync(Today, CancellationToken.None));
        Assert.Contains("Balance", refused.Message);
        Assert.False(new StripeRevenueSource(new ServiceCollection().BuildServiceProvider()).IsConfigured);
    }

    [Fact]
    public async Task WithNothingConfigured_EveryProviderSaysSo_AndNothingIsCalled()
    {
        FakeHttp http = new(_ => throw new InvalidOperationException("nothing should be called"));
        IOptionsMonitor<OpsCostsOptions> options = Options(new OpsCostsOptions());
        OpsCosts costs = Costs(options, new FakeTime(Now),
            new AwsCostSource(options),
            new OpenRouterCostSource(options, Configuration(), new FakeHttpClientFactory(http)),
            new OpenAiCostSource(options, new FakeHttpClientFactory(http)),
            new AnthropicCostSource(options, new FakeHttpClientFactory(http)),
            new StripeRevenueSource(new ServiceCollection().BuildServiceProvider()));

        await costs.RefreshDueAsync(CancellationToken.None);
        OpsCostsResponse snapshot = costs.Snapshot();

        Assert.Empty(http.Requests);
        Assert.Equal(0m, snapshot.MonthToDateUsd);
        Assert.Equal(["aws", "openrouter", "openai", "anthropic", "stripe"], snapshot.Providers.Select(p => p.Provider));
        Assert.All(snapshot.Providers, p =>
        {
            Assert.Equal(CostStatuses.NotConfigured, p.Status);
            Assert.Null(p.MonthToDate);
            Assert.Empty(p.Daily);
        });
        Assert.Equal(CostKinds.Revenue, snapshot.Providers[^1].Kind);
    }

    [Fact]
    public async Task AProviderIsReadOnItsOwnInterval_AndAFailureKeepsTheLastNumbers()
    {
        FakeTime time = new(Now);
        IOptionsMonitor<OpsCostsOptions> options = Options(new OpsCostsOptions());
        FakeSource aws = new("aws", TimeSpan.FromHours(6), 10m);
        FakeSource openai = new("openai", null, 2.5m);
        FakeSource stripe = new("stripe", null, 99m, CostKinds.Revenue);
        FakeSource anthropic = new("anthropic", null, 0m) { Configured = false };
        OpsCosts costs = Costs(options, time, aws, openai, stripe, anthropic);

        OpsCostsResponse before = costs.Snapshot();
        Assert.All(before.Providers.Take(3), p => Assert.Equal(CostStatuses.Pending, p.Status));

        await costs.RefreshDueAsync(CancellationToken.None);
        OpsCostsResponse first = costs.Snapshot();
        Assert.Equal(12.5m, first.MonthToDateUsd);
        Assert.Equal(CostStatuses.Ok, first.Providers[0].Status);
        Assert.Equal(Now, first.Providers[0].LastSuccessAt);
        Assert.Equal(CostStatuses.NotConfigured, first.Providers[3].Status);

        // An hour on, everything but AWS is read again; OpenAI fails and keeps its numbers.
        time.Advance(TimeSpan.FromHours(1));
        openai.Fail = "OpenAI answered 401 (the key was refused)";
        await costs.RefreshDueAsync(CancellationToken.None);
        Assert.Equal(1, aws.Reads);
        Assert.Equal(2, openai.Reads);
        Assert.Equal(2, stripe.Reads);
        OpsCostProvider failed = costs.Snapshot().Providers[1];
        Assert.Equal(CostStatuses.Failed, failed.Status);
        Assert.Equal("OpenAI answered 401 (the key was refused)", failed.Error);
        Assert.Equal(Now, failed.LastSuccessAt);
        Assert.Equal(2.5m, failed.MonthToDate);

        // Six hours on, AWS is read again; OpenAI recovers.
        time.Advance(TimeSpan.FromHours(5));
        openai.Fail = null;
        await costs.RefreshDueAsync(CancellationToken.None);
        Assert.Equal(2, aws.Reads);
        Assert.Equal(CostStatuses.Ok, costs.Snapshot().Providers[1].Status);
    }

    private static OpsCosts Costs(IOptionsMonitor<OpsCostsOptions> options, TimeProvider time,
        params ICostSource[] sources) =>
        new(sources, options, time, NullLogger<OpsCosts>.Instance);

    private static IOptionsMonitor<OpsCostsOptions> Options(OpsCostsOptions value) => new StaticOptions(value);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static long Unix(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

    private static (HttpStatusCode, string) Json(string body) => (HttpStatusCode.OK, body);

    private static string CostExplorerPage(string? next, params (string Day, string Service, string Amount)[] rows)
    {
        IEnumerable<string> results = rows.GroupBy(r => r.Day).Select(day =>
        {
            DateOnly start = DateOnly.Parse(day.Key);
            string groups = string.Join(",", day.Select(r =>
                "{\"Keys\":[\"" + r.Service + "\"],\"Metrics\":{\"UnblendedCost\":{\"Amount\":\"" + r.Amount +
                "\",\"Unit\":\"USD\"}}}"));
            return $$"""{"TimePeriod":{"Start":"{{day.Key}}","End":"{{start.AddDays(1):yyyy-MM-dd}}"},"Total":{},"Groups":[{{groups}}],"Estimated":true}""";
        });
        string token = next is null ? "" : $"\"NextPageToken\":\"{next}\",";
        return "{" + token + "\"GroupDefinitions\":[{\"Type\":\"DIMENSION\",\"Key\":\"SERVICE\"}]," +
               "\"ResultsByTime\":[" + string.Join(",", results) + "],\"DimensionValueAttributes\":[]}";
    }

    private static StripeClient StripeClient(FakeHttp http) =>
        new("rk_test_example", httpClient: new SystemNetHttpClient(new HttpClient(http)));

    private sealed class StaticOptions(OpsCostsOptions value) : IOptionsMonitor<OpsCostsOptions>
    {
        public OpsCostsOptions CurrentValue => value;
        public OpsCostsOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<OpsCostsOptions, string?> listener) => null;
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeSource(string provider, TimeSpan? every, decimal monthToDate, string kind = CostKinds.Cost)
        : ICostSource
    {
        public int Reads { get; private set; }
        public string? Fail { get; set; }
        public bool Configured { get; init; } = true;

        public string Provider => provider;
        public string Kind => kind;
        public bool IsConfigured => Configured;
        public TimeSpan RefreshEvery(OpsCostsOptions options) => every ?? options.RefreshInterval;

        public Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
        {
            Reads++;
            return Fail is { } error
                ? throw new CostSourceException(error)
                : Task.FromResult(new ProviderCosts("USD", monthToDate, null, [], []));
        }
    }

    private sealed record FakeRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body);

    /// <summary>Answers each request with what <paramref name="answer"/> says, keeping them.</summary>
    private sealed class FakeHttp(Func<FakeRequest, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<FakeRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            FakeRequest recorded = new(request.Method, request.RequestUri!, headers, body);
            Requests.Add(recorded);
            (HttpStatusCode status, string json) = answer(recorded);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }

    private sealed class FakeHttpClientFactory(FakeHttp handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeAwsHttpClientFactory(FakeHttp handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
