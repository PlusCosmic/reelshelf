using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Microsoft.Extensions.Options;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;
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
        meGroup.MapPut("/username", SetUsername).WithName("SetMyUsername");
        meGroup.MapGet("/plan", GetPlan).WithName("GetMyPlan");
        meGroup.MapGet("/features", GetFeatures).WithName("GetMyFeatures");
        meGroup.MapGet("/identities", GetLinkedIdentities).WithName("GetMyLinkedIdentities");
        meGroup.MapDelete("/identities/{provider}", UnlinkIdentity).WithName("UnlinkMyIdentity");
        meGroup.MapPost("/deletion", DeleteAccount).WithName("DeleteMyAccount");

        // Endpoints that don't need the current user but still require authorization
        app.MapGet("user/{userId:guid}", GetUser).RequireAuthorization();
        app.MapGet("users/suggestions", GetUserSuggestions).WithName("GetUserSuggestions").RequireAuthorization();
    }

    private static async Task<Ok<CurrentUserResponse>> GetMe(AuthenticatedUser user, UserStatements userStatements)
    {
        // Until the user has given an address, offer the one their providers reported (primary identity first).
        string? suggestedEmail = null;
        if (user.Email is null)
        {
            List<UserStatements.UserIdentityRow> identities = await userStatements.GetIdentitiesForUser(user.Id);
            suggestedEmail = identities.Select(identity => identity.Email).FirstOrDefault(email => !string.IsNullOrEmpty(email));
        }

        // Every account gives an email address, and one signed in with Google confirms its username, before the
        // app is theirs to use.
        return TypedResults.Ok(new CurrentUserResponse(
            user.Id,
            user.Username,
            user.GlobalName,
            user.AvatarUrl,
            user.Email,
            suggestedEmail,
            NeedsOnboarding: user.Email is null || user.NeedsUsername,
            NeedsUsername: user.NeedsUsername,
            CanSetUsername: user.CanSetUsername,
            IsAdmin: user.HasPermission(Permissions.AdminUsers)));
    }

    private static async Task<Results<NoContent, BadRequest<string>>> SetEmail(
        AuthenticatedUser user,
        SetEmailRequest request,
        UserStatements userStatements)
    {
        if (!EmailAddress.TryNormalize(request.Email, out string? email) || email is null)
        {
            return TypedResults.BadRequest("Enter a valid email address.");
        }

        await userStatements.SetUserEmail(user.Id, email);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Sets the username of an account whose owner chooses it (<see cref="AuthenticatedUser.CanSetUsername"/>);
    /// every other account's follows its primary sign-in, so this is a 400 for them.
    /// </summary>
    private static async Task<Results<NoContent, BadRequest<string>, Conflict<string>>> SetUsername(
        AuthenticatedUser user,
        SetUsernameRequest request,
        UserStatements userStatements)
    {
        if (!user.CanSetUsername)
        {
            return TypedResults.BadRequest("Your username comes from your primary sign-in.");
        }

        if (!Usernames.TryNormalize(request.Username, out string username))
        {
            return TypedResults.BadRequest(
                $"Use {Usernames.MinLength} to {Usernames.MaxLength} letters, numbers, underscores or full stops.");
        }

        if (!await userStatements.SetChosenUsername(user.Id, username))
        {
            return TypedResults.Conflict("Someone already has that username.");
        }

        return TypedResults.NoContent();
    }

    /// <summary>The caller's tier, its limits and usage, and its Stripe subscription (ADR-0007).</summary>
    private static async Task<Ok<AccountPlanResponse>> GetPlan(
        AuthenticatedUser user,
        AccountPlanService planService,
        IBillingStore billingStore,
        IPlanEnforcementStore accounts,
        IOptionsMonitor<PlanOptions> planOptions,
        FeatureFlagService featureFlags,
        HttpContext context)
    {
        AccountPlan plan = await planService.GetPlan(user);
        OverLimitState overLimit = await accounts.GetState(user.Id);
        List<BillingSubscription> subscriptions = await billingStore.GetSubscriptions(user.Id);
        bool billingEnabled = context.RequestServices.GetService<BillingService>() is not null;
        bool paidPlansOffered = billingEnabled &&
                                await featureFlags.IsEnabledAsync(FeatureFlagKeys.PaidPlans, user,
                                    context.RequestAborted);
        bool hasCustomer = await billingStore.GetCustomerId(user.Id) is not null;
        PlanOptions options = planOptions.CurrentValue;
        return TypedResults.Ok(AccountPlanResponse.From(plan, subscriptions, billingEnabled, paidPlansOffered,
            hasCustomer,
            overLimit.OverLimitSince + options.GracePeriod, options.Enforce ? options.GracePeriod : null,
            context.RequestServices.GetService<BillingOptions>()?.CanChangePlan ?? false, user.GrantedTier));
    }

    /// <summary>The feature flags on for the caller (ADR-0009), which the frontend reads to show what is being rolled out.</summary>
    private static async Task<Ok<FeatureFlagsResponse>> GetFeatures(
        AuthenticatedUser user,
        FeatureFlagService featureFlags,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(new FeatureFlagsResponse(await featureFlags.GetEnabledAsync(user, cancellationToken)));
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
        Moderation.ContentReportStatements contentReports,
        IOptionsMonitor<Moderation.ModerationOptions> moderation,
        HttpContext context)
    {
        if (!AccountDeletionService.IsConfirmed(request.Confirmation))
        {
            return TypedResults.BadRequest($"Type \"{AccountDeletionService.ConfirmationPhrase}\" to confirm.");
        }

        // One of its clips may be evidence (see ClipService.DeleteClip); the message gives nothing away.
        if (await contentReports.IsHeldForReview(ownerId: user.Id))
        {
            throw new Exceptions.ConflictException(
                $"Your account can't be deleted right now. Email {moderation.CurrentValue.ContactEmail}.");
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
    bool NeedsUsername,
    bool CanSetUsername,
    bool IsAdmin);

/// <summary>Every account needs an address, so an empty one is refused.</summary>
public sealed record SetEmailRequest(string? Email);

public sealed record SetUsernameRequest(string? Username);

/// <summary><see cref="Confirmation"/> must be the phrase the user was asked to type.</summary>
public sealed record DeleteAccountRequest(string? Confirmation);
