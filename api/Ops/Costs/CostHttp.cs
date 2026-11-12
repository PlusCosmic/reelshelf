using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Reelshelf.Ops.Costs;

/// <summary>Plain-HTTP reads for the providers without an SDK here, failing in words the monitor can show.</summary>
public static class CostHttp
{
    public const string HttpClientName = "ops-costs";

    /// <summary>Sends the request and parses its JSON body; a refusal or an unreadable body throws <see cref="CostSourceException"/>.</summary>
    public static async Task<JsonDocument> GetJsonAsync(HttpClient http, HttpRequestMessage request, string provider,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CostSourceException(
                $"{provider} answered {(int)response.StatusCode} {Reason(response.StatusCode)}{Detail(body)}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new CostSourceException($"{provider} answered something that isn't JSON.", ex);
        }
    }

    private static string Reason(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "(the key was refused)",
        HttpStatusCode.Forbidden => "(the key isn't allowed to read costs)",
        HttpStatusCode.TooManyRequests => "(rate limited)",
        _ => ""
    };

    /// <summary>The provider's own error message, when its body has one, cut short.</summary>
    private static string Detail(string body)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            JsonElement error = json.RootElement.TryGetProperty("error", out JsonElement e) ? e : json.RootElement;
            string? message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement m)
                ? m.GetString()
                : error.ValueKind == JsonValueKind.String ? error.GetString() : null;
            return string.IsNullOrWhiteSpace(message) ? "" : $": {Cut(message)}";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string Cut(string message) => message.Length <= 200 ? message : message[..199] + "…";

    /// <summary>A number the provider sent as a JSON number or a numeric string.</summary>
    public static decimal Decimal(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
            out decimal parsed) => parsed,
        _ => 0m
    };

    public static string? String(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime);
}
