using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// OpenAI's organization Costs API (<c>GET /v1/organization/costs</c>, daily buckets grouped by line item), with an
/// admin key (<c>Ops:Costs:OpenAi:AdminKey</c>).
/// </summary>
public sealed class OpenAiCostSource(IOptionsMonitor<OpsCostsOptions> options, IHttpClientFactory httpClientFactory)
    : ICostSource
{
    public const string Name = "openai";
    public static readonly Uri BaseAddress = new("https://api.openai.com/");

    /// <summary>The Costs API's most daily buckets per page.</summary>
    private const int PageSize = 180;

    public string Provider => Name;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.CurrentValue.OpenAi.AdminKey);

    public async Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        string key = options.CurrentValue.OpenAi.AdminKey.Trim();
        HttpClient http = httpClientFactory.CreateClient(CostHttp.HttpClientName);
        long start = new DateTimeOffset(CostWindow.ReadStart(today).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .ToUnixTimeSeconds();
        long end = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        List<CostEntry> entries = [];
        string? currency = null;
        string? page = null;
        do
        {
            string url = $"v1/organization/costs?start_time={start}&end_time={end}&bucket_width=1d" +
                         $"&group_by=line_item&limit={PageSize}" +
                         (page is null ? "" : $"&page={Uri.EscapeDataString(page)}");
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BaseAddress, url));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using JsonDocument json = await CostHttp.GetJsonAsync(http, request, "OpenAI", cancellationToken);
            JsonElement root = json.RootElement;
            if (root.TryGetProperty("data", out JsonElement buckets))
            {
                foreach (JsonElement bucket in buckets.EnumerateArray())
                {
                    DateOnly day = CostHttp.Day(DateTimeOffset.FromUnixTimeSeconds(bucket.GetProperty("start_time").GetInt64()));
                    if (!bucket.TryGetProperty("results", out JsonElement results))
                    {
                        continue;
                    }

                    foreach (JsonElement result in results.EnumerateArray())
                    {
                        if (!result.TryGetProperty("amount", out JsonElement amount) ||
                            amount.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        currency ??= CostHttp.String(amount, "currency");
                        entries.Add(new CostEntry(day, CostHttp.String(result, "line_item") ?? CostWindow.OtherName,
                            amount.TryGetProperty("value", out JsonElement value) ? CostHttp.Decimal(value) : 0m));
                    }
                }
            }

            page = root.TryGetProperty("has_more", out JsonElement more) && more.ValueKind == JsonValueKind.True
                ? CostHttp.String(root, "next_page")
                : null;
        } while (page is not null);

        return CostWindow.Summarize(currency ?? "usd", entries, today);
    }
}
