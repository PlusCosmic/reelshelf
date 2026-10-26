namespace Reelshelf.Billing;

/// <summary>
/// What an account may keep and use (ADR-0007). Free is the default, Plus and Pro come from a Stripe subscription,
/// and any but Free can be granted by an admin (ADR-0008). Ordered so a higher value is a better tier.
/// </summary>
public enum AccountTier
{
    Free,
    Plus,
    Pro,
    Unlimited
}

/// <summary>A paid plan: the tier it grants and how often it bills.</summary>
public sealed record BillingPlan(string LookupKey, AccountTier Tier, string Interval);

/// <summary>
/// The paid plans, by the lookup key their Stripe price carries. The API finds prices by these keys, never by
/// Stripe's ids, so a price can be replaced in Stripe (moving the key to the new price) without a deploy.
/// </summary>
public static class BillingPlans
{
    public const string Monthly = "month";
    public const string Yearly = "year";

    public static readonly IReadOnlyList<BillingPlan> All =
    [
        new("plus_monthly", AccountTier.Plus, Monthly),
        new("plus_yearly", AccountTier.Plus, Yearly),
        new("pro_monthly", AccountTier.Pro, Monthly),
        new("pro_yearly", AccountTier.Pro, Yearly)
    ];

    public static BillingPlan? Find(string? lookupKey)
    {
        return All.FirstOrDefault(plan => string.Equals(plan.LookupKey, lookupKey, StringComparison.Ordinal));
    }
}

/// <summary>
/// Stripe's subscription statuses as they matter here. <c>past_due</c> still grants its tier while Stripe retries
/// the card; <c>unpaid</c> and <c>paused</c> don't, but the subscription still exists, so the owner is sent to the
/// Customer Portal rather than to a second checkout.
/// </summary>
public static class SubscriptionStatuses
{
    public static bool GrantsTier(string status)
    {
        return status is "active" or "trialing" or "past_due";
    }

    /// <summary>
    /// True while the subscription could still bill. <c>incomplete</c> is left out: it is a checkout whose first
    /// payment never went through, and it expires on its own within a day.
    /// </summary>
    public static bool IsOpen(string status)
    {
        return status is not ("canceled" or "incomplete" or "incomplete_expired");
    }
}

public static class AccountTiers
{
    /// <summary>
    /// The better of the account's granted tier (ADR-0008) and the best tier any of its subscriptions grants, else
    /// Free. The result is never stored, so nothing has to be kept in step.
    /// </summary>
    public static AccountTier Resolve(AccountTier? granted, IEnumerable<BillingSubscription> subscriptions)
    {
        AccountTier tier = granted ?? AccountTier.Free;
        foreach (BillingSubscription subscription in subscriptions)
        {
            if (subscription.Tier is { } paid && SubscriptionStatuses.GrantsTier(subscription.Status) && paid > tier)
            {
                tier = paid;
            }
        }

        return tier;
    }
}
