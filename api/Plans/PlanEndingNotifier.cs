using Microsoft.Extensions.Options;
using Reelshelf.Billing;
using Reelshelf.Email;

namespace Reelshelf.Plans;

/// <summary>
/// Mails the owner as soon as a paid plan is set to end, when they have more clips than Free keeps: those clips get
/// the grace after the plan ends, and this gives them the paid period too to download or pick them.
/// </summary>
public sealed class PlanEndingNotifier(
    IAccountPlans plans,
    IPlanEnforcementStore accounts,
    IEmailSender emailSender,
    IOptionsMonitor<PlanOptions> options,
    IConfiguration configuration) : IPlanEndingNotifier
{
    private readonly string _planUrl = ClipExportProcessor.PlanUrl(configuration);

    public async Task SubscriptionEndingAsync(Guid userId, BillingSubscription subscription, DateTimeOffset endsAt,
        CancellationToken cancellationToken)
    {
        AccountPlan plan = await plans.GetPlan(userId);
        int freeLimit = TierLimits.For(AccountTier.Free).MaxClips!.Value;
        if (plan.Tier == AccountTier.Unlimited || plan.ClipCount <= freeLimit || subscription.Tier is not { } tier)
        {
            return;
        }

        OverLimitState state = await accounts.GetState(userId);
        if (string.IsNullOrEmpty(state.Email))
        {
            return;
        }

        string tierName = tier.ToString();
        await emailSender.SendAsync(AccountEmails.PlanEnding(state.Email, tierName, endsAt, plan.ClipCount - freeLimit,
            options.CurrentValue.GracePeriod, _planUrl), cancellationToken);
    }
}
