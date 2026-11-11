using Microsoft.Extensions.Options;

namespace Reelshelf.Video.Transcoding;

/// <summary>
/// Moves clips encoded with Opus audio onto AAC-LC, which Safari plays in MP4 (it plays no Opus there, so those clips
/// couldn't play on iPhones or Macs). Every <see cref="TranscodingOptions.AacBackfillInterval"/>, while
/// <see cref="TranscodingOptions.AacBackfill"/> is on, it tops the queue up to
/// <see cref="TranscodingOptions.AacBackfillBatchSize"/> <c>audio</c> jobs (<see cref="TranscodeRunTypes.Audio"/>),
/// at backfill priority so new uploads go first. Each job copies the video, re-encodes the audio and moves the clip to
/// the new version like a trim. Once no clip carries Opus, it queues nothing. Never in <c>OpenApi</c>.
/// </summary>
public sealed class AacBackfillService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TranscodingOptions> options,
    ILogger<AacBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A minute after startup, so a deploy's restart doesn't queue before the rest of the app is up.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            TranscodingOptions current = options.CurrentValue;
            if (current.AacBackfill)
            {
                try
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    int queued = await scope.ServiceProvider.GetRequiredService<TranscodeStatements>()
                        .EnqueueAacBackfillAsync(Math.Max(1, current.AacBackfillBatchSize),
                            TranscodeRunTypes.DefaultPriority(TranscodeRunTypes.Audio));
                    if (queued > 0)
                    {
                        logger.LogInformation("Queued {Count} audio jobs to move clips off Opus", queued);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "The AAC backfill failed");
                }
            }

            TimeSpan interval = current.AacBackfillInterval > TimeSpan.Zero
                ? current.AacBackfillInterval
                : TimeSpan.FromMinutes(1);
            await Task.Delay(interval, stoppingToken);
        }
    }
}
