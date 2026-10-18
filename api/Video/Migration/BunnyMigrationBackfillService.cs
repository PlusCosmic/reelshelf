using Microsoft.Extensions.Options;
using Reelshelf.Video.Transcoding;

namespace Reelshelf.Video.Migration;

/// <summary>
/// Keeps up to <see cref="MigrationOptions.BatchSize"/> <c>migrate</c> jobs queued while
/// <see cref="MigrationOptions.Enabled"/> is on, so the transcode workers move the ready Bunny clips to R2 a few at
/// a time, behind every other job (docs/video-pipeline/bunny-migration.md). Queues nothing without R2, since the
/// claim wouldn't hand the jobs out. Never registered in the <c>OpenApi</c> environment.
/// </summary>
public class BunnyMigrationBackfillService(
    IServiceScopeFactory scopeFactory,
    ITranscodeStorage storage,
    IOptionsMonitor<MigrationOptions> options,
    ILogger<BunnyMigrationBackfillService> logger)
    : BackgroundService
{
    /// <summary>
    /// How often the batch is topped up. A migrate job takes about as long as its clip, so a minute keeps the
    /// workers fed.
    /// </summary>
    private static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(1);

    private bool _warnedNoStorage;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PassInterval);
        do
        {
            await RunPassAsync();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: queues the next clips when the migration is on. Returns the clips queued.</summary>
    internal async Task<int> RunPassAsync()
    {
        MigrationOptions current = options.CurrentValue;
        if (!current.Enabled)
        {
            return 0;
        }

        if (!storage.IsConfigured)
        {
            if (!_warnedNoStorage)
            {
                logger.LogWarning("Migration:Enabled is on, but R2 is not configured; no clips are queued to move");
                _warnedNoStorage = true;
            }

            return 0;
        }

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BunnyMigrationStatements statements = scope.ServiceProvider.GetRequiredService<BunnyMigrationStatements>();
            List<Guid> queued = await statements.QueueBatchAsync(current.EffectiveBatchSize);
            if (queued.Count > 0)
            {
                logger.LogInformation("Queued {Count} Bunny clips to move to R2", queued.Count);
            }

            return queued.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue Bunny clips to move to R2");
            return 0;
        }
    }
}
