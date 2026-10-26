using Reelshelf.Billing;
using Reelshelf.Exceptions;

namespace Reelshelf.Auth;

/// <summary>The fields an <see cref="AccountAccessChange"/> records, as stored.</summary>
public static class AccessFields
{
    public const string Role = "role";
    public const string GrantedTier = "granted_tier";

    /// <summary>Recorded with <c>suspended</c> as the new value when suspended, and as the old one when lifted.</summary>
    public const string Suspended = "suspended";
}

/// <summary>Roles as <c>app_user.role</c> stores them: the enum's names.</summary>
public static class UserRoles
{
    /// <summary>
    /// The stored role; a value it doesn't recognise falls back to Viewer, so a bad value never grants anything.
    /// New accounts get Editor from the column default.
    /// </summary>
    public static UserRole Parse(string? stored)
    {
        return TryParse(stored) ?? UserRole.Viewer;
    }

    /// <summary>A role by name, case-insensitively; null for anything else, numbers included.</summary>
    public static UserRole? TryParse(string? name)
    {
        foreach (UserRole role in Enum.GetValues<UserRole>())
        {
            if (string.Equals(role.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return role;
            }
        }

        return null;
    }
}

/// <summary>
/// The tiers that can be granted (ADR-0008), stored lowercase in <c>app_user.granted_tier</c> like
/// <c>billing_subscription.tier</c>. Free is no grant at all.
/// </summary>
public static class GrantedTiers
{
    public static readonly IReadOnlyList<AccountTier> All = [AccountTier.Plus, AccountTier.Pro, AccountTier.Unlimited];

    public static string Name(AccountTier tier)
    {
        return tier.ToString().ToLowerInvariant();
    }

    /// <summary>A grantable tier by name; null for anything else, Free included.</summary>
    public static AccountTier? Parse(string? name)
    {
        foreach (AccountTier tier in All)
        {
            if (string.Equals(Name(tier), name, StringComparison.OrdinalIgnoreCase))
            {
                return tier;
            }
        }

        return null;
    }
}

/// <summary>
/// Changes an account's role, granted tier or suspension (ADR-0008). Nothing else writes them, and every change is recorded with
/// who made it and why. An admin can't change their own role, and the last Admin can't be demoted, so the admin pages
/// can't lock everyone out.
/// </summary>
public sealed class AccountAccessService(IAccountAccessStore store)
{
    public const int MaxReasonLength = 500;

    public async Task SetRole(AuthenticatedUser admin, Guid userId, UserRole role, string? reason)
    {
        string why = RequireReason(reason);
        if (admin.Id == userId)
        {
            throw new BadRequestException("You can't change your own role.");
        }

        Check(await store.SetRole(userId, role, admin.Id, why));
    }

    /// <summary>Grants <paramref name="tier"/>, or takes the grant away when it is null.</summary>
    public async Task SetGrantedTier(AuthenticatedUser admin, Guid userId, AccountTier? tier, string? reason)
    {
        string why = RequireReason(reason);
        if (tier is { } value && !GrantedTiers.All.Contains(value))
        {
            throw new BadRequestException("Only Plus, Pro or Unlimited can be granted.");
        }

        Check(await store.SetGrantedTier(userId, tier, admin.Id, why));
    }

    /// <summary>
    /// Suspends the account, so it can't sign in and its share links stop working, or lifts the suspension. Its clips
    /// are kept either way. An admin can't suspend themselves or another Admin.
    /// </summary>
    public async Task SetSuspended(AuthenticatedUser admin, Guid userId, bool suspended, string? reason)
    {
        string why = RequireReason(reason);
        if (admin.Id == userId)
        {
            throw new BadRequestException("You can't suspend your own account.");
        }

        Check(await store.SetSuspended(userId, suspended, admin.Id, why));
    }

    private static void Check(AccessChangeOutcome outcome)
    {
        switch (outcome)
        {
            case AccessChangeOutcome.NotFound:
                throw new NotFoundException("Account not found.");
            case AccessChangeOutcome.LastAdmin:
                throw new ConflictException("This is the last Admin. Make someone else an Admin first.");
            case AccessChangeOutcome.IsAdmin:
                throw new ConflictException("Admins can't be suspended. Change their role first.");
        }
    }

    private static string RequireReason(string? reason)
    {
        string why = reason?.Trim() ?? "";
        if (why.Length == 0)
        {
            throw new BadRequestException("Give a reason for the change.");
        }

        if (why.Length > MaxReasonLength)
        {
            throw new BadRequestException($"Keep the reason to {MaxReasonLength} characters.");
        }

        return why;
    }
}
