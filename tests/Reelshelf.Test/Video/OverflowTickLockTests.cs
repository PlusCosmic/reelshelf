using Npgsql;
using Reelshelf.Video.Transcoding.Overflow;
using Xunit;

namespace Reelshelf.Test.Video;

/// <summary>Two API instances side by side during a deploy: only one runs the overflow tick at a time.</summary>
public class OverflowTickLockTests
{
    [PostgresFact]
    public async Task SecondInstance_SkipsTheTick_WhileTheFirstHoldsIt()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PostgresFactAttribute.ConnectionString!);
        PostgresOverflowTickLock first = new(dataSource);
        PostgresOverflowTickLock second = new(dataSource);

        TaskCompletionSource holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> firstRun = first.TryRunAsync(async _ =>
        {
            holding.SetResult();
            await release.Task;
        }, CancellationToken.None);
        await holding.Task;

        bool secondTicked = false;
        Assert.False(await second.TryRunAsync(_ =>
        {
            secondTicked = true;
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.False(secondTicked);

        release.SetResult();
        Assert.True(await firstRun);

        // Released once the first tick is done, so the next tick runs wherever it lands.
        Assert.True(await second.TryRunAsync(_ => Task.CompletedTask, CancellationToken.None));
    }

    [PostgresFact]
    public async Task FailedTick_ReleasesTheLock()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(PostgresFactAttribute.ConnectionString!);
        PostgresOverflowTickLock tickLock = new(dataSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tickLock.TryRunAsync(_ => throw new InvalidOperationException("tick failed"), CancellationToken.None));

        Assert.True(await new PostgresOverflowTickLock(dataSource)
            .TryRunAsync(_ => Task.CompletedTask, CancellationToken.None));
    }
}
