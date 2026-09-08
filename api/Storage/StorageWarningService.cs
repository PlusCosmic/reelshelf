using Reelshelf.Email;

namespace Reelshelf.Storage;

/// <summary>
/// Sends the "storage nearly full" notice once per crossing of <c>Storage:WarnAtPercent</c> (default 90).
/// The account's <c>storage_warned_at</c> marker stops repeats; it is cleared when usage drops back under the
/// threshold so the next crossing warns again.
/// </summary>
public sealed class StorageWarningService(
    IStorageWarningStore store,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<StorageWarningService> logger)
{
    public const int DefaultWarnAtPercent = 90;

    private readonly int _warnAtPercent =
        Math.Clamp(configuration.GetValue<int?>("Storage:WarnAtPercent") ?? DefaultWarnAtPercent, 1, 100);

    private readonly string _libraryUrl = (configuration["FrontendOrigin"] ?? "http://localhost:5173").TrimEnd('/') + "/";

    /// <summary>Evaluates the account after its usage changed. Never throws.</summary>
    public async Task Evaluate(Guid userId, StorageQuota quota)
    {
        try
        {
            if (quota.LimitBytes is not { } limit)
            {
                return;
            }

            bool overThreshold = quota.UsedBytes * 100 >= limit * (long)_warnAtPercent;
            StorageWarningState state = await store.GetState(userId);

            if (!overThreshold)
            {
                if (state.WarnedAt is not null)
                {
                    await store.ClearWarned(userId);
                }

                return;
            }

            if (state.WarnedAt is not null)
            {
                return;
            }

            // Mark first so a slow or failing send can't fire twice on concurrent uploads.
            await store.MarkWarned(userId);
            if (!string.IsNullOrEmpty(state.Email))
            {
                await emailSender.SendAsync(AccountEmails.StorageNearlyFull(state.Email, quota, _libraryUrl));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Storage warning evaluation failed for user {UserId}", userId);
        }
    }
}

public sealed record StorageWarningState(string? Email, DateTimeOffset? WarnedAt);

public interface IStorageWarningStore
{
    Task<StorageWarningState> GetState(Guid userId);
    Task MarkWarned(Guid userId);
    Task ClearWarned(Guid userId);
}
