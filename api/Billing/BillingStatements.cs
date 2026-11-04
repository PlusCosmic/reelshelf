using Dapper;
using Npgsql;

namespace Reelshelf.Billing;

/// <summary>
/// A Stripe subscription as billing last read it. <see cref="Tier"/> is null when its price carries a lookup key
/// that isn't one of <see cref="BillingPlans"/>, so it grants nothing. <see cref="ScheduledLookupKey"/> is the plan a
/// downgrade moves it to at <see cref="ScheduledAt"/>, the end of the paid period.
/// </summary>
public sealed record BillingSubscription(
    string Id,
    Guid UserId,
    string Status,
    string? LookupKey,
    AccountTier? Tier,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CancelAt,
    DateTimeOffset? EndedAt,
    string? ScheduledLookupKey = null,
    DateTimeOffset? ScheduledAt = null);

public interface IBillingStore
{
    Task<string?> GetCustomerId(Guid userId);

    Task<Guid?> GetUserIdByCustomer(string customerId);

    /// <summary>
    /// Links the Stripe customer to the account when the account has none and no other account has it. Never
    /// replaces a customer already linked.
    /// </summary>
    Task LinkCustomer(Guid userId, string customerId);

    Task<List<BillingSubscription>> GetSubscriptions(Guid userId);

    /// <summary>Accounts with a subscription that grants <paramref name="minimum"/> or a better tier now.</summary>
    Task<List<Guid>> GetUserIdsWithTierAtLeast(AccountTier minimum);

    /// <summary>
    /// Inserts or replaces the subscription, and answers the account the stored row belongs to: a row already
    /// recorded keeps its account, so that is another account when Stripe's customer no longer matches. Null when
    /// nothing was recorded because the account is gone or being deleted.
    /// </summary>
    Task<Guid?> UpsertSubscription(BillingSubscription subscription);

    /// <summary>
    /// Holds the subscription's sync lock until disposed, so two syncs of it can't commit in the opposite order of
    /// their reads from Stripe. Store calls made meanwhile run inside it and are committed when it is disposed; under
    /// <see cref="LockCheckouts"/> it joins that transaction and is held until the checkout lock is released.
    /// </summary>
    Task<IAsyncDisposable> LockSubscription(string subscriptionId);

    /// <summary>
    /// Holds the account's checkout lock until disposed, so two requests can't each start a Checkout. Store calls
    /// made meanwhile run inside it and are committed when it is disposed.
    /// </summary>
    Task<IAsyncDisposable> LockCheckouts(Guid userId);

    /// <summary>The account's latest Checkout Session, if it started one.</summary>
    Task<string?> GetCheckoutSessionId(Guid userId);

    Task SetCheckoutSessionId(Guid userId, string sessionId);
}

public sealed class BillingStatements(NpgsqlConnection connection) : IBillingStore
{
    public async Task<string?> GetCustomerId(Guid userId)
    {
        return await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT stripe_customer_id FROM app_user WHERE id = @userId", new { userId });
    }

    public async Task<Guid?> GetUserIdByCustomer(string customerId)
    {
        return await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM app_user WHERE stripe_customer_id = @customerId", new { customerId });
    }

    public async Task LinkCustomer(Guid userId, string customerId)
    {
        const string sql = """
            UPDATE app_user
            SET stripe_customer_id = @customerId
            WHERE id = @userId
              AND stripe_customer_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM app_user WHERE stripe_customer_id = @customerId)
            """;
        await connection.ExecuteAsync(sql, new { userId, customerId });
    }

    public async Task<List<BillingSubscription>> GetSubscriptions(Guid userId)
    {
        const string sql = """
            SELECT id, user_id, status, lookup_key, tier, current_period_end, cancel_at_period_end, cancel_at, ended_at,
                   scheduled_lookup_key, scheduled_at
            FROM billing_subscription
            WHERE user_id = @userId
            ORDER BY updated_at DESC
            """;
        return (await connection.QueryAsync<SubscriptionRow>(sql, new { userId }))
            .Select(row => row.ToSubscription())
            .ToList();
    }

    public async Task<List<Guid>> GetUserIdsWithTierAtLeast(AccountTier minimum)
    {
        string[] tiers = BillingPlans.All
            .Select(plan => plan.Tier)
            .Where(tier => tier >= minimum)
            .Distinct()
            .Select(TierName)
            .ToArray();
        const string sql = """
            SELECT DISTINCT user_id
            FROM billing_subscription
            WHERE tier = ANY(@tiers) AND status IN ('active', 'trialing', 'past_due')
            """;
        return (await connection.QueryAsync<Guid>(sql, new { tiers })).ToList();
    }

    public async Task<Guid?> UpsertSubscription(BillingSubscription subscription)
    {
        // The account may have been deleted, or marked for deletion, meanwhile: its deletion may already have
        // cancelled what it knew of, so the caller cancels this one instead of recording it. A recorded
        // subscription never moves to another account; the caller logs the disagreement instead.
        const string sql = """
            INSERT INTO billing_subscription (
                id, user_id, status, lookup_key, tier, current_period_end, cancel_at_period_end, cancel_at, ended_at,
                scheduled_lookup_key, scheduled_at)
            SELECT @Id, @UserId, @Status, @LookupKey, @Tier, @CurrentPeriodEnd, @CancelAtPeriodEnd, @CancelAt, @EndedAt,
                   @ScheduledLookupKey, @ScheduledAt
            WHERE EXISTS (SELECT 1 FROM app_user WHERE id = @UserId AND deletion_requested_at IS NULL)
            ON CONFLICT (id) DO UPDATE SET
                status = EXCLUDED.status,
                lookup_key = EXCLUDED.lookup_key,
                tier = EXCLUDED.tier,
                current_period_end = EXCLUDED.current_period_end,
                cancel_at_period_end = EXCLUDED.cancel_at_period_end,
                cancel_at = EXCLUDED.cancel_at,
                ended_at = EXCLUDED.ended_at,
                scheduled_lookup_key = EXCLUDED.scheduled_lookup_key,
                scheduled_at = EXCLUDED.scheduled_at,
                updated_at = now()
            RETURNING user_id
            """;
        return await connection.QuerySingleOrDefaultAsync<Guid?>(sql, new
        {
            subscription.Id,
            subscription.UserId,
            subscription.Status,
            subscription.LookupKey,
            Tier = subscription.Tier is { } tier ? TierName(tier) : null,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CancelAt,
            subscription.EndedAt,
            subscription.ScheduledLookupKey,
            subscription.ScheduledAt
        });
    }

    /// <summary>The transaction a lock taken through this store holds open; locks taken meanwhile join it.</summary>
    private NpgsqlTransaction? _transaction;

    public Task<IAsyncDisposable> LockCheckouts(Guid userId) => Lock($"billing-checkout:{userId}");

    public Task<IAsyncDisposable> LockSubscription(string subscriptionId) =>
        Lock($"billing-subscription:{subscriptionId}");

    private async Task<IAsyncDisposable> Lock(string key)
    {
        if (_transaction is { } open)
        {
            // Already inside a lock's transaction: this one is held with it, until it commits.
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@key))", new { key }, open);
            return NestedLock.Instance;
        }

        // A transaction-level lock on a connection kept open until disposed; the other statements share it.
        bool openedConnection = connection.State != System.Data.ConnectionState.Open;
        if (openedConnection)
        {
            await connection.OpenAsync();
        }

        NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        try
        {
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@key))", new { key }, transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            if (openedConnection)
            {
                await connection.CloseAsync();
            }

            throw;
        }

        _transaction = transaction;
        return new TransactionLock(connection, transaction, openedConnection, () => _transaction = null);
    }

    public async Task<string?> GetCheckoutSessionId(Guid userId)
    {
        return await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT stripe_checkout_session_id FROM app_user WHERE id = @userId", new { userId });
    }

    public async Task SetCheckoutSessionId(Guid userId, string sessionId)
    {
        await connection.ExecuteAsync(
            "UPDATE app_user SET stripe_checkout_session_id = @sessionId WHERE id = @userId", new { userId, sessionId });
    }

    private sealed class TransactionLock(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool openedConnection,
        Action released) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await transaction.CommitAsync();
            }
            finally
            {
                released();
                await transaction.DisposeAsync();
                if (openedConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    private sealed class NestedLock : IAsyncDisposable
    {
        public static readonly NestedLock Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal static string TierName(AccountTier tier)
    {
        return tier.ToString().ToLowerInvariant();
    }

    private sealed class SubscriptionRow
    {
        public string Id { get; set; } = "";
        public Guid UserId { get; set; }
        public string Status { get; set; } = "";
        public string? LookupKey { get; set; }
        public string? Tier { get; set; }
        public DateTimeOffset? CurrentPeriodEnd { get; set; }
        public bool CancelAtPeriodEnd { get; set; }
        public DateTimeOffset? CancelAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public string? ScheduledLookupKey { get; set; }
        public DateTimeOffset? ScheduledAt { get; set; }

        public BillingSubscription ToSubscription()
        {
            AccountTier? tier = Enum.TryParse(Tier, ignoreCase: true, out AccountTier parsed) ? parsed : null;
            return new BillingSubscription(Id, UserId, Status, LookupKey, tier, CurrentPeriodEnd, CancelAtPeriodEnd,
                CancelAt, EndedAt, ScheduledLookupKey, ScheduledAt);
        }
    }
}
