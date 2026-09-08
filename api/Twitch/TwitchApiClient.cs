using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Reelshelf.Twitch;

/// <summary>
/// Thin Helix client for the calls the app makes on a user's behalf. Every call carries the user's access
/// token plus the app's Client-Id; a 401 surfaces as <see cref="TwitchUnauthorizedException"/> so the caller
/// can refresh the token and retry once.
/// </summary>
public sealed class TwitchApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public const string HttpClientName = "twitch-helix";

    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string HelixBase = "https://api.twitch.tv/helix";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private string ClientId => configuration["TwitchClientId"] ?? throw new InvalidOperationException("TwitchClientId not configured");
    private string ClientSecret => configuration["TwitchClientSecret"] ?? throw new InvalidOperationException("TwitchClientSecret not configured");

    /// <summary>Exchanges a refresh token for a new token pair; null when Twitch no longer honours the refresh token.</summary>
    public async Task<TwitchTokenResponse?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using FormUrlEncodedContent body = new(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        });

        using HttpResponseMessage response = await client.PostAsync(TokenEndpoint, body, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // invalid_grant: the user revoked access, changed their password, or the token was already rotated.
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TwitchTokenResponse>(Json, cancellationToken)
               ?? throw new InvalidOperationException("Twitch returned an empty token response");
    }

    /// <summary>A page of the broadcaster's clips, most viewed first within the optional window.</summary>
    public async Task<TwitchPage<TwitchClip>> GetClipsAsync(
        string accessToken,
        string broadcasterId,
        int first,
        string? after,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string?> query = new()
        {
            ["broadcaster_id"] = broadcasterId,
            ["first"] = Math.Clamp(first, 1, 100).ToString()
        };
        if (!string.IsNullOrEmpty(after))
        {
            query["after"] = after;
        }

        if (startedAt is { } start)
        {
            query["started_at"] = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            query["ended_at"] = (endedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        return await GetAsync<TwitchPage<TwitchClip>>(accessToken, QueryHelpers.AddQueryString($"{HelixBase}/clips", query), cancellationToken);
    }

    /// <summary>Specific clips by id, regardless of broadcaster; the caller checks ownership.</summary>
    public async Task<TwitchPage<TwitchClip>> GetClipsByIdAsync(string accessToken, IReadOnlyCollection<string> clipIds, CancellationToken cancellationToken)
    {
        if (clipIds.Count is 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(clipIds), "Twitch resolves between 1 and 100 clips per call");
        }

        string url = $"{HelixBase}/clips?" + string.Join('&', clipIds.Select(id => $"id={Uri.EscapeDataString(id)}"));
        return await GetAsync<TwitchPage<TwitchClip>>(accessToken, url, cancellationToken);
    }

    /// <summary>Temporary download URLs for up to ten of the broadcaster's clips (needs channel:manage:clips).</summary>
    public async Task<IReadOnlyList<TwitchClipDownload>> GetClipDownloadsAsync(
        string accessToken,
        string broadcasterId,
        IReadOnlyCollection<string> clipIds,
        CancellationToken cancellationToken)
    {
        if (clipIds.Count is 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(clipIds), "Twitch resolves between 1 and 10 clip downloads per call");
        }

        string url = $"{HelixBase}/clips/downloads?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&editor_id={Uri.EscapeDataString(broadcasterId)}"
                     + string.Concat(clipIds.Select(id => $"&clip_id={Uri.EscapeDataString(id)}"));

        TwitchPage<TwitchClipDownload> page = await GetAsync<TwitchPage<TwitchClipDownload>>(accessToken, url, cancellationToken);
        return page.Data;
    }

    /// <summary>Twitch categories by id; each carries the IGDB id Reelshelf keys its game categories on.</summary>
    public async Task<IReadOnlyList<TwitchGame>> GetGamesAsync(
        string accessToken,
        IReadOnlyCollection<string> gameIds,
        CancellationToken cancellationToken)
    {
        if (gameIds.Count == 0)
        {
            return [];
        }

        string url = $"{HelixBase}/games?" + string.Join('&', gameIds.Take(100).Select(id => $"id={Uri.EscapeDataString(id)}"));
        TwitchPage<TwitchGame> page = await GetAsync<TwitchPage<TwitchGame>>(accessToken, url, cancellationToken);
        return page.Data;
    }

    private async Task<T> GetAsync<T>(string accessToken, string url, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", ClientId);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new TwitchUnauthorizedException();
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new TwitchForbiddenException(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TwitchRateLimitedException();
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
               ?? throw new InvalidOperationException($"Twitch returned an empty response for {url}");
    }
}

/// <summary>Helix rejected the access token; refresh it and retry, or ask the user to reconnect.</summary>
public sealed class TwitchUnauthorizedException() : Exception("Twitch rejected the access token");

/// <summary>Helix refused the call for this user, e.g. the token lacks a scope or the user is not an editor.</summary>
public sealed class TwitchForbiddenException(string detail) : Exception($"Twitch refused the request: {detail}");

public sealed class TwitchRateLimitedException() : Exception("Twitch rate limit reached");

public sealed record TwitchTokenResponse(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    List<string>? Scope,
    string? TokenType);

public sealed record TwitchPage<T>(List<T> Data, TwitchPagination? Pagination);

public sealed record TwitchPagination(string? Cursor);

public sealed record TwitchClip(
    string Id,
    string Url,
    string? EmbedUrl,
    string BroadcasterId,
    string? BroadcasterName,
    string? CreatorId,
    string? CreatorName,
    string? VideoId,
    string? GameId,
    string? Language,
    string Title,
    long ViewCount,
    DateTimeOffset CreatedAt,
    string? ThumbnailUrl,
    double Duration,
    int? VodOffset,
    bool? IsFeatured);

public sealed record TwitchClipDownload(
    string ClipId,
    string? LandscapeDownloadUrl,
    string? PortraitDownloadUrl);

public sealed record TwitchGame(
    string Id,
    string Name,
    string? BoxArtUrl,
    [property: JsonConverter(typeof(LenientLongConverter))] long? IgdbId);

/// <summary>Twitch sends igdb_id as a string ("" when unknown); read it as a number.</summary>
public sealed class LenientLongConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt64(out long number) => number,
            JsonTokenType.String when long.TryParse(reader.GetString(), out long parsed) && parsed > 0 => parsed,
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
