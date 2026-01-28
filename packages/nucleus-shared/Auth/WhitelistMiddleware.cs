using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Nucleus.Shared.Auth;

public class WhitelistMiddleware
{
    private readonly ILogger<WhitelistMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly WhitelistService _whitelistService;
    private readonly HashSet<string> _bypassPaths;

    public WhitelistMiddleware(
        RequestDelegate next,
        WhitelistService whitelistService,
        ILogger<WhitelistMiddleware> logger,
        IEnumerable<string>? bypassPaths = null)
    {
        _next = next;
        _whitelistService = whitelistService;
        _logger = logger;

        string[] defaultBypassPaths = ["/health", "/auth", "/webhooks", "/apex-legends"];
        _bypassPaths = new HashSet<string>(
            bypassPaths?.Any() == true ? bypassPaths : defaultBypassPaths,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? "";

        // Skip whitelist check for bypass paths
        if (_bypassPaths.Any(bp => path.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            return;
        }

        // Get Discord user ID from claims
        string? discordUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(discordUserId))
        {
            _logger.LogWarning("Authenticated user has no Discord ID claim");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid user claims" });
            return;
        }

        // Check if user is whitelisted
        if (!_whitelistService.IsWhitelisted(discordUserId))
        {
            _logger.LogWarning("Discord user {DiscordUserId} is not whitelisted", discordUserId);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Access denied: User not whitelisted" });
            return;
        }

        await _next(context);
    }
}
