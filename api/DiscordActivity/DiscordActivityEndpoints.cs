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
        group.MapGet("/me", GetMe).WithName("GetActivityParticipant")
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

        builder.Services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, RoomTokenAuthentication>(
                RoomTokenAuthentication.Scheme, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RoomTokenAuthentication.Policy, policy => policy
                .AddAuthenticationSchemes(RoomTokenAuthentication.Scheme)
                .RequireAuthenticatedUser());
    }
}
