using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.Core;
using Reelshelf.Exceptions;

namespace Reelshelf.Plans;

/// <summary>
/// Works out an account's plan and enforces it when a clip is created. Tiers limit clips, not bytes; the byte
/// backstop (<c>Storage:BackstopBytes</c>, 1 TiB by default, never for Unlimited) only stops a pathological file or a
/// bug from running up the bill.
/// </summary>
public sealed class AccountPlanService(
    AccountTierService tiers,
    ClipsStatements clips,
    TimeProvider timeProvider,
    IConfiguration configuration) : IAccountPlans
{
    /// <summary>Largest file size a client may declare. Bounds bogus reservations so per-owner sums stay well inside int64.</summary>
    public const long MaxDeclaredFileSizeBytes = 1024L * 1024 * 1024 * 1024;

    public const long DefaultBackstopBytes = 1024L * 1024 * 1024 * 1024;

    private readonly long _backstopBytes =
        configuration.GetValue<long?>("Storage:BackstopBytes") ?? DefaultBackstopBytes;

    public async Task<AccountPlan> GetPlan(AuthenticatedUser user)
    {
        return await GetPlan(user.Id, await tiers.GetTier(user));
    }

    public async Task<AccountPlan> GetPlan(Guid userId)
    {
        return await GetPlan(userId, await tiers.GetTier(userId));
    }

    private async Task<AccountPlan> GetPlan(Guid userId, AccountTier tier)
    {
        ClipsStatements.OwnerClipUsage usage =
            await clips.GetOwnerClipUsage(userId, timeProvider.GetUtcNow() - TierLimits.NewClipWindow);
        return new AccountPlan(tier, TierLimits.For(tier), usage.ClipCount, usage.NewClips, usage.StorageBytes);
    }

    /// <summary>
    /// Throws <see cref="PlanLimitExceededException"/> when the account can't add a clip of this size and, when it is
    /// known before the upload (a Twitch import), this length. Called once before the per-owner lock and again
    /// inside it, where the counts can't change underneath.
    /// </summary>
    public async Task EnsureCanCreateClip(Guid userId, long fileSize, double? durationSeconds)
    {
        AccountPlan plan = await GetPlan(userId);
        string? problem = plan.NewClipProblem() ?? plan.Limits.LengthProblem(durationSeconds);
        if (problem is null && plan.Tier != AccountTier.Unlimited && fileSize > _backstopBytes - plan.StorageUsedBytes)
        {
            problem = "This account has reached its storage limit. Delete some clips to make room.";
        }

        if (problem is not null)
        {
            throw new PlanLimitExceededException(problem);
        }
    }
}
