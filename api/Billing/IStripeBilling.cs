namespace Reelshelf.Billing;

/// <summary>The Stripe calls billing makes; <see cref="StripeBilling"/> is the only code that talks to Stripe.</summary>
public interface IStripeBilling
{
    /// <summary>The Checkout page's URL, or null when no active price carries the plan's lookup key.</summary>
    Task<string?> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken);

    /// <summary>A Customer Portal session's URL for the customer.</summary>
    Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken);

    /// <summary>The subscription as Stripe has it now, or null when Stripe has no such subscription.</summary>
    Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);

    /// <summary>Cancels the subscription at once. Does nothing when it is already over or Stripe has no such subscription.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);
}

public sealed record CheckoutSessionRequest(
    BillingPlan Plan,
    Guid UserId,
    string? CustomerId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl);

/// <summary>
/// The parts of a Stripe subscription billing keeps. <see cref="UserId"/> is the account id Checkout wrote to the
/// subscription's metadata, used only when the customer isn't linked to an account yet.
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
    Guid? UserId);
