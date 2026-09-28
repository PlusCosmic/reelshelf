using Microsoft.Extensions.Options;

namespace Reelshelf.ClipTranscription;

/// <summary>
/// Works through queued transcription runs, <see cref="ClipTranscriptionOptions.Concurrency"/> at a time, each
/// worker checking for new ones every few seconds once the queue is empty. Runs live in Postgres and are claimed
/// with SKIP LOCKED, so workers never share a run and nothing is lost when the API restarts mid-run.
/// </summary>
public class ClipTranscriptionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ClipTranscriptionOptions> options,
    ILogger<ClipTranscriptionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(10);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int workers = Math.Max(1, options.Value.Concurrency);
        logger.LogInformation("Processing clip transcription runs with {Workers} workers", workers);
        return Task.WhenAll(Enumerable.Range(0, workers).Select(_ => WorkAsync(stoppingToken)));
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        // Leave ExecuteAsync first, so the workers don't hold up the rest of startup.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed = false;
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ClipTranscriptionService service = scope.ServiceProvider.GetRequiredService<ClipTranscriptionService>();
                processed = await service.ProcessNextRunAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to process clip transcription runs");
            }

            if (!processed)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
