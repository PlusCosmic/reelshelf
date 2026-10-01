using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Reelshelf.DiscordActivity;

/// <summary>
/// The Discord calls the Activity needs: exchanging the Embedded App SDK's authorization code, reading who the
/// code belongs to, and asking (with the bot token) who is in an Activity instance. Uses the same Discord
/// application as site sign-in, so the user ids match stored Discord identities.
/// </summary>
public sealed class DiscordActivityClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    public const string HttpClientName = "discord-activity";

    private const string ApiBase = "https://discord.com/api/v10";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string ClientId => configuration["DiscordClientId"]?.Trim() ?? "";
    private string ClientSecret => configuration["DiscordClientSecret"]?.Trim() ?? "";
    private string BotToken => configuration[DiscordActivityOptions.BotTokenKey]?.Trim() ?? "";

    /// <summary>Instance checks need the bot token; without one the Activity stays off.</summary>
    public bool IsConfigured => ClientId.Length > 0 && ClientSecret.Length > 0 && BotToken.Length > 0;

    /// <summary>The user's access token for <paramref name="code"/>; null when Discord refuses the code.</summary>
    public async Task<string?> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        // The SDK's authorize() needs no redirect_uri, so the exchange sends none either.
        using FormUrlEncodedContent body = new(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code
        });

        using HttpResponseMessage response = await client.PostAsync($"{ApiBase}/oauth2/token", body, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken);
        return string.IsNullOrEmpty(token?.AccessToken) ? null : token.AccessToken;
    }

    public async Task<DiscordUser> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, $"{ApiBase}/users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiscordUser>(Json, cancellationToken)
               ?? throw new InvalidOperationException("Discord returned an empty user");
    }

    /// <summary>Ids of the users connected to an Activity instance; null when the instance is not running.</summary>
    public async Task<IReadOnlyList<string>?> GetInstanceUserIdsAsync(string instanceId, CancellationToken cancellationToken)
    {
        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get,
            $"{ApiBase}/applications/{Uri.EscapeDataString(ClientId)}/activity-instances/{Uri.EscapeDataString(instanceId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", BotToken);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        ActivityInstance? instance = await response.Content.ReadFromJsonAsync<ActivityInstance>(Json, cancellationToken);
        return instance?.Users ?? [];
    }

    private sealed record TokenResponse(string? AccessToken);

    private sealed record ActivityInstance(string? InstanceId, List<string>? Users);
}

public sealed record DiscordUser(
    string Id,
    string Username,
    string? GlobalName,
    string? Avatar)
{
    public string Name => string.IsNullOrWhiteSpace(GlobalName) ? Username : GlobalName;

    public string? AvatarUrl => string.IsNullOrEmpty(Avatar)
        ? null
        : Auth.AuthenticationSetup.DiscordAvatarUrl(Id, Avatar);
}
