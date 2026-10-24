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

    /// <summary>Every session created, by id, as Stripe would answer for it now; tests complete or expire them.</summary>
    public Dictionary<string, StripeCheckoutSession> Sessions { get; } = [];
    public List<string> Expired { get; } = [];

    /// <summary>Completes the session before an expiry lands, as a payment finishing at that moment would.</summary>
    public Func<string, StripeCheckoutSession>? CompleteOnExpire { get; set; }

    public Task<StripeCheckoutSession?> CreateCheckoutSessionAsync(CheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (MissingPrices)
        {
            return Task.FromResult<StripeCheckoutSession?>(null);
        }

        Checkouts.Add(request);
        StripeCheckoutSession session = new($"cs_{Checkouts.Count}", "open",
            $"https://checkout.stripe.test/{Checkouts.Count}", null);
        Sessions[session.Id] = session;
        return Task.FromResult<StripeCheckoutSession?>(session);
    }

    public Task<StripeCheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.GetValueOrDefault(sessionId));

    public Task<bool> ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (CompleteOnExpire is not null)
        {
            Sessions[sessionId] = CompleteOnExpire(sessionId);
        }

        if (Sessions.GetValueOrDefault(sessionId) is not { Status: "open" } session)
        {
            return Task.FromResult(false);
        }

        Expired.Add(sessionId);
        Sessions[sessionId] = session with { Status = "expired", Url = null };
        return Task.FromResult(true);
    }

    public Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken cancellationToken)
    {
        PortalReturnUrl = returnUrl;
        return Task.FromResult($"https://portal.stripe.test/{customerId}");
    }

    public List<PlanChangeSessionRequest> PlanChanges { get; } = [];
    public List<(string SubscriptionId, string PriceId, string Interval)> Downgrades { get; } = [];
    public List<string> Released { get; } = [];

    public Task<string> CreatePlanChangeSessionAsync(PlanChangeSessionRequest request, CancellationToken cancellationToken)
    {
        PlanChanges.Add(request);
        return Task.FromResult($"https://portal.stripe.test/confirm/{PlanChanges.Count}");
    }

    /// <summary>Attaches a schedule moving the subscription to the price's plan when its period ends.</summary>
    public Task ScheduleDowngradeAsync(StripeSubscriptionState subscription, string priceId, string interval,
        CancellationToken cancellationToken)
    {
        Downgrades.Add((subscription.Id, priceId, interval));
        string lookupKey = Prices.Single(price => price.Id == priceId).LookupKey;
        Subscriptions[subscription.Id] = Subscriptions[subscription.Id] with
        {
            ScheduleId = subscription.ScheduleId ?? $"sub_sched_{Downgrades.Count}",
            ScheduledLookupKey = lookupKey,
            ScheduledAt = subscription.CurrentPeriodEnd
        };
        return Task.CompletedTask;
    }

    public Task ReleaseScheduleAsync(string scheduleId, CancellationToken cancellationToken)
    {
        Released.Add(scheduleId);
        foreach ((string id, StripeSubscriptionState state) in Subscriptions.ToList())
        {
            if (state.ScheduleId == scheduleId)
            {
                Subscriptions[id] = state with { ScheduleId = null, ScheduledLookupKey = null, ScheduledAt = null };
            }
        }

        return Task.CompletedTask;
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

    public Dictionary<Guid, string> CheckoutSessions { get; } = [];
    public int CheckoutLocks { get; private set; }

    public Task<IAsyncDisposable> LockCheckouts(Guid userId)
    {
        CheckoutLocks++;
        return Task.FromResult<IAsyncDisposable>(new NoLock());
    }

    public Task<string?> GetCheckoutSessionId(Guid userId) => Task.FromResult(CheckoutSessions.GetValueOrDefault(userId));

    public Task SetCheckoutSessionId(Guid userId, string sessionId)
    {
        CheckoutSessions[userId] = sessionId;
        return Task.CompletedTask;
    }

    private sealed class NoLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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
