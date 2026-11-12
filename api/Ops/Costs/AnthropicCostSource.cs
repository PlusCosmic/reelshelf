using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// Anthropic's Admin API cost report (<c>GET /v1/organizations/cost_report</c>, daily, grouped by description so each
/// amount names its model), with an Admin API key (<c>Ops:Costs:Anthropic:AdminKey</c>). Amounts come in cents as
/// decimal strings. Priority Tier costs aren't in the report.
/// </summary>
public sealed class AnthropicCostSource(IOptionsMonitor<OpsCostsOptions> options, IHttpClientFactory httpClientFactory)
    : ICostSource
{
    public const string Name = "anthropic";
    public static readonly Uri BaseAddress = new("https://api.anthropic.com/");
    public const string ApiVersion = "2023-06-01";

    /// <summary>The report's most daily buckets per page.</summary>
    private const int PageSize = 31;

    public string Provider => Name;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.Anthropic.AdminKey);

    public async Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        string key = options.CurrentValue.Anthropic.AdminKey.Trim();
        HttpClient http = httpClientFactory.CreateClient(CostHttp.HttpClientName);
        string start = Rfc3339(CostWindow.ReadStart(today));
        string end = Rfc3339(today.AddDays(1));
        List<CostEntry> entries = [];
        string? currency = null;
        string? page = null;
        do
        {
            string url = $"v1/organizations/cost_report?starting_at={start}&ending_at={end}&bucket_width=1d" +
                         $"&group_by[]=description&limit={PageSize}" +
                         (page is null ? "" : $"&page={Uri.EscapeDataString(page)}");
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BaseAddress, url));
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", ApiVersion);
            using JsonDocument json = await CostHttp.GetJsonAsync(http, request, "Anthropic", cancellationToken);
            JsonElement root = json.RootElement;
            if (root.TryGetProperty("data", out JsonElement buckets))
            {
                foreach (JsonElement bucket in buckets.EnumerateArray())
                {
                    if (!DateTimeOffset.TryParse(CostHttp.String(bucket, "starting_at"), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out DateTimeOffset startsAt) ||
                        !bucket.TryGetProperty("results", out JsonElement results))
                    {
                        continue;
                    }

                    DateOnly day = CostHttp.Day(startsAt);
                    foreach (JsonElement result in results.EnumerateArray())
                    {
                        currency ??= CostHttp.String(result, "currency");
                        string name = CostHttp.String(result, "model") ??
                                      CostHttp.String(result, "cost_type") ??
                                      CostHttp.String(result, "description") ?? CostWindow.OtherName;
                        decimal cents = result.TryGetProperty("amount", out JsonElement amount)
                            ? CostHttp.Decimal(amount)
                            : 0m;
                        entries.Add(new CostEntry(day, name, cents / 100m));
                    }
                }
            }

            page = root.TryGetProperty("has_more", out JsonElement more) && more.ValueKind == JsonValueKind.True
                ? CostHttp.String(root, "next_page")
                : null;
        } while (page is not null);

        return CostWindow.Summarize(currency ?? "USD", entries, today);
    }

    private static string Rfc3339(DateOnly day) =>
        Uri.EscapeDataString(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z");
}
