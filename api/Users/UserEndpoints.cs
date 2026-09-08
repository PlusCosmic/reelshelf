using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.Storage;

namespace Reelshelf.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // Endpoints that need the authenticated user
        RouteGroupBuilder meGroup = app.MapGroup("me")
            .RequireAuthorization();

        meGroup.MapGet("/", GetMe).WithName("GetMe");
        meGroup.MapPut("/email", SetEmail).WithName("SetMyEmail");
        meGroup.MapGet("/storage", GetStorageUsage).WithName("GetMyStorageUsage");
        meGroup.MapGet("/identities", GetLinkedIdentities).WithName("GetMyLinkedIdentities");
        meGroup.MapDelete("/identities/{provider}", UnlinkIdentity).WithName("UnlinkMyIdentity");

        // Endpoints that don't need the current user but still require authorization
        app.MapGet("user/{userId:guid}", GetUser).RequireAuthorization();
        app.MapGet("users/suggestions", GetUserSuggestions).RequireAuthorization();
    }

    private static async Task<Ok<CurrentUserResponse>> GetMe(AuthenticatedUser user, UserStatements userStatements)
    {
        // Until the user has chosen an address, offer the one their providers reported (primary identity first).
        string? suggestedEmail = null;
        if (!user.OnboardingCompleted)
        {
            List<UserStatements.UserIdentityRow> identities = await userStatements.GetIdentitiesForUser(user.Id);
            suggestedEmail = identities.Select(identity => identity.Email).FirstOrDefault(email => !string.IsNullOrEmpty(email));
        }

        return TypedResults.Ok(new CurrentUserResponse(
            user.Id,
            user.Username,
            user.GlobalName,
            user.AvatarUrl,
            user.Email,
            suggestedEmail,
            NeedsOnboarding: !user.OnboardingCompleted));
    }

    private static async Task<Results<NoContent, BadRequest<string>>> SetEmail(
        AuthenticatedUser user,
        SetEmailRequest request,
        UserStatements userStatements)
    {
        if (!EmailAddress.TryNormalize(request.Email, out string? email))
        {
            return TypedResults.BadRequest("Enter a valid email address, or leave it empty.");
        }

        await userStatements.SetUserEmail(user.Id, email);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<StorageUsageResponse>> GetStorageUsage(
        AuthenticatedUser user,
        StorageQuotaService storageQuotaService)
    {
        StorageQuota quota = await storageQuotaService.GetQuota(user);
        return TypedResults.Ok(new StorageUsageResponse(quota.UsedBytes, quota.LimitBytes, quota.IsUnlimited));
    }

    private static async Task<Ok<List<LinkedIdentity>>> GetLinkedIdentities(
        AuthenticatedUser user,
        AccountLinkingService accountLinking)
    {
        return TypedResults.Ok(await accountLinking.GetLinkedIdentities(user.Id));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> UnlinkIdentity(
        string provider,
        AuthenticatedUser user,
        AccountLinkingService accountLinking)
    {
        string? normalized = AuthProvider.Normalize(provider);
        if (normalized is null)
        {
            return TypedResults.NotFound();
        }

        return await accountLinking.Unlink(user.Id, normalized) switch
        {
            UnlinkOutcome.Unlinked => TypedResults.NoContent(),
            UnlinkOutcome.NotLinked => TypedResults.NotFound(),
            _ => TypedResults.BadRequest("Link another sign-in method before removing this one.")
        };
    }

    private static async Task<Results<Ok<UserProfile>, NotFound>> GetUser(Guid userId, UserStatements userStatements)
    {
        UserStatements.UserRow? dbUser = await userStatements.GetUserById(userId);
        if (dbUser is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new UserProfile(dbUser.Id, dbUser.Username, dbUser.GlobalName, dbUser.AvatarUrl));
    }

    private static async Task<Ok<List<UserProfile>>> GetUserSuggestions(
        AuthenticatedUser user,
        UserStatements userStatements)
    {
        // Sign-up is open, so only suggest people the caller already shares a playlist with.
        var users = await userStatements.GetPlaylistPeers(user.Id);

        var suggestions = users
            .Select(u => new UserProfile(u.Id, u.Username, u.GlobalName, u.AvatarUrl))
            .ToList();

        return TypedResults.Ok(suggestions);
    }
}

/// <summary>The signed-in user's own view of their account; never returned for other users.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Username,
    string? GlobalName,
    string? Avatar,
    string? Email,
    string? SuggestedEmail,
    bool NeedsOnboarding);

/// <summary>An empty or null email clears the address; onboarding still counts as completed.</summary>
public sealed record SetEmailRequest(string? Email);
