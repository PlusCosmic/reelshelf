using Dapper;
using Npgsql;
using Reelshelf.Billing;
using Reelshelf.Core;
using Xunit;

namespace Reelshelf.Test.Billing;

/// <summary>Runs the billing queries against a real Postgres, on the V45 migration itself.</summary>
public class BillingStatementsTests
{
    [PostgresFact]
    public async Task CustomersAndSubscriptions_RoundTrip()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        string schema = $"billing_test_{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(PostgresFactAttribute.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        }

        await using NpgsqlDataSource dataSource = ReelshelfDataSource.Create(
            $"{PostgresFactAttribute.ConnectionString};Search Path={schema}");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
        try
        {
            await connection.ExecuteAsync("CREATE TABLE app_user (id uuid PRIMARY KEY, deletion_requested_at timestamptz)");
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V45__billing.sql")));
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V48__checkout_session.sql")));
            await connection.ExecuteAsync(await File.ReadAllTextAsync(MigrationPath("V49__scheduled_plan_change.sql")));
            await RunAsync(connection, new BillingStatements(dataSource.CreateConnection()));
            await CheckoutLockAsync(connection, dataSource);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP SCHEMA {schema} CASCADE");
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, BillingStatements statements)
    {
        Guid harry = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO app_user (id) VALUES (@harry), (@other)", new { harry, other });

        // A customer is linked once, and never to two accounts.
        await statements.LinkCustomer(harry, "cus_1");
        await statements.LinkCustomer(harry, "cus_2");
        await statements.LinkCustomer(other, "cus_1");
        Assert.Equal("cus_1", await statements.GetCustomerId(harry));
        Assert.Null(await statements.GetCustomerId(other));
        Assert.Equal(harry, await statements.GetUserIdByCustomer("cus_1"));
        Assert.Null(await statements.GetUserIdByCustomer("cus_2"));

        DateTimeOffset periodEnd = DateTimeOffset.Parse("2026-11-23T10:00:00Z");
        BillingSubscription subscription = new("sub_1", harry, "active", "pro_yearly", AccountTier.Pro, periodEnd,
            false, null, null);
        Assert.True(await statements.UpsertSubscription(subscription));
        Assert.Equal(subscription, Assert.Single(await statements.GetSubscriptions(harry)));

        // A downgrade waiting for the end of the period is kept with it.
        BillingSubscription downgrading = subscription with
        {
            ScheduledLookupKey = "plus_monthly", ScheduledAt = periodEnd
        };
        Assert.True(await statements.UpsertSubscription(downgrading));
        Assert.Equal(downgrading, Assert.Single(await statements.GetSubscriptions(harry)));

        // An upsert replaces the row.
        BillingSubscription cancelled = subscription with
        {
            Status = "canceled", CancelAtPeriodEnd = true, EndedAt = periodEnd, CancelAt = periodEnd
        };
        Assert.True(await statements.UpsertSubscription(cancelled));
        Assert.Equal(cancelled, Assert.Single(await statements.GetSubscriptions(harry)));

        Assert.Empty(await statements.GetUserIdsWithTierAtLeast(AccountTier.Plus));
        Assert.True(await statements.UpsertSubscription(subscription with { Id = "sub_live", Status = "past_due" }));
        Assert.Equal([harry], await statements.GetUserIdsWithTierAtLeast(AccountTier.Pro));
        Assert.Equal([harry], await statements.GetUserIdsWithTierAtLeast(AccountTier.Plus));

                // An unknown plan is recorded without a tier.
        BillingSubscription legacy = new("sub_2", harry, "active", "legacy", null, null, false, null, null);
        Assert.True(await statements.UpsertSubscription(legacy));
        Assert.Contains(legacy, await statements.GetSubscriptions(harry));

        // Nor is one of an account being deleted, or of one already gone; the account's rows go with it.
        await connection.ExecuteAsync("UPDATE app_user SET deletion_requested_at = now() WHERE id = @other", new { other });
        Assert.False(await statements.UpsertSubscription(subscription with { Id = "sub_4", UserId = other }));
        Assert.False(await statements.UpsertSubscription(subscription with { Id = "sub_3", UserId = Guid.NewGuid() }));
        await connection.ExecuteAsync("DELETE FROM app_user WHERE id = @harry", new { harry });
        Assert.Empty(await statements.GetSubscriptions(harry));
    }

    private static async Task CheckoutLockAsync(NpgsqlConnection connection, NpgsqlDataSource dataSource)
    {
        Guid harry = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO app_user (id) VALUES (@harry)", new { harry });
        BillingStatements first = new(dataSource.CreateConnection());
        BillingStatements second = new(dataSource.CreateConnection());

        // What is written under the lock is committed when it is released, and a second request for the same
        // account waits for the first to finish.
        Task<IAsyncDisposable> waiting;
        await using (await first.LockCheckouts(harry))
        {
            Assert.Null(await first.GetCheckoutSessionId(harry));
            await first.SetCheckoutSessionId(harry, "cs_1");

            waiting = second.LockCheckouts(harry);
            await Task.Delay(200);
            Assert.False(waiting.IsCompleted);
        }

        await using (await waiting)
        {
            Assert.Equal("cs_1", await second.GetCheckoutSessionId(harry));
        }

        Assert.Equal("cs_1", await first.GetCheckoutSessionId(harry));
    }

    private static string MigrationPath(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "migrations", "db", "migrations", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Couldn't find the migration {name} above {AppContext.BaseDirectory}");
    }
}
