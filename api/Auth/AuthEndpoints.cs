using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Reelshelf.Email;
using Reelshelf.Users;

namespace Reelshelf.Auth;

public static class AuthEndpoints
{
    private static string _frontendOrigin = "http://localhost:5173";
    private static HashSet<string> _allowedReturnOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:5173",
        "http://localhost:5174"
    };

    /// <summary>Frontend page that shows linked accounts; link flows land here by default.</summary>
    private const string SettingsPath = "/settings";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        string? frontendOrigin = configuration["FrontendOrigin"];
        if (frontendOrigin != null)
        {
            _frontendOrigin = frontendOrigin;
        }

        string[] configuredOrigins = configuration.GetSection("Auth:AllowedReturnOrigins").Get<string[]>()
                                     ?? configuration["AllowedReturnOrigins"]?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                                     ?? [];
        if (configuredOrigins.Length > 0)
        {
            _allowedReturnOrigins = configuredOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrWhiteSpace(_frontendOrigin))
        {
            _allowedReturnOrigins.Add(_frontendOrigin);
        }

        RouteGroupBuilder group = app.MapGroup("auth");

        group.MapGet("{provider}/login", Login).WithName("Login");
        group.MapGet("{provider}/link", Link).WithName("LinkIdentity").RequireAuthorization();
        group.MapGet("post-login-redirect", PostLoginRedirect).WithName("PostLoginRedirect");
        group.MapPost("dev-login", DevLogin).WithName("DevLogin");
        group.MapPost("logout", Logout).WithName("Logout");
    }

    /// <summary>Starts a sign-in with <paramref name="provider"/>; unknown providers are a 404.</summary>
    public static Results<ChallengeHttpResult, NotFound> Login(string provider, string? returnUrl)
    {
        string? scheme = AuthProvider.Normalize(provider);
        if (scheme is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Challenge(BuildChallengeProperties(scheme, returnUrl, linkUserId: null), [scheme]);
    }

    /// <summary>
    /// Attaches a second provider to the signed-in account. The account id travels in the protected
    /// authentication properties and is checked against the session again when the provider returns.
    /// </summary>
    public static Results<ChallengeHttpResult, NotFound, UnauthorizedHttpResult> Link(
        string provider,
        string? returnUrl,
        ClaimsPrincipal user)
    {
        string? scheme = AuthProvider.Normalize(provider);
        if (scheme is null)
        {
            return TypedResults.NotFound();
        }

        Guid? currentUserId = GetUserId(user);
        if (currentUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Challenge(BuildChallengeProperties(scheme, returnUrl, currentUserId), [scheme]);
    }

    public static async Task<Results<RedirectHttpResult, UnauthorizedHttpResult>> PostLoginRedirect(
        HttpContext ctx,
        string? returnUrl,
        AccountLinkingService accountLinking,
        UserStatements userStatements,
        IEmailSender emailSender)
    {
        AuthenticateResult external = await ctx.AuthenticateAsync(AuthenticationSetup.ExternalScheme);
        if (!external.Succeeded || external.Principal is null || external.Properties is null)
        {
            return TypedResults.Unauthorized();
        }

        await ctx.SignOutAsync(AuthenticationSetup.ExternalScheme);

        AuthenticationProperties properties = external.Properties;
        string? provider = properties.GetString(AuthenticationSetup.ProviderItem);
        ExternalIdentity? identity = AuthProvider.IsKnown(provider)
            ? AuthenticationSetup.ReadExternalIdentity(provider!, external.Principal)
            : null;
        if (identity is null)
        {
            return TypedResults.Unauthorized();
        }

        string? linkUserId = properties.GetString(AuthenticationSetup.LinkUserItem);
        if (linkUserId is not null)
        {
            return await CompleteLink(ctx, linkUserId, identity, returnUrl, accountLinking, userStatements, emailSender);
        }

        SignInOutcome outcome = await accountLinking.SignIn(identity);
        await SignInAccount(ctx, outcome.User);

        return TypedResults.Redirect(ResolveReturnUrl(returnUrl) ?? _frontendOrigin);
    }

    private static async Task<Results<RedirectHttpResult, UnauthorizedHttpResult>> CompleteLink(
        HttpContext ctx,
        string linkUserId,
        ExternalIdentity identity,
        string? returnUrl,
        AccountLinkingService accountLinking,
        UserStatements userStatements,
        IEmailSender emailSender)
    {
        string destination = ResolveReturnUrl(returnUrl) ?? _frontendOrigin.TrimEnd('/') + SettingsPath;

        // The link must complete in the same session that started it.
        Guid? sessionUserId = GetUserId(ctx.User);
        if (sessionUserId is null || !Guid.TryParse(linkUserId, out Guid startedBy) || startedBy != sessionUserId)
        {
            return TypedResults.Redirect(WithQuery(destination, "link_error", "session_expired"));
        }

        LinkOutcome outcome = await accountLinking.Link(startedBy, identity);
        if (outcome == LinkOutcome.Linked)
        {
            await NotifyIdentityLinked(startedBy, identity, userStatements, emailSender);
        }

        string redirect = outcome switch
        {
            LinkOutcome.Linked or LinkOutcome.AlreadyLinked => WithQuery(destination, "linked", identity.Provider),
            LinkOutcome.ProviderAlreadyLinked => WithQuery(destination, "link_error", "provider_already_linked"),
            _ => WithQuery(destination, "link_error", "linked_to_another_account")
        };

        return TypedResults.Redirect(redirect);
    }

    /// <summary>Tells the account owner a new sign-in method was attached, so a hijacked link can be undone.</summary>
    private static async Task NotifyIdentityLinked(
        Guid userId,
        ExternalIdentity identity,
        UserStatements userStatements,
        IEmailSender emailSender)
    {
        UserStatements.UserRow? user = await userStatements.GetUserById(userId);
        if (string.IsNullOrEmpty(user?.Email))
        {
            return;
        }

        string providerLabel = identity.Provider == AuthProvider.Twitch ? "Twitch" : "Discord";
        string settingsUrl = _frontendOrigin.TrimEnd('/') + SettingsPath;
        await emailSender.SendAsync(AccountEmails.IdentityLinked(user.Email, providerLabel, identity.Username, settingsUrl));
    }

    public static async Task Logout(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static async Task<Results<Ok<DevLoginResponse>, NotFound, UnauthorizedHttpResult>> DevLogin(
        HttpContext ctx,
        IConfiguration configuration,
        AccountLinkingService accountLinking,
        DevLoginRequest request)
    {
        DevLoginOptions? options = GetDevLoginOptions(configuration);
        if (options is null)
        {
            return TypedResults.NotFound();
        }

        if (!ApiKeysMatch(options.ApiKey, request.ApiKey))
        {
            return TypedResults.Unauthorized();
        }

        string? avatarUrl = string.IsNullOrWhiteSpace(options.Avatar)
            ? null
            : options.Avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? options.Avatar
                : AuthenticationSetup.DiscordAvatarUrl(options.DiscordId, options.Avatar);

        SignInOutcome outcome = await accountLinking.SignIn(new ExternalIdentity(
            AuthProvider.Discord,
            options.DiscordId,
            options.Username,
            string.IsNullOrWhiteSpace(options.GlobalName) ? null : options.GlobalName,
            avatarUrl,
            Email: null));

        await SignInAccount(ctx, outcome.User, persistent: true);

        return TypedResults.Ok(new DevLoginResponse(ResolveReturnUrl(request.ReturnUrl) ?? _frontendOrigin));
    }

    /// <summary>Where a failed or cancelled provider round-trip sends the browser.</summary>
    internal static string BuildFailureRedirect(AuthenticationProperties? properties, bool linking)
    {
        // The challenge's RedirectUri is "/auth/post-login-redirect?returnUrl=..."; recover the returnUrl from it.
        string? returnUrl = null;
        if (properties?.RedirectUri is { } redirectUri &&
            Uri.TryCreate(new Uri("http://localhost"), redirectUri, out Uri? uri))
        {
            returnUrl = QueryHelpers.ParseQuery(uri.Query).GetValueOrDefault("returnUrl").ToString();
        }

        string destination = ResolveReturnUrl(returnUrl)
                             ?? (linking ? _frontendOrigin.TrimEnd('/') + SettingsPath : _frontendOrigin);

        return WithQuery(destination, linking ? "link_error" : "auth_error", "provider_failed");
    }

    private static AuthenticationProperties BuildChallengeProperties(string provider, string? returnUrl, Guid? linkUserId)
    {
        string? safeReturnUrl = ResolveReturnUrl(returnUrl);

        AuthenticationProperties props = new()
        {
            RedirectUri = "/auth/post-login-redirect" +
                          (safeReturnUrl != null ? $"?returnUrl={Uri.EscapeDataString(safeReturnUrl)}" : "")
        };

        props.Items["state"] = GenerateSecureRandomState();
        props.Items[AuthenticationSetup.ProviderItem] = provider;
        if (linkUserId is { } userId)
        {
            props.Items[AuthenticationSetup.LinkUserItem] = userId.ToString();
        }

        return props;
    }

    private static async Task SignInAccount(HttpContext ctx, UserStatements.UserRow user, bool persistent = false)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        ];

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        AuthenticationProperties properties = new();
        if (persistent)
        {
            properties.IsPersistent = true;
            properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7);
        }

        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;
    }

    private static string? ResolveReturnUrl(string? url)
    {
        return !string.IsNullOrEmpty(url) && IsValidReturnUrl(url) ? url : null;
    }

    private static bool IsValidReturnUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return _allowedReturnOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));
    }

    private static string WithQuery(string url, string name, string value)
    {
        return QueryHelpers.AddQueryString(url, name, value);
    }

    private static string GenerateSecureRandomState()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(randomBytes).ToLowerInvariant();
    }

    private static DevLoginOptions? GetDevLoginOptions(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Auth:DevLogin:Enabled"))
        {
            return null;
        }

        string apiKey = configuration["Auth:DevLogin:ApiKey"] ?? "";
        string discordId = configuration["Auth:DevLogin:DiscordId"] ?? "";
        string username = configuration["Auth:DevLogin:Username"] ?? "dev-user";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(discordId))
        {
            return null;
        }

        return new DevLoginOptions(
            apiKey,
            discordId,
            username,
            configuration["Auth:DevLogin:GlobalName"],
            configuration["Auth:DevLogin:Avatar"]);
    }

    private static bool ApiKeysMatch(string configuredApiKey, string? providedApiKey)
    {
        if (string.IsNullOrEmpty(providedApiKey))
        {
            return false;
        }

        byte[] configured = Encoding.UTF8.GetBytes(configuredApiKey);
        byte[] provided = Encoding.UTF8.GetBytes(providedApiKey);

        return provided.Length == configured.Length &&
               CryptographicOperations.FixedTimeEquals(provided, configured);
    }

    private sealed record DevLoginOptions(
        string ApiKey,
        string DiscordId,
        string Username,
        string? GlobalName,
        string? Avatar);
}

public sealed record DevLoginRequest(
    string? ApiKey,
    string? ReturnUrl = null);

public sealed record DevLoginResponse(string RedirectUrl);
