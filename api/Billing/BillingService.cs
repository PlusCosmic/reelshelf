using Microsoft.Extensions.Caching.Memory;
using Reelshelf.Auth;

namespace Reelshelf.Billing;

public enum CheckoutOutcome
{
    Created,
    UnknownPlan,
    AlreadySubscribed,
    PlanUnavailable
}

public sealed record CheckoutResult(CheckoutOutcome Outcome, string? Url = null);

public enum PlanChangeOutcome
{
    /// <summary>An upgrade: <see cref="PlanChangeResult.Url"/> is Stripe's page confirming it.</summary>
    Confirm,

    /// <summary>A downgrade, scheduled for <see cref="PlanChangeResult.At"/>, the end of the paid period.</summary>
    Scheduled,

    /// <summary>Already on that plan.</summary>
    SamePlan,
    UnknownPlan,

    /// <summary>No active subscription to change; Checkout starts one.</summary>
    NotSubscribed,

    /// <summary>A payment is failing or the plan is set to end; that is dealt with first, in the Customer Portal.</summary>
    NotChangeable,

    /// <summary>Changing plan isn't configured, or the plan has no active price.</summary>
    Unavailable
}

public sealed record PlanChangeResult(PlanChangeOutcome Outcome, string? Url = null, DateTimeOffset? At = null);

/// <summary>Cancels an account's subscriptions when the account is deleted; does nothing while billing is off.</summary>
public interface IAccountSubscriptions
{
    Task CancelAllAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Told when a subscription is newly set to end, so the owner hears what happens to clips over the limit.</summary>
public interface IPlanEndingNotifier
{
    Task SubscriptionEndingAsync(Guid userId, BillingSubscription subscription, DateTimeOffset endsAt,
        CancellationToken cancellationToken);
}

public sealed class NoPlanEndingNotifier : IPlanEndingNotifier
{
    public Task SubscriptionEndingAsync(Guid userId, BillingSubscription subscription, DateTimeOffset endsAt,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NoAccountSubscriptions : IAccountSubscriptions
{
    public Task CancelAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Starts Checkout and the Customer Portal, and records subscriptions from Stripe's webhooks (ADR-0007). Only the
/// webhook grants anything: the page Checkout returns to never does.
/// </summary>
public sealed class BillingService(
    IStripeBilling stripe,
    IBillingStore store,
    BillingOptions options,
    IMemoryCache cache,
    IPlanEndingNotifier planEnding,
    ILogger<BillingService> logger) : IAccountSubscriptions
{
    private const string PricesCacheKey = "billing:prices";

    /// <summary>How long the plans page trusts Stripe's prices; a price changed in Stripe shows within this.</summary>
    private static readonly TimeSpan PricesCacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>The paid plans' prices as Stripe has them, by lookup key. A plan without an active price is missing.</summary>
    public async Task<IReadOnlyDictionary<string, StripePlanPrice>> GetPrices(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(PricesCacheKey, out IReadOnlyDictionary<string, StripePlanPrice>? cached) && cached is not null)
        {
            return cached;
        }

        List<StripePlanPrice> prices = await stripe.ListPricesAsync(
            BillingPlans.All.Select(plan => plan.LookupKey).ToList(), cancellationToken);
        IReadOnlyDictionary<string, StripePlanPrice> byKey = prices
            .GroupBy(price => price.LookupKey)
            .ToDictionary(group => group.Key, group => group.First());
        cache.Set(PricesCacheKey, byKey, PricesCacheDuration);
        return byKey;
    }

    public async Task<CheckoutResult> StartCheckout(AuthenticatedUser user, string? lookupKey, CancellationToken cancellationToken)
    {
        BillingPlan? plan = BillingPlans.Find(lookupKey);
        if (plan is null)
        {
            return new CheckoutResult(CheckoutOutcome.UnknownPlan);
        }

        // One subscription per account: changing plan, or fixing a failing card, happens in the Customer Portal.
        // Under the account's lock, so two tabs can't each start one.
        await using IAsyncDisposable checkoutLock = await store.LockCheckouts(user.Id);
        if (await HasOpenSubscription(user.Id))
        {
            return new CheckoutResult(CheckoutOutcome.AlreadySubscribed);
        }

        // The webhook can lag the payment by seconds, so the account's last checkout is asked about directly.
        if (await store.GetCheckoutSessionId(user.Id) is { } previous &&
            await SettlePreviousCheckout(user.Id, previous, cancellationToken))
        {
            return new CheckoutResult(CheckoutOutcome.AlreadySubscribed);
        }

        StripeCheckoutSession? session = await stripe.CreateCheckoutSessionAsync(new CheckoutSessionRequest(
            plan,
            user.Id,
            await store.GetCustomerId(user.Id),
            user.Email,
            SuccessUrl: $"{options.FrontendOrigin}/settings?billing=success",
            CancelUrl: $"{options.FrontendOrigin}/settings?billing=cancelled"), cancellationToken);

        if (session?.Url is null)
        {
            logger.LogError("No active Stripe price has the lookup key {LookupKey}", plan.LookupKey);
            return new CheckoutResult(CheckoutOutcome.PlanUnavailable);
        }

        await store.SetCheckoutSessionId(user.Id, session.Id);
        return new CheckoutResult(CheckoutOutcome.Created, session.Url);
    }

    private async Task<bool> HasOpenSubscription(Guid userId) =>
        (await store.GetSubscriptions(userId)).Any(subscription => SubscriptionStatuses.IsOpen(subscription.Status));

    /// <summary>
    /// Makes sure the account's previous Checkout Session can't lead to a second subscription: an open one is
    /// expired, and a completed one's subscription is recorded now rather than when its webhook arrives. True when
    /// the account turns out to have an open subscription.
    /// </summary>
    private async Task<bool> SettlePreviousCheckout(Guid userId, string sessionId, CancellationToken cancellationToken)
    {
        StripeCheckoutSession? previous = await stripe.GetCheckoutSessionAsync(sessionId, cancellationToken);
        if (previous?.Status == "open" && !await stripe.ExpireCheckoutSessionAsync(sessionId, cancellationToken))
        {
            // Paid between the read and the expiry.
            previous = await stripe.GetCheckoutSessionAsync(sessionId, cancellationToken);
        }

        if (previous is not { Status: "complete", SubscriptionId: { } subscriptionId })
        {
            return false;
        }

        await SyncSubscription(subscriptionId, userId, cancellationToken);
        if (!await HasOpenSubscription(userId))
        {
            return false;
        }

        logger.LogInformation("Refused a second checkout for account {UserId}: session {SessionId} already paid",
            userId, sessionId);
        return true;
    }

    /// <summary>The Customer Portal's URL, or null when the account has never checked out.</summary>
    public async Task<string?> OpenPortal(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        string? customerId = await store.GetCustomerId(user.Id);
        if (customerId is null)
        {
            return null;
        }

        // Settings refetches the plan for a while after ?billing=portal, since changes made there arrive by webhook.
        return await stripe.CreatePortalSessionAsync(customerId, $"{options.FrontendOrigin}/settings?billing=portal",
            cancellationToken);
    }

    /// <summary>
    /// Changes the account's plan (ADR-0007). Moving to a better plan (a higher tier, or yearly from monthly) opens
    /// Stripe's page confirming it, which charges the prorated difference at once; moving down waits for the end of
    /// what is already paid for, through a subscription schedule, and any downgrade already scheduled is replaced.
    /// </summary>
    public async Task<PlanChangeResult> ChangePlan(AuthenticatedUser user, string? lookupKey,
        CancellationToken cancellationToken)
    {
        BillingPlan? target = BillingPlans.Find(lookupKey);
        if (target is null)
        {
            return new PlanChangeResult(PlanChangeOutcome.UnknownPlan);
        }

        if (!options.CanChangePlan)
        {
            return new PlanChangeResult(PlanChangeOutcome.Unavailable);
        }

        BillingSubscription? subscription = await ChangeableSubscription(user.Id);
        if (subscription is null)
        {
            return new PlanChangeResult(await HasOpenSubscription(user.Id)
                ? PlanChangeOutcome.NotChangeable
                : PlanChangeOutcome.NotSubscribed);
        }

        // Stripe's current state: the item to change and the schedule to rewrite aren't kept locally.
        StripeSubscriptionState? state = await stripe.GetSubscriptionAsync(subscription.Id, cancellationToken);
        BillingPlan? current = BillingPlans.Find(state?.LookupKey);
        if (state is null || current is null || state.ItemId is null ||
            await store.GetCustomerId(user.Id) is not { } customerId)
        {
            return new PlanChangeResult(PlanChangeOutcome.NotChangeable);
        }

        if (current.LookupKey == target.LookupKey)
        {
            if (state.ScheduleId is null)
            {
                return new PlanChangeResult(PlanChangeOutcome.SamePlan);
            }

            // Staying put: drop the downgrade waiting at the end of the period.
            await KeepPlan(user, cancellationToken);
            return new PlanChangeResult(PlanChangeOutcome.SamePlan);
        }

        if ((await GetPrices(cancellationToken)).GetValueOrDefault(target.LookupKey)?.Id is not { } priceId)
        {
            return new PlanChangeResult(PlanChangeOutcome.Unavailable);
        }

        if (!IsUpgrade(current, target))
        {
            await stripe.ScheduleDowngradeAsync(state, priceId, target.Interval, cancellationToken);
            await SyncSubscription(state.Id, user.Id, cancellationToken);
            logger.LogInformation("Scheduled account {UserId}'s move from {From} to {To} for {At}",
                user.Id, current.LookupKey, target.LookupKey, state.CurrentPeriodEnd);
            return new PlanChangeResult(PlanChangeOutcome.Scheduled, At: state.CurrentPeriodEnd);
        }

        // A subscription under a schedule can't be changed directly, and the upgrade replaces any downgrade.
        if (state.ScheduleId is { } scheduleId)
        {
            await stripe.ReleaseScheduleAsync(scheduleId, cancellationToken);
            await SyncSubscription(state.Id, user.Id, cancellationToken);
        }

        string url = await stripe.CreatePlanChangeSessionAsync(new PlanChangeSessionRequest(
            customerId,
            state.Id,
            state.ItemId,
            priceId,
            options.PlanChangePortalConfiguration,
            $"{options.FrontendOrigin}/settings?billing=portal"), cancellationToken);
        return new PlanChangeResult(PlanChangeOutcome.Confirm, url);
    }

    /// <summary>Drops a downgrade waiting for the end of the paid period. False when there was none.</summary>
    public async Task<bool> KeepPlan(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        BillingSubscription? subscription = (await store.GetSubscriptions(user.Id))
            .FirstOrDefault(candidate => SubscriptionStatuses.IsOpen(candidate.Status));
        if (subscription is null)
        {
            return false;
        }

        StripeSubscriptionState? state = await stripe.GetSubscriptionAsync(subscription.Id, cancellationToken);
        if (state?.ScheduleId is not { } scheduleId)
        {
            return false;
        }

        await stripe.ReleaseScheduleAsync(scheduleId, cancellationToken);
        await SyncSubscription(state.Id, user.Id, cancellationToken);
        logger.LogInformation("Account {UserId} kept {LookupKey}; its scheduled change is dropped", user.Id,
            state.LookupKey);
        return true;
    }

    /// <summary>A higher tier, or the same one paid yearly instead of monthly: charged now rather than waited for.</summary>
    internal static bool IsUpgrade(BillingPlan current, BillingPlan target) =>
        target.Tier > current.Tier ||
        (target.Tier == current.Tier && target.Interval == BillingPlans.Yearly && current.Interval == BillingPlans.Monthly);

    /// <summary>The subscription a plan change applies to: active, paying, and not set to end.</summary>
    private async Task<BillingSubscription?> ChangeableSubscription(Guid userId) =>
        (await store.GetSubscriptions(userId)).FirstOrDefault(subscription =>
            subscription.Status is "active" or "trialing" && !subscription.CancelAtPeriodEnd &&
            subscription.CancelAt is null);

    public async Task HandleWebhook(BillingWebhookEvent stripeEvent, CancellationToken cancellationToken)
    {
        if (!BillingWebhookEvent.HandledTypes.Contains(stripeEvent.Type))
        {
            return;
        }

        if (stripeEvent.Type == "checkout.session.completed" && stripeEvent.UserId is { } userId &&
            stripeEvent.CustomerId is { } customerId)
        {
            await store.LinkCustomer(userId, customerId);
        }

        if (stripeEvent.SubscriptionId is null)
        {
            // A one-off invoice, or a checkout that created no subscription.
            return;
        }

        await SyncSubscription(stripeEvent.SubscriptionId, stripeEvent.UserId, cancellationToken);
    }

    /// <summary>Reads the subscription from Stripe and records it against the account its customer belongs to.</summary>
    public async Task SyncSubscription(string subscriptionId, Guid? userIdHint, CancellationToken cancellationToken)
    {
        StripeSubscriptionState? state = await stripe.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (state is null)
        {
            logger.LogWarning("Stripe has no subscription {SubscriptionId}; nothing recorded", subscriptionId);
            return;
        }

        // The customer is linked by checkout.session.completed, but the subscription's own events can arrive
        // first; Checkout also wrote the account id to the subscription's metadata for that case.
        Guid? userId = await store.GetUserIdByCustomer(state.CustomerId);
        Guid? hinted = userIdHint ?? state.UserId;
        if (userId is null && hinted is not null)
        {
            await store.LinkCustomer(hinted.Value, state.CustomerId);
            userId = await store.GetUserIdByCustomer(state.CustomerId);
        }

        if (userId is null)
        {
            if (hinted is not null)
            {
                // Checked out for an account that has since been deleted: nobody could ever manage it.
                await CancelOrphan(state, hinted.Value, cancellationToken);
                return;
            }

            logger.LogWarning(
                "Stripe subscription {SubscriptionId} belongs to customer {CustomerId}, which no account has; nothing recorded",
                state.Id, state.CustomerId);
            return;
        }

        BillingPlan? plan = BillingPlans.Find(state.LookupKey);
        if (plan is null)
        {
            logger.LogWarning("Stripe subscription {SubscriptionId} is on price lookup key {LookupKey}, which grants no tier",
                state.Id, state.LookupKey);
        }

        BillingSubscription subscription = new(
            state.Id,
            userId.Value,
            state.Status,
            state.LookupKey,
            plan?.Tier,
            state.CurrentPeriodEnd,
            state.CancelAtPeriodEnd,
            state.CancelAt,
            state.EndedAt,
            state.ScheduledLookupKey,
            state.ScheduledAt);

        BillingSubscription? before = (await store.GetSubscriptions(userId.Value))
            .FirstOrDefault(existing => existing.Id == subscription.Id);
        if (!await store.UpsertSubscription(subscription))
        {
            await CancelOrphan(state, userId.Value, cancellationToken);
            return;
        }

        if (EndsAt(subscription) is { } endsAt && (before is null || EndsAt(before) is null) &&
            SubscriptionStatuses.GrantsTier(subscription.Status))
        {
            try
            {
                await planEnding.SubscriptionEndingAsync(userId.Value, subscription, endsAt, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Could not tell account {UserId} its plan ends", userId);
            }
        }

        logger.LogInformation("Stripe subscription {SubscriptionId} of account {UserId} is {Status} on {LookupKey}",
            state.Id, userId, state.Status, state.LookupKey);

        List<BillingSubscription> all = await store.GetSubscriptions(userId.Value);
        if (all.Count(other => SubscriptionStatuses.IsOpen(other.Status)) > 1)
        {
            // Two checkouts completed side by side. Both bill until the owner cancels one in the Customer Portal;
            // the account gets the better tier meanwhile.
            logger.LogWarning("Account {UserId} has more than one open Stripe subscription", userId);
        }
    }

    /// <summary>When a subscription that is set to end does, or null while it renews.</summary>
    private static DateTimeOffset? EndsAt(BillingSubscription subscription) =>
        subscription.CancelAt ?? (subscription.CancelAtPeriodEnd ? subscription.CurrentPeriodEnd : null);

    private async Task CancelOrphan(StripeSubscriptionState state, Guid userId, CancellationToken cancellationToken)
    {
        if (!SubscriptionStatuses.IsOpen(state.Status) && state.Status != "incomplete")
        {
            return;
        }

        await stripe.CancelSubscriptionAsync(state.Id, cancellationToken);
        logger.LogWarning("Account {UserId} of Stripe subscription {SubscriptionId} is deleted or being deleted; cancelled it",
            userId, state.Id);
    }

    public async Task CancelAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (BillingSubscription subscription in await store.GetSubscriptions(userId))
        {
            if (!SubscriptionStatuses.IsOpen(subscription.Status) && subscription.Status != "incomplete")
            {
                continue;
            }

            await stripe.CancelSubscriptionAsync(subscription.Id, cancellationToken);
            logger.LogInformation("Cancelled Stripe subscription {SubscriptionId} of deleted account {UserId}",
                subscription.Id, userId);
        }
    }
}

/// <summary>Works out an account's <see cref="AccountTier"/> from its granted tier and its subscriptions.</summary>
public sealed class AccountTierService(IBillingStore store, IAccountAccessStore access)
{
    public async Task<AccountTier> GetTier(AuthenticatedUser user)
    {
        return AccountTiers.Resolve(user.GrantedTier, await store.GetSubscriptions(user.Id));
    }

    public async Task<AccountTier> GetTier(Guid userId)
    {
        return AccountTiers.Resolve(await access.GetGrantedTier(userId), await store.GetSubscriptions(userId));
    }

    /// <summary>
    /// Of these accounts, the ones on <paramref name="minimum"/> or better, by a granted tier or a subscription.
    /// </summary>
    public async Task<List<Guid>> FilterAtLeast(AccountTier minimum, IEnumerable<Guid> accounts)
    {
        HashSet<Guid> atLeast = (await store.GetUserIdsWithTierAtLeast(minimum)).ToHashSet();
        atLeast.UnionWith(await access.GetUserIdsWithGrantedTierAtLeast(minimum));
        return accounts.Where(atLeast.Contains).ToList();
    }
}
