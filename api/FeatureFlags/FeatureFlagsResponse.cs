namespace Reelshelf.FeatureFlags;

/// <summary>The keys of the flags on for the caller; a flag that is off, or unknown to this build, is absent.</summary>
public sealed record FeatureFlagsResponse(List<string> Enabled);
