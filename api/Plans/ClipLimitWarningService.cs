using Reelshelf.Email;

namespace Reelshelf.Plans;

/// <summary>
/// Sends the "clip limit nearly reached" notice once per crossing of <c>Storage:WarnAtPercent</c> (default 90) of the
/// plan's clip limit. The account's <c>storage_warned_at</c> marker stops repeats; it is cleared when the count drops
/// back under the threshold (fewer clips, or a bigger plan) so the next crossing warns again.
/// </summary>
public sealed class ClipLimitWarningService(
    IClipLimitWarningStore store,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<ClipLimitWarningService> logger)
{
    public const int DefaultWarnAtPercent = 90;

    private readonly int _warnAtPercent =
        Math.Clamp(configuration.GetValue<int?>("Storage:WarnAtPercent") ?? DefaultWarnAtPercent, 1, 100);

    private readonly string _libraryUrl = (configuration["FrontendOrigin"] ?? "http://localhost:5173").TrimEnd('/') + "/";

    /// <summary>Evaluates the account after its clip count changed. Never throws.</summary>
    public async Task Evaluate(Guid userId, AccountPlan plan)
    {
        try
        {
            if (plan.Limits.MaxClips is not { } limit)
            {
                return;
            }

            int threshold = (int)Math.Ceiling(limit * (_warnAtPercent / 100.0));
            bool overThreshold = plan.ClipCount >= threshold;
            ClipLimitWarningState state = await store.GetState(userId);

            // The store re-checks the live count inside the same statement that flips the marker, so a stale
            // snapshot from a request that raced a concurrent upload or deletion cannot set or clear it wrongly.
            if (!overThreshold)
            {
                if (state.WarnedAt is not null)
                {
                    await store.ClearWarned(userId, threshold);
                }

                return;
            }

            if (state.WarnedAt is not null)
            {
                return;
            }

            // Only the request that wins the claim sends, so concurrent uploads crossing the threshold
            // together produce one notice.
            if (!await store.MarkWarned(userId, threshold))
            {
                return;
            }

            if (!string.IsNullOrEmpty(state.Email))
            {
                await emailSender.SendAsync(AccountEmails.ClipLimitNearlyReached(state.Email, plan, _libraryUrl));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Clip limit warning evaluation failed for user {UserId}", userId);
        }
    }
}

public sealed record ClipLimitWarningState(string? Email, DateTimeOffset? WarnedAt);

public interface IClipLimitWarningStore
{
    Task<ClipLimitWarningState> GetState(Guid userId);

    /// <summary>
    /// Sets the marker if it is unset and the account has at least <paramref name="thresholdClips"/> clips now;
    /// false when another request got there first or the count has since dropped.
    /// </summary>
    Task<bool> MarkWarned(Guid userId, int thresholdClips);

    /// <summary>Clears the marker only if the account has fewer than <paramref name="thresholdClips"/> clips now.</summary>
    Task ClearWarned(Guid userId, int thresholdClips);
}
