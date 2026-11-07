using System.Text.Json;
using Dapper;
using Npgsql;
using Reelshelf.Auth;
using Reelshelf.Billing;

namespace Reelshelf.FeatureFlags;

/// <summary>An account as the person sync sees it: what its flag properties are made of.</summary>
public sealed record PostHogPersonAccount(Guid Id, UserRole Role, AccountTier? GrantedTier);

public interface IPostHogPersonStore
{
    /// <summary>Every account that isn't being deleted.</summary>
    Task<List<PostHogPersonAccount>> GetAccounts();

    /// <summary>The properties last sent for each account, as the JSON sent.</summary>
    Task<Dictionary<Guid, string>> GetSynced();

    /// <summary>Records what was sent, for accounts that still exist.</summary>
    Task MarkSynced(IReadOnlyCollection<(Guid AccountId, string Properties)> synced);
}

public sealed class PostHogPersonStatements(NpgsqlConnection connection) : IPostHogPersonStore
{
    public async Task<List<PostHogPersonAccount>> GetAccounts()
    {
        const string sql = """
            SELECT id, role, granted_tier
            FROM app_user
            WHERE deletion_requested_at IS NULL
            """;
        IEnumerable<(Guid Id, string Role, string? GrantedTier)> rows =
            await connection.QueryAsync<(Guid, string, string?)>(sql);
        return rows
            .Select(row => new PostHogPersonAccount(row.Id, UserRoles.Parse(row.Role), GrantedTiers.Parse(row.GrantedTier)))
            .ToList();
    }

    public async Task<Dictionary<Guid, string>> GetSynced()
    {
        const string sql = "SELECT app_user_id, properties FROM posthog_person";
        return (await connection.QueryAsync<(Guid, string)>(sql)).ToDictionary(row => row.Item1, row => row.Item2);
    }

    public async Task MarkSynced(IReadOnlyCollection<(Guid AccountId, string Properties)> synced)
    {
        // An account deleted since it was read is skipped rather than failing the batch.
        const string sql = """
            INSERT INTO posthog_person (app_user_id, properties, synced_at)
            SELECT u.id, s.properties, now()
            FROM unnest(@ids, @properties) AS s(id, properties)
            JOIN app_user u ON u.id = s.id
            ON CONFLICT (app_user_id) DO UPDATE SET properties = excluded.properties, synced_at = excluded.synced_at
            """;
        await connection.ExecuteAsync(sql, new
        {
            ids = synced.Select(row => row.AccountId).ToArray(),
            properties = synced.Select(row => row.Properties).ToArray()
        });
    }
}

/// <summary>
/// Identifies to PostHog every account whose flag properties differ from what was last sent (all of them, the first
/// time), and records what was sent. Tiers are worked out for every account at once by the same rule as
/// <see cref="AccountTiers.Resolve"/>: the better of the granted tier and the best open subscription.
/// </summary>
public sealed class PostHogPersonSync(
    IPostHogPersonStore store,
    IBillingStore billing,
    IPostHogPeople people,
    ILogger<PostHogPersonSync> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        List<PostHogPersonAccount> accounts = await store.GetAccounts();
        HashSet<Guid> pro = (await billing.GetUserIdsWithTierAtLeast(AccountTier.Pro)).ToHashSet();
        HashSet<Guid> plus = (await billing.GetUserIdsWithTierAtLeast(AccountTier.Plus)).ToHashSet();
        Dictionary<Guid, string> synced = await store.GetSynced();

        List<(FeatureFlagSubject Subject, string Properties)> changed = [];
        foreach (PostHogPersonAccount account in accounts)
        {
            AccountTier subscribed = pro.Contains(account.Id) ? AccountTier.Pro
                : plus.Contains(account.Id) ? AccountTier.Plus
                : AccountTier.Free;
            AccountTier granted = account.GrantedTier ?? AccountTier.Free;
            FeatureFlagSubject subject = new(account.Id, account.Role,
                granted > subscribed ? granted : subscribed, account.GrantedTier);

            string properties = JsonSerializer.Serialize(subject.PersonProperties());
            if (!synced.TryGetValue(account.Id, out string? sent) || sent != properties)
            {
                changed.Add((subject, properties));
            }
        }

        foreach ((FeatureFlagSubject Subject, string Properties)[] batch in changed.Chunk(PostHogPeopleClient.BatchSize))
        {
            await people.IdentifyAsync(batch.Select(item => item.Subject).ToList(), cancellationToken);
            await store.MarkSynced(batch.Select(item => (item.Subject.AccountId, item.Properties)).ToList());
        }

        if (changed.Count > 0)
        {
            logger.LogInformation("Identified {Count} accounts to PostHog", changed.Count);
        }

        return changed.Count;
    }
}

/// <summary>Lets a change of access start the person sync straight away instead of waiting for its interval.</summary>
public sealed class PostHogPersonSyncSignal
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _wake.WaitAsync(timeout, cancellationToken);
    }
}

/// <summary>
/// Runs the person sync at startup, every <c>PostHog:PersonSyncInterval</c>, and when woken. Registered only where
/// PostHog is configured, never in <c>OpenApi</c>. A failed pass is logged and repeated on the next one; what it
/// already sent stays recorded.
/// </summary>
public sealed class PostHogPersonSyncService(
    IServiceScopeFactory scopes,
    PostHogPersonSyncSignal signal,
    PostHogSettings settings,
    ILogger<PostHogPersonSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PostHogPersonSync>().RunAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "The PostHog person sync failed; retrying on the next pass");
            }

            try
            {
                await signal.WaitAsync(settings.PersonSyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
