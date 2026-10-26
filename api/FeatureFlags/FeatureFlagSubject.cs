using Reelshelf.Auth;
using Reelshelf.Billing;

namespace Reelshelf.FeatureFlags;

/// <summary>
/// Who a flag is checked for: the account id PostHog knows it by, and the properties a flag's conditions can target.
/// Nothing else about the account is sent (ADR-0009), and these are worked out from the database on every check, so a
/// role or granted tier changed at <c>/admin/accounts</c> applies on the account's next request.
/// </summary>
public sealed record FeatureFlagSubject(Guid AccountId, UserRole Role, AccountTier Tier, AccountTier? GrantedTier)
{
    public string DistinctId => AccountId.ToString();

    /// <summary>The person properties PostHog evaluates conditions against, in lower case like the rest of the API.</summary>
    public Dictionary<string, object?> PersonProperties()
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = Role.ToString().ToLowerInvariant(),
            ["tier"] = Tier.ToString().ToLowerInvariant(),
            ["granted_tier"] = GrantedTier is { } granted ? GrantedTiers.Name(granted) : "none"
        };
    }
}

public interface IFeatureFlagSubjects
{
    Task<FeatureFlagSubject> ForUser(AuthenticatedUser user);

    /// <summary>For work outside a request; null when the account no longer exists.</summary>
    Task<FeatureFlagSubject?> ForAccount(Guid accountId);
}

public sealed class FeatureFlagSubjects(AccountTierService tiers, IAccountAccessStore access) : IFeatureFlagSubjects
{
    public async Task<FeatureFlagSubject> ForUser(AuthenticatedUser user)
    {
        return new FeatureFlagSubject(user.Id, user.Role, await tiers.GetTier(user), user.GrantedTier);
    }

    public async Task<FeatureFlagSubject?> ForAccount(Guid accountId)
    {
        AccountAccessRow? account = await access.GetAccount(accountId);
        if (account is null)
        {
            return null;
        }

        return new FeatureFlagSubject(account.Id, account.Role, await tiers.GetTier(accountId), account.GrantedTier);
    }
}
