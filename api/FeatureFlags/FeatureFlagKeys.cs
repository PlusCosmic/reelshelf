namespace Reelshelf.FeatureFlags;

/// <summary>
/// Every feature flag the code checks (ADR-0009), by its key in PostHog. A flag is temporary: once its feature is out
/// to everyone, its constant goes, so every check of it stops compiling and is removed with it. The frontend mirrors
/// this list in <c>shared/services/featureFlags.ts</c>.
/// </summary>
public static class FeatureFlagKeys
{
    // Add a flag as a constant here and in All.

    /// <summary>
    /// Buying a paid plan: Checkout, changing plan, and the prices and upgrades Settings, <c>/plan</c> and the uploader
    /// offer. Off leaves an account as it is: a subscriber keeps their plan and Manage billing, and Stripe's webhook
    /// still syncs it.
    /// </summary>
    public const string PaidPlans = "paid-plans";

    /// <summary>The flags <c>GET /api/me/features</c> answers about; a key PostHog has but this list lacks is ignored.</summary>
    public static readonly IReadOnlyList<string> All = [PaidPlans];

    public static bool IsKnown(string key)
    {
        return All.Contains(key, StringComparer.Ordinal);
    }
}
