namespace Reelshelf.Billing;

/// <summary>The Stripe calls billing makes; <see cref="StripeBilling"/> is the only code that talks to Stripe.</summary>
public interface IStripeBilling
{
    /// <summary>The Checkout Session, or null when no active price carries the plan's lookup key.</summary>
    Task<StripeCheckoutSession?> CreateCheckoutSessionAsync(CheckoutSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>The Checkout Session as Stripe has it now, or null when Stripe has no such session.</summary>
    Task<StripeCheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Expires an open Checkout Session so it can't be paid. False when it is no longer open.</summary>
    Task<bool> ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>A Customer Portal session's URL for the customer.</summary>
    Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken);

    /// <summary>
    /// A Customer Portal session that opens straight on confirming one change of plan (an upgrade: Stripe shows the
    /// prorated charge and takes the payment), under the portal configuration that allows changing plans.
    /// </summary>
    Task<string> CreatePlanChangeSessionAsync(PlanChangeSessionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Moves the subscription to the price at the end of its current period, through a subscription schedule
    /// (creating one, or rewriting the one it has), released once the change is made.
    /// </summary>
    Task ScheduleDowngradeAsync(StripeSubscriptionState subscription, string priceId, string interval,
        CancellationToken cancellationToken);

    /// <summary>Releases the subscription's schedule, so it renews on its current plan. Nothing when it's already gone.</summary>
    Task ReleaseScheduleAsync(string scheduleId, CancellationToken cancellationToken);

    /// <summary>The subscription as Stripe has it now, or null when Stripe has no such subscription.</summary>
    Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);

    /// <summary>The active prices carrying these lookup keys.</summary>
    Task<List<StripePlanPrice>> ListPricesAsync(IReadOnlyList<string> lookupKeys, CancellationToken cancellationToken);

    /// <summary>Cancels the subscription at once. Does nothing when it is already over or Stripe has no such subscription.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);
}

/// <summary>
/// A Checkout Session: <see cref="Status"/> is <c>open</c>, <c>complete</c> or <c>expired</c>, and
/// <see cref="SubscriptionId"/> is set once a subscription checkout completes.
/// </summary>
public sealed record StripeCheckoutSession(string Id, string Status, string? Url, string? SubscriptionId);

/// <summary>A price in its default currency, in the currency's minor unit (pence, cents).</summary>
public sealed record StripePlanPrice(string LookupKey, long UnitAmount, string Currency, string? Id = null);

public sealed record PlanChangeSessionRequest(
    string CustomerId,
    string SubscriptionId,
    string SubscriptionItemId,
    string PriceId,
    string ConfigurationId,
    string ReturnUrl);

public sealed record CheckoutSessionRequest(
    BillingPlan Plan,
    Guid UserId,
    string? CustomerId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl);

/// <summary>
/// The parts of a Stripe subscription billing keeps. <see cref="UserId"/> is the account id Checkout wrote to the
/// subscription's metadata, used only when the customer isn't linked to an account yet. <see cref="ItemId"/> is its
/// one item (a plan is one item), and <see cref="ScheduleId"/> its schedule, whose next phase, when it has one,
/// moves it to <see cref="ScheduledLookupKey"/> at <see cref="ScheduledAt"/>.
/// </summary>
public sealed record StripeSubscriptionState(
    string Id,
    string CustomerId,
    string Status,
    string? LookupKey,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelAt,
    DateTimeOffset? EndedAt,
    Guid? UserId,
    string? ItemId = null,
    string? ScheduleId = null,
    string? ScheduledLookupKey = null,
    DateTimeOffset? ScheduledAt = null);
