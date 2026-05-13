using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Discord;

namespace Reelshelf.Auth;

public static class AuthEndpoints
{
    private static string _frontendOrigin = "http://localhost:5173";
    private static HashSet<string> _allowedReturnOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:5173",
        "http://localhost:5174"
    };

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

        group.MapGet("discord/login", Login).WithName("Login");
        group.MapGet("post-login-redirect", PostLoginRedirect).WithName("PostLoginRedirect");
        group.MapPost("dev-login", DevLogin).WithName("DevLogin");
        group.MapPost("logout", Logout).WithName("Logout");
    }

    public static IResult Login(HttpContext ctx, string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && !IsValidReturnUrl(returnUrl))
        {
            returnUrl = null;
        }

        string state = GenerateSecureRandomState();

        AuthenticationProperties props = new()
        {
            RedirectUri = "/auth/post-login-redirect" +
                          (returnUrl != null ? $"?returnUrl={Uri.EscapeDataString(returnUrl)}" : "")
        };

        props.Items["state"] = state;

        return Results.Challenge(props, new[] { "Discord" });
    }

    public static async Task<Results<RedirectHttpResult, UnauthorizedHttpResult>> PostLoginRedirect(HttpContext ctx,
        string? returnUrl,
        ClaimsPrincipal user, DiscordStatements discordStatements)
    {
        string discordId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        string username = user.FindFirstValue(ClaimTypes.Name) ?? "";
        if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(username))
        {
            return TypedResults.Unauthorized();
        }

        string? globalName = user.FindFirstValue("urn:discord:global_name");
        string? avatar = user.FindFirstValue("urn:discord:avatar");

        await discordStatements.UpsertUser(discordId, username, globalName, avatar);

        string redirect;
        if (!string.IsNullOrEmpty(returnUrl) && IsValidReturnUrl(returnUrl))
        {
            redirect = returnUrl;
        }
        else
        {
            redirect = _frontendOrigin;
        }

        ctx.Response.Redirect(redirect);
        return TypedResults.Redirect(redirect);
    }

    public static async Task Logout(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static async Task<Results<Ok<DevLoginResponse>, NotFound, UnauthorizedHttpResult>> DevLogin(
        HttpContext ctx,
        IConfiguration configuration,
        DiscordStatements discordStatements,
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

        DiscordStatements.DiscordUserRow? existingUser = await discordStatements.GetUserByDiscordId(options.DiscordId);
        if (existingUser is null)
        {
            await discordStatements.UpsertUser(options.DiscordId, options.Username, options.GlobalName, options.Avatar);
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, options.DiscordId),
            new(ClaimTypes.Name, options.Username)
        ];

        if (!string.IsNullOrWhiteSpace(options.GlobalName))
        {
            claims.Add(new Claim("urn:discord:global_name", options.GlobalName));
        }

        if (!string.IsNullOrWhiteSpace(options.Avatar))
        {
            claims.Add(new Claim("urn:discord:avatar", options.Avatar));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        AuthenticationProperties properties = new()
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);

        string redirectUrl = _frontendOrigin;
        if (!string.IsNullOrEmpty(request.ReturnUrl) && IsValidReturnUrl(request.ReturnUrl))
        {
            redirectUrl = request.ReturnUrl;
        }

        return TypedResults.Ok(new DevLoginResponse(redirectUrl));
    }

    private static bool IsValidReturnUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return _allowedReturnOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));
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
