using Microsoft.Extensions.Options;
using Reelshelf.Auth;

namespace Reelshelf.FeatureFlags;

/// <summary>
/// The one place code asks whether a flag is on (ADR-0009). A flag is for rolling a feature out, never for access: a
/// Pro-only feature still checks the tier, and an admin page its permission. An override in
/// <c>FeatureFlags:Overrides</c> wins; anything else goes to the evaluator (PostHog in prod, off without a key), and
/// an evaluator that fails leaves the flag off. Scoped, so a request works out its subject once however many flags
/// it checks.
/// </summary>
public sealed class FeatureFlagService(
    IFeatureFlagEvaluator evaluator,
    IFeatureFlagSubjects subjects,
    IOptionsMonitor<FeatureFlagOptions> options,
    ILogger<FeatureFlagService> logger)
{
    private readonly Dictionary<Guid, FeatureFlagSubject?> _subjects = [];

    public async Task<bool> IsEnabledAsync(string key, AuthenticatedUser user,
        CancellationToken cancellationToken = default)
    {
        return (await GetEnabledAsync([key], () => SubjectFor(user), cancellationToken)).Contains(key);
    }

    /// <summary>For work outside a request, such as a background service; off for an account that no longer exists.</summary>
    public async Task<bool> IsEnabledAsync(string key, Guid accountId, CancellationToken cancellationToken = default)
    {
        return (await GetEnabledAsync([key], () => SubjectFor(accountId), cancellationToken)).Contains(key);
    }

    /// <summary>Every known flag that is on for the user, in <see cref="FeatureFlagKeys.All"/> order.</summary>
    public async Task<List<string>> GetEnabledAsync(AuthenticatedUser user, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> enabled = await GetEnabledAsync(FeatureFlagKeys.All, () => SubjectFor(user),
            cancellationToken);
        return FeatureFlagKeys.All.Where(enabled.Contains).ToList();
    }

    private async Task<IReadOnlySet<string>> GetEnabledAsync(IReadOnlyCollection<string> keys,
        Func<Task<FeatureFlagSubject?>> subject, CancellationToken cancellationToken)
    {
        Dictionary<string, bool> overrides = options.CurrentValue.Overrides;
        HashSet<string> enabled = keys.Where(key => overrides.GetValueOrDefault(key)).ToHashSet(StringComparer.Ordinal);
        List<string> toEvaluate = keys.Where(key => !overrides.ContainsKey(key)).ToList();
        if (toEvaluate.Count == 0)
        {
            return enabled;
        }

        try
        {
            if (await subject() is { } resolved)
            {
                enabled.UnionWith(await evaluator.GetEnabledAsync(resolved, toEvaluate, cancellationToken));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Feature flags {Keys} couldn't be evaluated; treating them as off",
                string.Join(", ", toEvaluate));
        }

        return enabled;
    }

    private async Task<FeatureFlagSubject?> SubjectFor(AuthenticatedUser user)
    {
        if (!_subjects.TryGetValue(user.Id, out FeatureFlagSubject? subject))
        {
            subject = await subjects.ForUser(user);
            _subjects[user.Id] = subject;
        }

        return subject;
    }

    private async Task<FeatureFlagSubject?> SubjectFor(Guid accountId)
    {
        if (!_subjects.TryGetValue(accountId, out FeatureFlagSubject? subject))
        {
            subject = await subjects.ForAccount(accountId);
            _subjects[accountId] = subject;
        }

        return subject;
    }
}
