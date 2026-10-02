using Microsoft.AspNetCore.Http.HttpResults;

namespace Reelshelf.DiscordActivity;

public sealed record ActivityConfigResponse(bool Enabled, string? ClientId);

public sealed record ActivityTokenRequest(string Code, string InstanceId);

/// <summary>
/// Endpoints for the Discord Activity (ADR-0006). The config and token exchange are open, since the Activity
/// starts with no session; everything else requires a room token through <see cref="RoomTokenAuthentication.Policy"/>.
/// </summary>
public static class DiscordActivityEndpoints
{
    public static void MapDiscordActivityEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("activity");

        group.MapGet("/config", GetConfig).WithName("GetActivityConfig").AllowAnonymous();
        group.MapPost("/token", ExchangeToken).WithName("ExchangeActivityToken")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.ActivityToken);
        group.MapPost("/token/refresh", RefreshToken).WithName("RefreshActivityToken")
            .RequireAuthorization(RoomTokenAuthentication.Policy)
            .RequireRateLimiting(RateLimitPolicies.ActivityToken);
        group.MapHub<WatchRoomHub>("/hub").RequireAuthorization(RoomTokenAuthentication.Policy);
        group.MapGet("/me", GetMe).WithName("GetActivityParticipant")
            .RequireAuthorization(RoomTokenAuthentication.Policy);
        group.MapGet("/clips", GetOwnClips).WithName("GetActivityClips")
            .RequireAuthorization(RoomTokenAuthentication.Policy);
    }

    /// <summary>The Discord client id the SDK is constructed with; disabled until the bot token is configured.</summary>
    private static Ok<ActivityConfigResponse> GetConfig(DiscordActivityClient discord)
    {
        return TypedResults.Ok(discord.IsConfigured
            ? new ActivityConfigResponse(true, discord.ClientId)
            : new ActivityConfigResponse(false, null));
    }

    private static async Task<Results<Ok<ActivitySessionResponse>, BadRequest<string>, ProblemHttpResult>> ExchangeToken(
        ActivitySignInService signIn,
        ActivityTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.InstanceId))
        {
            return TypedResults.BadRequest("A code and instance id are required");
        }

        ActivitySignInResult result = await signIn.SignInAsync(request.Code.Trim(), request.InstanceId.Trim(), cancellationToken);
        return result.Outcome switch
        {
            ActivitySignInOutcome.Ok => TypedResults.Ok(result.Session!),
            ActivitySignInOutcome.InvalidCode => TypedResults.BadRequest("Discord did not accept the authorization code"),
            ActivitySignInOutcome.NotInInstance => TypedResults.Problem("You are not connected to this Activity", statusCode: StatusCodes.Status403Forbidden),
            _ => TypedResults.Problem("The Discord Activity is not configured", statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    /// <summary>
    /// Renews a room token before it expires, so a long session can reconnect to the hub. Refused once the
    /// holder is no longer in the Activity instance.
    /// </summary>
    private static async Task<Results<Ok<RoomTokenResponse>, UnauthorizedHttpResult, ProblemHttpResult>> RefreshToken(
        ActivitySignInService signIn,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        RoomParticipant? participant = RoomTokenAuthentication.ReadParticipant(context.User);
        if (participant is null)
        {
            return TypedResults.Unauthorized();
        }

        RoomTokenResponse? refreshed = await signIn.RefreshAsync(participant, cancellationToken);
        return refreshed is null
            ? TypedResults.Problem("You are not connected to this Activity", statusCode: StatusCodes.Status403Forbidden)
            : TypedResults.Ok(refreshed);
    }

    private static async Task<Results<Ok<ActivityParticipantResponse>, UnauthorizedHttpResult>> GetMe(
        ActivitySignInService signIn,
        HttpContext context)
    {
        RoomParticipant? participant = RoomTokenAuthentication.ReadParticipant(context.User);
        if (participant is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await signIn.DescribeAsync(participant.DiscordUserId, participant.Name, participant.AvatarUrl));
    }

    /// <summary>The caller's own clips to pick from. Only room members have a shelf; guests are refused.</summary>
    private static async Task<Results<Ok<ActivityClipsResponse>, UnauthorizedHttpResult, ProblemHttpResult>> GetOwnClips(
        ActivitySignInService signIn,
        ActivityClipService activityClips,
        HttpContext context,
        string? search = null,
        int page = 1)
    {
        RoomParticipant? participant = RoomTokenAuthentication.ReadParticipant(context.User);
        if (participant is null)
        {
            return TypedResults.Unauthorized();
        }

        ActivityParticipantResponse described =
            await signIn.DescribeAsync(participant.DiscordUserId, participant.Name, participant.AvatarUrl);
        if (described.AccountId is not { } accountId)
        {
            return TypedResults.Problem("Link Discord to a Reelshelf account to play your clips",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(await activityClips.GetOwnClips(accountId, search, page));
    }
}

public static class DiscordActivitySetup
{
    public static void AddDiscordActivity(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient(DiscordActivityClient.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddSingleton<DiscordActivityClient>();
        builder.Services.AddSingleton<RoomTokens>();
        builder.Services.AddScoped<ActivitySignInService>();
        builder.Services.AddScoped<ActivityClipService>();
        builder.Services.AddSingleton<WatchRoomRegistry>();
        builder.Services.AddSignalR();

        builder.Services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, RoomTokenAuthentication>(
                RoomTokenAuthentication.Scheme, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RoomTokenAuthentication.Policy, policy => policy
                .AddAuthenticationSchemes(RoomTokenAuthentication.Scheme)
                .RequireAuthenticatedUser());
    }
}
