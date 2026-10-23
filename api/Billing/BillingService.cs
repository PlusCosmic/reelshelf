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

/// <summary>Cancels an account's subscriptions when the account is deleted; does nothing while billing is off.</summary>
public interface IAccountSubscriptions
{
    Task CancelAllAsync(Guid userId, CancellationToken cancellationToken);
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
    ILogger<BillingService> logger) : IAccountSubscriptions
{
    public async Task<CheckoutResult> StartCheckout(AuthenticatedUser user, string? lookupKey, CancellationToken cancellationToken)
    {
        BillingPlan? plan = BillingPlans.Find(lookupKey);
        if (plan is null)
        {
            return new CheckoutResult(CheckoutOutcome.UnknownPlan);
        }

        // One subscription per account: changing plan, or fixing a failing card, happens in the Customer Portal.
        List<BillingSubscription> subscriptions = await store.GetSubscriptions(user.Id);
        if (subscriptions.Any(subscription => SubscriptionStatuses.IsOpen(subscription.Status)))
        {
            return new CheckoutResult(CheckoutOutcome.AlreadySubscribed);
        }

        string? url = await stripe.CreateCheckoutSessionAsync(new CheckoutSessionRequest(
            plan,
            user.Id,
            await store.GetCustomerId(user.Id),
            user.Email,
            SuccessUrl: $"{options.FrontendOrigin}/settings?billing=success",
            CancelUrl: $"{options.FrontendOrigin}/settings?billing=cancelled"), cancellationToken);

        if (url is null)
        {
            logger.LogError("No active Stripe price has the lookup key {LookupKey}", plan.LookupKey);
            return new CheckoutResult(CheckoutOutcome.PlanUnavailable);
        }

        return new CheckoutResult(CheckoutOutcome.Created, url);
    }

    /// <summary>The Customer Portal's URL, or null when the account has never checked out.</summary>
    public async Task<string?> OpenPortal(AuthenticatedUser user, CancellationToken cancellationToken)
    {
        string? customerId = await store.GetCustomerId(user.Id);
        if (customerId is null)
        {
            return null;
        }

        return await stripe.CreatePortalSessionAsync(customerId, $"{options.FrontendOrigin}/settings", cancellationToken);
    }

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
            state.EndedAt);

        if (!await store.UpsertSubscription(subscription))
        {
            await CancelOrphan(state, userId.Value, cancellationToken);
            return;
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

/// <summary>Works out an account's <see cref="AccountTier"/> from <c>whitelist.json</c> and its subscriptions.</summary>
public sealed class AccountTierService(WhitelistService whitelist, IBillingStore store)
{
    public async Task<AccountTier> GetTier(AuthenticatedUser user)
    {
        if (whitelist.IsWhitelisted(user.Identities))
        {
            return AccountTier.Unlimited;
        }

        return AccountTiers.Resolve(false, await store.GetSubscriptions(user.Id));
    }
}
