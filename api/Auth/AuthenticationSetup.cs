using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Reelshelf.Twitch;
using Reelshelf.Users;

namespace Reelshelf.Auth;

/// <summary>
/// Cookie and OAuth wiring. Providers sign in to a short-lived external cookie; the post-login endpoint
/// turns that external identity into an account session (or links it to the current account).
/// </summary>
internal static class AuthenticationSetup
{
    /// <summary>Scheme holding the provider's identity between the OAuth callback and account resolution.</summary>
    public const string ExternalScheme = "External";

    public const string DisplayNameClaim = "urn:reelshelf:display_name";
    public const string AvatarUrlClaim = "urn:reelshelf:avatar_url";
    public const string EmailClaim = "urn:reelshelf:email";

    /// <summary>Space-separated scopes the provider actually granted on the token, as reported in its token response.</summary>
    public const string TokenScopesClaim = "urn:reelshelf:token_scopes";

    /// <summary>Authentication-properties key naming the provider a challenge was issued for.</summary>
    public const string ProviderItem = "reelshelf.provider";

    /// <summary>Authentication-properties key set when the challenge links an identity to an existing account.</summary>
    public const string LinkUserItem = "reelshelf.link_user_id";

    public static void AddReelshelfAuthentication(this WebApplicationBuilder builder)
    {
        IConfiguration configuration = builder.Configuration;

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "pcdash.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "pcdash.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.SlidingExpiration = false;
            })
            .AddOAuth(AuthProvider.Discord, options =>
            {
                options.SignInScheme = ExternalScheme;
                options.AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize";
                options.TokenEndpoint = "https://discord.com/api/oauth2/token";
                options.UserInformationEndpoint = "https://discord.com/api/users/@me";

                options.ClientId = configuration["DiscordClientId"] ?? "";
                options.ClientSecret = configuration["DiscordClientSecret"] ?? "";

                options.CallbackPath = new PathString("/auth/discord/callback");

                options.Scope.Add("identify");
                options.Scope.Add("email");

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "username");
                options.ClaimActions.MapJsonKey(DisplayNameClaim, "global_name");

                options.Events = ProviderEvents(async context =>
                {
                    using JsonDocument user = await FetchUser(context, addClientIdHeader: false);
                    context.RunClaimActions(user.RootElement);

                    string? id = user.RootElement.GetStringOrNull("id");
                    string? avatarHash = user.RootElement.GetStringOrNull("avatar");
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(avatarHash))
                    {
                        context.Identity?.AddClaim(new Claim(AvatarUrlClaim, DiscordAvatarUrl(id, avatarHash)));
                    }

                    // Discord returns the address even when unconfirmed; only keep one the user has verified.
                    string? email = user.RootElement.GetStringOrNull("email");
                    bool verified = user.RootElement.TryGetProperty("verified", out JsonElement verifiedElement) &&
                                    verifiedElement.ValueKind == JsonValueKind.True;
                    if (verified && !string.IsNullOrEmpty(email))
                    {
                        context.Identity?.AddClaim(new Claim(EmailClaim, email));
                    }
                });

                options.SaveTokens = true;
            })
            .AddOAuth(AuthProvider.Twitch, options =>
            {
                options.SignInScheme = ExternalScheme;
                options.AuthorizationEndpoint = "https://id.twitch.tv/oauth2/authorize";
                options.TokenEndpoint = "https://id.twitch.tv/oauth2/token";
                options.UserInformationEndpoint = "https://api.twitch.tv/helix/users";

                options.ClientId = configuration["TwitchClientId"] ?? "";
                options.ClientSecret = configuration["TwitchClientSecret"] ?? "";

                options.CallbackPath = new PathString("/auth/twitch/callback");

                // user:read:email adds the address to the Helix user payload; channel:manage:clips lets the app
                // fetch download URLs for the user's own clips (see Twitch/TwitchScopes). Twitch:Scopes overrides
                // the list (space separated) if a deployment needs something else.
                foreach (string scope in (configuration["Twitch:Scopes"] ?? TwitchScopes.Default)
                             .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    options.Scope.Add(scope);
                }

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
                options.ClaimActions.MapJsonKey(DisplayNameClaim, "display_name");
                options.ClaimActions.MapJsonKey(AvatarUrlClaim, "profile_image_url");
                options.ClaimActions.MapJsonKey(EmailClaim, "email");

                options.Events = ProviderEvents(async context =>
                {
                    // Helix wraps the caller in { "data": [ {...} ] } and requires the Client-Id header.
                    using JsonDocument payload = await FetchUser(context, addClientIdHeader: true);
                    if (payload.RootElement.TryGetProperty("data", out JsonElement data) &&
                        data.ValueKind == JsonValueKind.Array &&
                        data.GetArrayLength() > 0)
                    {
                        context.RunClaimActions(data[0]);
                    }

                    // Twitch echoes the granted scopes in the token response; keep them so the app can tell
                    // whether this token can do more than sign in.
                    if (context.TokenResponse.Response?.RootElement.TryGetProperty("scope", out JsonElement scopes) == true &&
                        scopes.ValueKind == JsonValueKind.Array)
                    {
                        string granted = string.Join(' ', scopes.EnumerateArray()
                            .Where(scope => scope.ValueKind == JsonValueKind.String)
                            .Select(scope => scope.GetString()));
                        context.Identity?.AddClaim(new Claim(TokenScopesClaim, granted));
                    }
                });

                options.SaveTokens = true;
            });

        builder.Services.AddAuthorization();
    }

    public static string DiscordAvatarUrl(string discordId, string avatarHash)
    {
        return $"https://cdn.discordapp.com/avatars/{discordId}/{avatarHash}";
    }

    /// <summary>
    /// Reads the provider identity out of an external-scheme principal. Tokens are kept only for Twitch, the
    /// one provider the app calls back on the user's behalf; Discord tokens have no use here and are dropped.
    /// </summary>
    public static ExternalIdentity? ReadExternalIdentity(string provider, ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        string? providerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        string? username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(providerUserId) || string.IsNullOrEmpty(username))
        {
            return null;
        }

        return new ExternalIdentity(
            provider,
            providerUserId,
            username,
            NullIfEmpty(principal.FindFirstValue(DisplayNameClaim)),
            NullIfEmpty(principal.FindFirstValue(AvatarUrlClaim)),
            NullIfEmpty(principal.FindFirstValue(EmailClaim)),
            provider == AuthProvider.Twitch ? ReadTokens(principal, properties) : null);
    }

    private static ProviderTokens? ReadTokens(ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        string? accessToken = properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        DateTimeOffset? expiresAt = DateTimeOffset.TryParse(
            properties?.GetTokenValue("expires_at"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : null;

        return new ProviderTokens(
            accessToken,
            NullIfEmpty(properties?.GetTokenValue("refresh_token")),
            expiresAt,
            ProviderTokens.ParseScopes(principal.FindFirstValue(TokenScopesClaim)));
    }

    private static OAuthEvents ProviderEvents(Func<OAuthCreatingTicketContext, Task> onCreatingTicket)
    {
        return new OAuthEvents
        {
            OnRedirectToAuthorizationEndpoint = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnCreatingTicket = onCreatingTicket,
            OnRemoteFailure = context =>
            {
                // A cancelled consent screen or provider error lands the user back on the app instead of a 500.
                bool linking = context.Properties?.Items.ContainsKey(LinkUserItem) == true;
                context.Response.Redirect(AuthEndpoints.BuildFailureRedirect(context.Properties, linking));
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    }

    private static async Task<JsonDocument> FetchUser(OAuthCreatingTicketContext context, bool addClientIdHeader)
    {
        HttpRequestMessage request = new(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        if (addClientIdHeader)
        {
            request.Headers.Add("Client-Id", context.Options.ClientId);
        }

        HttpResponseMessage response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static string? GetStringOrNull(this JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
