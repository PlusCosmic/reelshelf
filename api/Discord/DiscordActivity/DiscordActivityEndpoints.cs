using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Reelshelf.Core;
using StackExchange.Redis;

namespace Reelshelf.Discord.DiscordActivity;

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
        group.MapGet("/clips/games", GetOwnGames).WithName("GetActivityClipGames")
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

    /// <summary>
    /// The caller's own clips to pick from, optionally from one game, made between two instants (either end
    /// optional, both inclusive) and oldest first. Only room members have a shelf; guests are refused.
    /// </summary>
    private static async Task<Results<Ok<ActivityClipsResponse>, BadRequest<string>, UnauthorizedHttpResult, ProblemHttpResult>> GetOwnClips(
        ActivitySignInService signIn,
        ActivityClipService activityClips,
        HttpContext context,
        string? search = null,
        int page = 1,
        Guid? categoryId = null,
        ClipSortOrder sortOrder = ClipSortOrder.DateDescending,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null)
    {
        if (startDate > endDate)
        {
            return TypedResults.BadRequest("The start date must not be after the end date");
        }

        (bool signedIn, Guid? accountId) = await ResolveOwnShelf(signIn, context);
        if (!signedIn)
        {
            return TypedResults.Unauthorized();
        }

        if (accountId is not { } ownerId)
        {
            return NotLinked();
        }

        return TypedResults.Ok(await activityClips.GetOwnClips(ownerId, search, page, categoryId, sortOrder, startDate, endDate));
    }

    /// <summary>The games on the caller's shelf with a clip count each, for the picker's game filter.</summary>
    private static async Task<Results<Ok<ActivityGamesResponse>, UnauthorizedHttpResult, ProblemHttpResult>> GetOwnGames(
        ActivitySignInService signIn,
        ActivityClipService activityClips,
        HttpContext context)
    {
        (bool signedIn, Guid? accountId) = await ResolveOwnShelf(signIn, context);
        if (!signedIn)
        {
            return TypedResults.Unauthorized();
        }

        if (accountId is not { } ownerId)
        {
            return NotLinked();
        }

        return TypedResults.Ok(await activityClips.GetOwnGames(ownerId));
    }

    /// <summary>
    /// Whether the caller holds a room token and, when they do, the Reelshelf account linked to their Discord
    /// identity (null for a guest).
    /// </summary>
    private static async Task<(bool SignedIn, Guid? AccountId)> ResolveOwnShelf(
        ActivitySignInService signIn,
        HttpContext context)
    {
        RoomParticipant? participant = RoomTokenAuthentication.ReadParticipant(context.User);
        if (participant is null)
        {
            return (false, null);
        }

        ActivityParticipantResponse described =
            await signIn.DescribeAsync(participant.DiscordUserId, participant.Name, participant.AvatarUrl);
        return (true, described.AccountId);
    }

    private static ProblemHttpResult NotLinked() =>
        TypedResults.Problem("Link Discord to a Reelshelf account to play your clips",
            statusCode: StatusCodes.Status403Forbidden);
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
        // Rooms live in Redis and the hub's broadcasts go through it (ADR-0006), so a room outlives a deploy and
        // reaches its people whichever container each is connected to while the old and new ones overlap.
        string redisPrefix = RedisWatchRoomStore.KeyPrefixFor(builder.Environment.EnvironmentName);
        builder.Services.AddSingleton<WatchRoomServer>();
        builder.Services.AddSingleton<ReactionLimiter>();
        builder.Services.AddSingleton<IWatchRoomStore>(services =>
            new RedisWatchRoomStore(services.GetRequiredService<IConnectionMultiplexer>(), redisPrefix));
        builder.Services.AddSingleton<WatchRoomRegistry>();

        ISignalRServerBuilder signalR = builder.Services.AddSignalR();
        if (!builder.Environment.IsEnvironment("OpenApi"))
        {
            builder.Services.AddHostedService<WatchRoomClock>();
            signalR.AddStackExchangeRedis(options =>
            {
                options.Configuration = ReelshelfApiConfiguration.RedisOptions(builder.Configuration);
                options.Configuration.ChannelPrefix = RedisChannel.Literal($"{redisPrefix}signalr");
            });
        }

        builder.Services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, RoomTokenAuthentication>(
                RoomTokenAuthentication.Scheme, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RoomTokenAuthentication.Policy, policy => policy
                .AddAuthenticationSchemes(RoomTokenAuthentication.Scheme)
                .RequireAuthenticatedUser());
    }
}
