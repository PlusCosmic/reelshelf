using Reelshelf.Billing;

namespace Reelshelf.Plans;

/// <summary>
/// The signed-in account's plan (<c>GET /api/me/plan</c>): its tier, limits and usage, and its subscription. Null
/// limits are no limit. <see cref="FreeClipLimit"/> and <see cref="OverLimitGraceDays"/> let the page say what
/// ending a plan would cost: the clips over Free's limit, deleted that many days after it ends (null while
/// <c>Plans:Enforce</c> is off, when nothing is deleted).
/// </summary>
public sealed record AccountPlanResponse(
    string Tier,
    int ClipCount,
    int? ClipLimit,
    int NewClipsLast30Days,
    int? NewClipLimit,
    int? MaxClipSeconds,
    long StorageUsedBytes,
    bool HasTranscripts,
    bool BillingEnabled,
    bool CanManageBilling,
    PlanSubscription? Subscription,
    int ClipsOverLimit,
    DateTimeOffset? OverLimitDeletesAt,
    int? FreeClipLimit,
    int? OverLimitGraceDays)
{
    public static AccountPlanResponse From(AccountPlan plan, IReadOnlyList<BillingSubscription> subscriptions,
        bool billingEnabled, bool hasCustomer, DateTimeOffset? overLimitDeletesAt = null,
        TimeSpan? overLimitGrace = null)
    {
        // The subscription that grants the tier, else the latest one that could still bill (unpaid, paused), else
        // the latest of any kind, so a lapsed plan still shows when it ended.
        BillingSubscription? shown =
            subscriptions.Where(subscription => SubscriptionStatuses.GrantsTier(subscription.Status))
                .OrderByDescending(subscription => subscription.Tier)
                .FirstOrDefault()
            ?? subscriptions.FirstOrDefault(subscription => SubscriptionStatuses.IsOpen(subscription.Status))
            ?? subscriptions.FirstOrDefault();

        return new AccountPlanResponse(
            BillingStatements.TierName(plan.Tier),
            plan.ClipCount,
            plan.Limits.MaxClips,
            plan.NewClipsLast30Days,
            plan.Limits.MaxNewClipsPer30Days,
            plan.Limits.MaxClipSeconds,
            plan.StorageUsedBytes,
            TierLimits.HasTranscripts(plan.Tier),
            billingEnabled,
            billingEnabled && hasCustomer,
            shown is null ? null : PlanSubscription.From(shown),
            plan.Limits.MaxClips is { } limit ? Math.Max(0, plan.ClipCount - limit) : 0,
            overLimitDeletesAt,
            TierLimits.For(AccountTier.Free).MaxClips,
            overLimitGrace is { } grace ? (int)Math.Ceiling(grace.TotalDays) : null);
    }
}

/// <summary>A Stripe subscription as the account sees it.</summary>
public sealed record PlanSubscription(
    string Status,
    string? Tier,
    string? Interval,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelAt,
    DateTimeOffset? EndedAt)
{
    public static PlanSubscription From(BillingSubscription subscription)
    {
        return new PlanSubscription(
            subscription.Status,
            subscription.Tier is { } tier ? BillingStatements.TierName(tier) : null,
            BillingPlans.Find(subscription.LookupKey)?.Interval,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CancelAt,
            subscription.EndedAt);
    }
}
