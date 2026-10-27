using PostHog;
using PostHog.Features;

namespace Reelshelf.FeatureFlags;

/// <summary>Decides which of some flags are on for a subject. Overrides are applied before this is asked.</summary>
public interface IFeatureFlagEvaluator
{
    Task<IReadOnlySet<string>> GetEnabledAsync(FeatureFlagSubject subject, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);
}

/// <summary>Used without a PostHog key (dev, local runs, tests): every flag is off unless overridden.</summary>
public sealed class NoFeatureFlagEvaluator : IFeatureFlagEvaluator
{
    public Task<IReadOnlySet<string>> GetEnabledAsync(FeatureFlagSubject subject, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}

/// <summary>
/// Evaluates flags in-process from the definitions the PostHog client last downloaded (every
/// <c>PostHog:FeatureFlagPollInterval</c>). Only locally: a flag that can't be decided here is off rather than asked of
/// PostHog, so a check never makes a network call, never costs a flag request and never sends the account's
/// properties anywhere. <c>GetAllFeatureFlagsAsync</c> sends no <c>$feature_flag_called</c> events. With no definitions
/// downloaded yet, or the download failing, the client answers nothing, so every flag reads as off.
/// </summary>
public sealed class PostHogFeatureFlagEvaluator(IPostHogClient posthog) : IFeatureFlagEvaluator
{
    public async Task<IReadOnlySet<string>> GetEnabledAsync(FeatureFlagSubject subject,
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, FeatureFlag> flags = await posthog.GetAllFeatureFlagsAsync(subject.DistinctId,
            new AllFeatureFlagsOptions
            {
                OnlyEvaluateLocally = true,
                DisableGeoIp = true,
                FlagKeysToEvaluate = keys.ToList(),
                PersonProperties = subject.PersonProperties()
            },
            cancellationToken);

        return flags
            .Where(flag => flag.Value.IsEnabled && keys.Contains(flag.Key))
            .Select(flag => flag.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}
