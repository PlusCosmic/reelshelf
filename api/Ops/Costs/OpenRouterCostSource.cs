using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// OpenRouter's spend in USD. With a management key (<c>Ops:Costs:OpenRouter:ManagementKey</c>) the account's daily
/// spend by model for the last 30 completed UTC days (<c>GET /api/v1/activity</c>) and its credit left
/// (<c>GET /api/v1/credits</c>). With API keys (the app's own OpenRouter keys and <c>Ops:Costs:OpenRouter:ApiKey</c>),
/// each key's spend this month and today (<c>GET /api/v1/key</c>), which is all there is without a management key and
/// gives today's spend, which the activity leaves out, with one. OpenRouter keeps 30 days of activity, so last month
/// is never complete and isn't given.
/// </summary>
public sealed class OpenRouterCostSource(
    IOptionsMonitor<OpsCostsOptions> options,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : ICostSource
{
    public const string Name = "openrouter";
    public static readonly Uri BaseAddress = new("https://openrouter.ai/");

    /// <summary>Where the app's own OpenRouter keys are configured.</summary>
    public static readonly string[] AppKeyPaths =
    [
        "ClipTranscription:OpenRouterApiKey",
        "LegendDetection:Providers:openrouter:ApiKey",
        "ClipSummary:Providers:openrouter:ApiKey",
        "ClipSearch:Providers:openrouter:ApiKey"
    ];

    public string Provider => Name;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.CurrentValue.OpenRouter.ManagementKey) || ApiKeys().Count > 0;

    /// <summary>Every distinct API key whose usage counts.</summary>
    internal List<string> ApiKeys() =>
        AppKeyPaths.Select(path => configuration[path])
            .Append(options.CurrentValue.OpenRouter.ApiKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public async Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        HttpClient http = httpClientFactory.CreateClient(CostHttp.HttpClientName);
        string management = options.CurrentValue.OpenRouter.ManagementKey.Trim();

        // Each API key's own usage: this month and today.
        decimal? keysMonth = null;
        decimal? keysToday = null;
        foreach (string key in ApiKeys())
        {
            using JsonDocument json = await GetAsync(http, "api/v1/key", key, cancellationToken);
            JsonElement data = json.RootElement.GetProperty("data");
            keysMonth = (keysMonth ?? 0m) + Number(data, "usage_monthly");
            keysToday = (keysToday ?? 0m) + Number(data, "usage_daily");
        }

        if (management.Length == 0)
        {
            List<OpsCostDay> todayOnly = keysToday is { } keysSpent ? [new OpsCostDay(today, keysSpent)] : [];
            return new ProviderCosts("USD", keysMonth, null, todayOnly, []);
        }

        List<CostEntry> entries = [];
        using (JsonDocument activity = await GetAsync(http, "api/v1/activity", management, cancellationToken))
        {
            foreach (JsonElement item in activity.RootElement.GetProperty("data").EnumerateArray())
            {
                if (!DateOnly.TryParseExact(CostHttp.String(item, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateOnly day))
                {
                    continue;
                }

                entries.Add(new CostEntry(day, CostHttp.String(item, "model") ?? CostWindow.OtherName,
                    Number(item, "usage")));
            }
        }

        decimal? balance;
        using (JsonDocument credits = await GetAsync(http, "api/v1/credits", management, cancellationToken))
        {
            JsonElement data = credits.RootElement.GetProperty("data");
            balance = Number(data, "total_credits") - Number(data, "total_usage");
        }

        // The activity stops at yesterday: today comes from the keys (by key, not by model, so not in the breakdown).
        entries.RemoveAll(e => e.Date == today);
        ProviderCosts summary = CostWindow.Summarize("USD", entries, today, lastMonthKnown: false);
        decimal spentToday = keysToday ?? 0m;
        return summary with
        {
            MonthToDate = summary.MonthToDate + spentToday,
            Daily = summary.Daily.Select(d => d.Date == today ? d with { Amount = spentToday } : d).ToList(),
            Balance = balance
        };
    }

    private static async Task<JsonDocument> GetAsync(HttpClient http, string path, string key,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await CostHttp.GetJsonAsync(http, request, "OpenRouter", cancellationToken);
    }

    private static decimal Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) ? CostHttp.Decimal(value) : 0m;
}
