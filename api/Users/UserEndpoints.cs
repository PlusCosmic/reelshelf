using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Microsoft.Extensions.Options;
using Reelshelf.Billing;
using Reelshelf.Plans;

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
        meGroup.MapGet("/plan", GetPlan).WithName("GetMyPlan");
        meGroup.MapGet("/identities", GetLinkedIdentities).WithName("GetMyLinkedIdentities");
        meGroup.MapDelete("/identities/{provider}", UnlinkIdentity).WithName("UnlinkMyIdentity");
        meGroup.MapPost("/deletion", DeleteAccount).WithName("DeleteMyAccount");

        // Endpoints that don't need the current user but still require authorization
        app.MapGet("user/{userId:guid}", GetUser).RequireAuthorization();
        app.MapGet("users/suggestions", GetUserSuggestions).WithName("GetUserSuggestions").RequireAuthorization();
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
            NeedsOnboarding: !user.OnboardingCompleted,
            IsAdmin: user.HasPermission(Permissions.AdminUsers)));
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

    /// <summary>The caller's tier, its limits and usage, and its Stripe subscription (ADR-0007).</summary>
    private static async Task<Ok<AccountPlanResponse>> GetPlan(
        AuthenticatedUser user,
        AccountPlanService planService,
        IBillingStore billingStore,
        IPlanEnforcementStore accounts,
        IOptionsMonitor<PlanOptions> planOptions,
        HttpContext context)
    {
        AccountPlan plan = await planService.GetPlan(user);
        OverLimitState overLimit = await accounts.GetState(user.Id);
        List<BillingSubscription> subscriptions = await billingStore.GetSubscriptions(user.Id);
        bool billingEnabled = context.RequestServices.GetService<BillingService>() is not null;
        bool hasCustomer = await billingStore.GetCustomerId(user.Id) is not null;
        return TypedResults.Ok(AccountPlanResponse.From(plan, subscriptions, billingEnabled, hasCustomer,
            overLimit.OverLimitSince + planOptions.CurrentValue.GracePeriod));
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

    /// <summary>
    /// Permanently deletes the caller's account. It stops working immediately and the session is ended; the clips
    /// and the rest of the data are removed in the background.
    /// </summary>
    private static async Task<Results<NoContent, BadRequest<string>>> DeleteAccount(
        AuthenticatedUser user,
        DeleteAccountRequest request,
        AccountDeletionService accountDeletion,
        HttpContext context)
    {
        if (!AccountDeletionService.IsConfirmed(request.Confirmation))
        {
            return TypedResults.BadRequest($"Type \"{AccountDeletionService.ConfirmationPhrase}\" to confirm.");
        }

        await accountDeletion.Request(user.Id);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
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
    bool NeedsOnboarding,
    bool IsAdmin);

/// <summary>An empty or null email clears the address; onboarding still counts as completed.</summary>
public sealed record SetEmailRequest(string? Email);

/// <summary><see cref="Confirmation"/> must be the phrase the user was asked to type.</summary>
public sealed record DeleteAccountRequest(string? Confirmation);
