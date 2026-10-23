using Reelshelf.Billing;

namespace Reelshelf.Test.Billing;

internal sealed class FakeStripe : IStripeBilling
{
    public List<CheckoutSessionRequest> Checkouts { get; } = [];
    public Dictionary<string, StripeSubscriptionState> Subscriptions { get; } = [];
    public List<string> Cancelled { get; } = [];
    public bool MissingPrices { get; set; }
    public string? PortalReturnUrl { get; private set; }
    public int SubscriptionReads { get; private set; }

    public Task<string?> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        if (MissingPrices)
        {
            return Task.FromResult<string?>(null);
        }

        Checkouts.Add(request);
        return Task.FromResult<string?>($"https://checkout.stripe.test/{Checkouts.Count}");
    }

    public Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken)
    {
        PortalReturnUrl = returnUrl;
        return Task.FromResult($"https://portal.stripe.test/{customerId}");
    }

    public Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        SubscriptionReads++;
        return Task.FromResult(Subscriptions.GetValueOrDefault(subscriptionId));
    }

    public List<StripePlanPrice> Prices { get; } = [];
    public int PriceReads { get; private set; }

    public Task<List<StripePlanPrice>> ListPricesAsync(IReadOnlyList<string> lookupKeys, CancellationToken cancellationToken)
    {
        PriceReads++;
        return Task.FromResult(Prices.Where(price => lookupKeys.Contains(price.LookupKey)).ToList());
    }

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        Cancelled.Add(subscriptionId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeStore : IBillingStore
{
    public HashSet<Guid> Users { get; } = [];
    public Dictionary<Guid, string> Customers { get; } = [];
    public List<BillingSubscription> Subscriptions { get; } = [];

    public Task<string?> GetCustomerId(Guid userId) => Task.FromResult(Customers.GetValueOrDefault(userId));

    public Task<Guid?> GetUserIdByCustomer(string customerId) =>
        Task.FromResult<Guid?>(Customers.Where(pair => pair.Value == customerId).Select(pair => pair.Key).FirstOrDefault() is { } id &&
                               id != Guid.Empty
            ? id
            : null);

    public Task LinkCustomer(Guid userId, string customerId)
    {
        if (Users.Contains(userId) && !Customers.ContainsKey(userId) && !Customers.ContainsValue(customerId))
        {
            Customers[userId] = customerId;
        }

        return Task.CompletedTask;
    }

    public Task<List<BillingSubscription>> GetSubscriptions(Guid userId) =>
        Task.FromResult(Subscriptions.Where(subscription => subscription.UserId == userId).ToList());

    public Task<List<Guid>> GetUserIdsWithTierAtLeast(AccountTier minimum) =>
        Task.FromResult(Subscriptions
            .Where(subscription => subscription.Tier >= minimum && SubscriptionStatuses.GrantsTier(subscription.Status))
            .Select(subscription => subscription.UserId)
            .Distinct()
            .ToList());

    public Task<bool> UpsertSubscription(BillingSubscription subscription)
    {
        if (!Users.Contains(subscription.UserId))
        {
            return Task.FromResult(false);
        }

        Subscriptions.RemoveAll(existing => existing.Id == subscription.Id);
        Subscriptions.Add(subscription);
        return Task.FromResult(true);
    }
}
