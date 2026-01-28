using Microsoft.AspNetCore.Http.HttpResults;
using Nucleus.Shared.Auth;
using Nucleus.Shared.Discord;

namespace Nucleus.Minecraft.Discord;

public static class DiscordUserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Endpoints that need the authenticated user
        RouteGroupBuilder meGroup = app.MapGroup("me")
            .RequireAuthorization();

        meGroup.MapGet("/", GetMe);

        // Endpoints that don't need the current user but still require authorization
        app.MapGet("user/{userId:guid}", GetUser).RequireAuthorization();
        app.MapGet("users/suggestions", GetUserSuggestions).RequireAuthorization();
    }

    private static Ok<DiscordUser> GetMe(AuthenticatedUser user)
    {
        return TypedResults.Ok(new DiscordUser(user.Id, user.Username, user.GlobalName, user.GetAvatarUrl()));
    }

    private static async Task<Results<Ok<DiscordUser>, NotFound>> GetUser(Guid userId, DiscordStatements discordStatements)
    {
        DiscordStatements.DiscordUserRow? dbUser = await discordStatements.GetUserById(userId);
        if (dbUser is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new DiscordUser(
            dbUser.Id,
            dbUser.Username,
            dbUser.GlobalName,
            $"https://cdn.discordapp.com/avatars/{dbUser.DiscordId}/{dbUser.Avatar}"));
    }

    private static async Task<Ok<List<DiscordUser>>> GetUserSuggestions(
        AuthenticatedUser user,
        DiscordStatements discordStatements)
    {
        var users = await discordStatements.GetAllUsersExcept(user.DiscordId);

        var suggestions = users.Select(u => new DiscordUser(
            u.Id,
            u.Username,
            u.GlobalName,
            string.IsNullOrEmpty(u.Avatar)
                ? null
                : $"https://cdn.discordapp.com/avatars/{u.DiscordId}/{u.Avatar}"
        )).ToList();

        return TypedResults.Ok(suggestions);
    }
}
