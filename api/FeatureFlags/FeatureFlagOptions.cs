namespace Reelshelf.FeatureFlags;

/// <summary>Settings that apply on any instance, PostHog or not (<c>FeatureFlags:*</c>).</summary>
public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// Fixes a flag's value on this instance without asking PostHog (<c>FeatureFlags:Overrides:&lt;key&gt;</c>,
    /// <c>true</c> or <c>false</c>). Dev, local runs and tests use it; prod sets none, since an override there wins
    /// over PostHog for every account.
    /// </summary>
    public Dictionary<string, bool> Overrides { get; set; } = new(StringComparer.Ordinal);
}
