using System.Net;
using Stripe;
using Stripe.Checkout;

namespace Reelshelf.Billing;

/// <summary>
/// Stripe Billing through Managed Payments: Stripe (as Link) is the merchant of record, so every subscription is
/// created by a Checkout Session with <c>managed_payments.enabled</c>, and changed through the Customer Portal.
/// </summary>
public sealed class StripeBilling(StripeClient client) : IStripeBilling
{
    /// <summary>Tags our sessions in the Dashboard's Checkout analytics.</summary>
    internal const string IntegrationIdentifier = "reelshelf_plans_qmtvzkre";

    internal const string UserIdMetadataKey = "reelshelf_user_id";

    public async Task<StripeCheckoutSession?> CreateCheckoutSessionAsync(CheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        StripeList<Price> prices = await client.V1.Prices.ListAsync(new PriceListOptions
        {
            LookupKeys = [request.Plan.LookupKey],
            Active = true,
            Limit = 1
        }, cancellationToken: cancellationToken);

        Price? price = prices.Data.FirstOrDefault();
        if (price is null)
        {
            return null;
        }

        SessionCreateOptions options = new()
        {
            Mode = "subscription",
            // No payment_method_types: Managed Payments and the Dashboard decide which methods to offer.
            LineItems = [new SessionLineItemOptions { Price = price.Id, Quantity = 1 }],
            ManagedPayments = new SessionManagedPaymentsOptions { Enabled = true },
            ClientReferenceId = request.UserId.ToString(),
            Customer = request.CustomerId,
            CustomerEmail = request.CustomerId is null ? request.CustomerEmail : null,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = request.UserId.ToString() }
            },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            IntegrationIdentifier = IntegrationIdentifier
        };

        Session session = await client.V1.Checkout.Sessions.CreateAsync(options, cancellationToken: cancellationToken);
        return ToCheckoutSession(session);
    }

    public async Task<StripeCheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return ToCheckoutSession(
                await client.V1.Checkout.Sessions.GetAsync(sessionId, cancellationToken: cancellationToken));
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await client.V1.Checkout.Sessions.ExpireAsync(sessionId, cancellationToken: cancellationToken);
            return true;
        }
        catch (StripeException ex) when (ex.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            // Completed or expired meanwhile, or gone: it can't be paid through this session either way.
            return false;
        }
    }

    private static StripeCheckoutSession ToCheckoutSession(Session session) =>
        new(session.Id, session.Status, session.Url, session.SubscriptionId);

    public async Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken)
    {
        Stripe.BillingPortal.Session session = await client.V1.BillingPortal.Sessions.CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl },
            cancellationToken: cancellationToken);
        return session.Url;
    }

    public async Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        Subscription subscription;
        try
        {
            subscription = await client.V1.Subscriptions.GetAsync(subscriptionId, cancellationToken: cancellationToken);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // A plan is one item; the period moved from the subscription to its items in API version 2025-03-31.
        SubscriptionItem? item = subscription.Items?.Data.FirstOrDefault();
        Guid? userId = subscription.Metadata is not null &&
                       subscription.Metadata.TryGetValue(UserIdMetadataKey, out string? value) &&
                       Guid.TryParse(value, out Guid parsed)
            ? parsed
            : null;

        return new StripeSubscriptionState(
            subscription.Id,
            subscription.CustomerId,
            subscription.Status,
            item?.Price?.LookupKey,
            ToOffset(item?.CurrentPeriodEnd),
            subscription.CancelAtPeriodEnd,
            ToOffset(subscription.CancelAt),
            ToOffset(subscription.EndedAt),
            userId);
    }

    public async Task<List<StripePlanPrice>> ListPricesAsync(IReadOnlyList<string> lookupKeys,
        CancellationToken cancellationToken)
    {
        StripeList<Price> prices = await client.V1.Prices.ListAsync(new PriceListOptions
        {
            LookupKeys = lookupKeys.ToList(),
            Active = true,
            Limit = 100
        }, cancellationToken: cancellationToken);

        return prices.Data
            .Where(price => price.LookupKey is not null && price.UnitAmount is not null)
            .Select(price => new StripePlanPrice(price.LookupKey, price.UnitAmount!.Value, price.Currency))
            .ToList();
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        StripeSubscriptionState? state = await GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (state is null || state.Status is "canceled" or "incomplete_expired")
        {
            return;
        }

        try
        {
            await client.V1.Subscriptions.CancelAsync(subscriptionId, cancellationToken: cancellationToken);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Gone between the read and the cancel.
        }
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        return value is { } dateTime ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)) : null;
    }
}
