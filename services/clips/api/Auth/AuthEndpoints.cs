using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Shared.Discord;

namespace Nucleus.Clips.Auth;

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
}
