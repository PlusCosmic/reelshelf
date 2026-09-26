using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reelshelf.Core;
using Reelshelf.Exceptions;
using Reelshelf.Games;

namespace Reelshelf.ApexLegends.LegendDetection;

public class LegendDetectionService(
    LegendDetectionStatements statements,
    ClipsStatements clipsStatements,
    GameCategoryStatements gameCategoryStatements,
    ILegendRecognizerFactory recognizerFactory,
    LegendDetectionResources resources,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<LegendDetectionOptions> options,
    IConfiguration configuration,
    ILogger<LegendDetectionService> logger)
{
    public const string FramesHttpClientName = "legend-detection-frames";

    private const string ApexLegendsSlug = "apex-legends";

    /// <summary>Retry a failed attempt after this long, so a brief provider or CDN outage is not retried at once.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>A run still marked running after this long belonged to a worker that stopped; claim it again.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Queues the automatic run for a clip whose video has finished encoding, when automatic detection is on and
    /// the clip is Apex Legends. Safe to call on every webhook: a clip only ever gets one automatic run.
    /// </summary>
    public async Task QueueForEncodedClipAsync(ClipsStatements.ClipRow clip)
    {
        LegendDetectionOptions current = options.CurrentValue;
        if (!current.AutoDetect || !recognizerFactory.IsConfigured(current.Provider)
                                || string.IsNullOrWhiteSpace(current.Model))
        {
            return;
        }

        GameCategory? apex = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apex is null || apex.Id != clip.GameCategoryId)
        {
            return;
        }

        if (await statements.QueueAutoRunAsync(clip.Id, current.Provider, current.Model))
        {
            logger.LogInformation("Queued legend detection for clip {ClipId}", clip.Id);
        }
    }

    /// <summary>
    /// Archives every run so detection can start again from scratch; usage totals keep the archived runs.
    /// </summary>
    public async Task<int> ArchiveAllRunsAsync()
    {
        int archived = await statements.ArchiveAllRunsAsync();
        logger.LogInformation("Archived {Count} legend detection runs", archived);
        return archived;
    }

    /// <summary>Queues the automatic run for every Apex Legends clip without an unarchived one.</summary>
    public async Task<int> BackfillAsync()
    {
        (string provider, string model) = Resolve(null, null);
        GameCategory apex = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug)
                            ?? throw new BadRequestException("Apex Legends category not found");
        return await statements.QueueAutoRunsForCategoryAsync(apex.Id, provider, model);
    }

    /// <summary>Queues an extra run for one clip, optionally with a different provider or model to compare against.</summary>
    public async Task<LegendDetectionStatements.LegendDetectionRunRow> QueueManualRunAsync(
        Guid clipId,
        string? provider,
        string? model)
    {
        (string resolvedProvider, string resolvedModel) = Resolve(provider, model);
        _ = await clipsStatements.GetClipById(clipId) ?? throw new NotFoundException("Clip", clipId);
        return await statements.QueueManualRunAsync(clipId, resolvedProvider, resolvedModel);
    }

    public async Task<List<LegendDetectionStatements.LegendDetectionRunRow>> GetRunsAsync(Guid clipId)
    {
        return await statements.GetRunsForClipAsync(clipId);
    }

    /// <summary>
    /// The owner's Apex Legends clips with their latest run, the thumbnails the model is sent and a player to
    /// check the result against.
    /// </summary>
    public async Task<List<LegendDetectionReviewClip>> GetClipsForReviewAsync(Guid ownerId)
    {
        GameCategory? apex = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apex is null)
        {
            return [];
        }

        List<LegendDetectionStatements.ReviewClipRow> clips = await statements.GetClipsForReviewAsync(apex.Id, ownerId);
        Dictionary<Guid, LegendDetectionStatements.LatestRunRow> latestRuns =
            (await statements.GetLatestRunsAsync(clips.Select(clip => clip.Id).ToList()))
            .ToDictionary(run => run.ClipId);

        string cdnBaseUrl = CdnBaseUrl();
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");

        return clips.Select(clip =>
        {
            latestRuns.TryGetValue(clip.Id, out LegendDetectionStatements.LatestRunRow? latest);
            return new LegendDetectionReviewClip(
                clip.Id,
                clip.Title ?? "Untitled",
                clip.CreatedAt,
                clip.Length,
                $"https://player.mediadelivery.net/embed/{libraryId}/{clip.VideoId}?autoplay=false",
                ThumbnailUrls(cdnBaseUrl, clip.VideoId),
                latest is null ? null : LegendDetectionRun.From(latest),
                (int)(latest?.RunCount ?? 0));
        }).ToList();
    }

    public async Task<List<LegendDetectionUsage>> GetUsageAsync()
    {
        return (await statements.GetUsageAsync())
            .Select(row => new LegendDetectionUsage(
                row.Provider,
                row.Model,
                row.PromptVersion,
                (int)row.Succeeded,
                (int)row.Failed,
                (int)row.Queued,
                row.InputTokens,
                row.CachedInputTokens,
                row.OutputTokens,
                row.AverageDurationMs,
                row.ArchivedAt))
            .ToList();
    }

    /// <summary>Processes the next queued run. Returns false when there was nothing to do.</summary>
    public async Task<bool> ProcessNextRunAsync(CancellationToken cancellationToken)
    {
        LegendDetectionStatements.ClaimedRunRow? run = await statements.ClaimNextRunAsync(RetryAfter, StaleAfter);
        if (run is null)
        {
            return false;
        }

        int maxAttempts = options.CurrentValue.MaxAttempts;
        if (run.Attempts > maxAttempts)
        {
            await statements.FailRunAsync(run.Id, $"Gave up after {maxAttempts} attempts", null, null, retry: false);
            return true;
        }

        bool canRetry = run.Attempts < maxAttempts;
        if (!recognizerFactory.IsConfigured(run.Provider))
        {
            await statements.FailRunAsync(run.Id, $"Provider '{run.Provider}' is not configured", null, null, retry: false);
            return true;
        }

        List<LegendFrame> frames = await DownloadFramesAsync(run.VideoId, cancellationToken);
        if (frames.Count == 0)
        {
            await statements.FailRunAsync(run.Id, "No thumbnails could be downloaded", null, null, canRetry);
            return true;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ILegendRecognizer recognizer = recognizerFactory.Create(run.Provider, run.Model);
            LegendRecognition recognition = await recognizer.RecognizeAsync(frames, cancellationToken);
            await statements.CompleteRunAsync(run.Id, resources.PromptVersion, frames.Count, recognition,
                (int)stopwatch.ElapsedMilliseconds);

            logger.LogInformation(
                "Legend detection for clip {ClipId} with {Provider}/{Model}: {Legend} ({Confidence:0.00})",
                run.ClipId, run.Provider, run.Model, recognition.Result.Player.Legend ?? "none",
                recognition.Result.Player.LegendConfidence);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Legend detection attempt {Attempt} failed for clip {ClipId} with {Provider}/{Model}",
                run.Attempts, run.ClipId, run.Provider, run.Model);
            string? rawResponse = ex is JsonException ? ex.Data["RawResponse"] as string : null;
            await statements.FailRunAsync(run.Id, ex.Message, resources.PromptVersion, rawResponse, canRetry);
        }

        return true;
    }

    /// <summary>
    /// The thumbnails Bunny generates while encoding: the main thumbnail plus five more taken through the video.
    /// </summary>
    public static List<string> ThumbnailUrls(string cdnBaseUrl, Guid videoId)
    {
        string baseUrl = $"{cdnBaseUrl.TrimEnd('/')}/{videoId}";
        return
        [
            $"{baseUrl}/thumbnail.jpg",
            .. Enumerable.Range(1, 5).Select(index => $"{baseUrl}/thumbnail_{index}.jpg")
        ];
    }

    private async Task<List<LegendFrame>> DownloadFramesAsync(Guid videoId, CancellationToken cancellationToken)
    {
        string cdnBaseUrl = CdnBaseUrl();
        HttpClient client = httpClientFactory.CreateClient(FramesHttpClientName);

        LegendFrame?[] frames = await Task.WhenAll(ThumbnailUrls(cdnBaseUrl, videoId).Select(async url =>
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Thumbnail {Url} returned {Status}; skipping it", url, (int)response.StatusCode);
                    return null;
                }

                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return new LegendFrame(data, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to download thumbnail {Url}", url);
                return null;
            }
        }));

        return frames.OfType<LegendFrame>().ToList();
    }

    private string CdnBaseUrl()
    {
        return configuration["BunnyCdnBaseUrl"]
               ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
    }

    private (string Provider, string Model) Resolve(string? provider, string? model)
    {
        LegendDetectionOptions current = options.CurrentValue;
        string resolvedProvider = string.IsNullOrWhiteSpace(provider) ? current.Provider : provider.Trim();
        string resolvedModel = string.IsNullOrWhiteSpace(model) ? current.Model : model.Trim();

        if (!recognizerFactory.IsConfigured(resolvedProvider))
        {
            throw new BadRequestException($"Legend detection provider '{resolvedProvider}' is not configured");
        }

        if (string.IsNullOrWhiteSpace(resolvedModel))
        {
            throw new BadRequestException("No legend detection model given and LegendDetection:Model is not set");
        }

        return (resolvedProvider.ToLowerInvariant(), resolvedModel);
    }
}
