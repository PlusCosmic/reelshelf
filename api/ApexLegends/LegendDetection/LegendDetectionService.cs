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

    /// <summary>A wrong legend reported at or above this confidence counts as a confident mistake.</summary>
    private const double ConfidentAt = 0.8;

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

        string? reasoningEffort;
        try
        {
            reasoningEffort = LegendReasoningEffort.Normalize(current.ReasoningEffort);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning("Not queueing legend detection: LegendDetection:ReasoningEffort is invalid. {Error}", ex.Message);
            return;
        }

        if (await statements.QueueAutoRunAsync(clip.Id, current.Provider, current.Model, reasoningEffort))
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

    /// <summary>
    /// Queues the automatic run for every Apex Legends clip without an unarchived one, at
    /// <paramref name="reasoningEffort"/> or the configured default when it is empty.
    /// </summary>
    public async Task<int> BackfillAsync(string? reasoningEffort)
    {
        (string provider, string model, string? effort) = Resolve(null, null, reasoningEffort);
        GameCategory apex = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug)
                            ?? throw new BadRequestException("Apex Legends category not found");
        return await statements.QueueAutoRunsForCategoryAsync(apex.Id, provider, model, effort);
    }

    /// <summary>
    /// Queues an extra run for one clip, optionally with a different provider, model or reasoning effort to compare
    /// against.
    /// </summary>
    public async Task<LegendDetectionStatements.LegendDetectionRunRow> QueueManualRunAsync(
        Guid clipId,
        string? provider,
        string? model,
        string? reasoningEffort)
    {
        (string resolvedProvider, string resolvedModel, string? effort) = Resolve(provider, model, reasoningEffort);
        _ = await clipsStatements.GetClipById(clipId) ?? throw new NotFoundException("Clip", clipId);
        return await statements.QueueManualRunAsync(clipId, resolvedProvider, resolvedModel, effort);
    }

    public async Task<List<LegendDetectionStatements.LegendDetectionRunRow>> GetRunsAsync(Guid clipId)
    {
        return await statements.GetRunsForClipAsync(clipId);
    }

    /// <summary>
    /// The owner's Apex Legends clips with their latest run, the thumbnails the model is sent and a player to
    /// check the result against.
    /// </summary>
    public async Task<List<LegendDetectionReviewClip>> GetClipsForReviewAsync()
    {
        GameCategory? apex = await gameCategoryStatements.GetBySlugAsync(ApexLegendsSlug);
        if (apex is null)
        {
            return [];
        }

        List<LegendDetectionStatements.ReviewClipRow> clips = await statements.GetClipsForReviewAsync(apex.Id);
        List<Guid> clipIds = clips.Select(clip => clip.Id).ToList();
        Dictionary<Guid, LegendDetectionStatements.LatestRunRow> latestRuns =
            (await statements.GetLatestRunsAsync(clipIds)).ToDictionary(run => run.ClipId);
        Dictionary<Guid, LegendDetectionStatements.LabelRow> labels =
            (await statements.GetLabelsAsync(clipIds)).ToDictionary(label => label.ClipId);

        string cdnBaseUrl = CdnBaseUrl();
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");

        return clips.Select(clip =>
        {
            latestRuns.TryGetValue(clip.Id, out LegendDetectionStatements.LatestRunRow? latest);
            labels.TryGetValue(clip.Id, out LegendDetectionStatements.LabelRow? label);
            return new LegendDetectionReviewClip(
                clip.Id,
                clip.Title ?? "Untitled",
                clip.OwnerName,
                clip.CreatedAt,
                clip.Length,
                $"https://player.mediadelivery.net/embed/{libraryId}/{clip.VideoId}?autoplay=false",
                ThumbnailUrls(cdnBaseUrl, clip.VideoId),
                latest is null ? null : LegendDetectionRun.From(latest),
                (int)(latest?.RunCount ?? 0),
                label is null
                    ? null
                    : new LegendDetectionLabel(label.PlayerLegend, label.TeammateLegends.ToList(), label.LabelledAt));
        }).ToList();
    }

    /// <summary>
    /// Records the legends actually in a clip. Names are matched to the reference sheet's spelling; a null player
    /// legend means the owner's legend can't be identified from the clip.
    /// </summary>
    public async Task SetLabelAsync(Guid clipId, string? playerLegend, List<string> teammateLegends, Guid labelledBy)
    {
        if (teammateLegends.Count > 2)
        {
            throw new BadRequestException("A squad has at most two teammates");
        }

        string? player = playerLegend is null ? null : CanonicalLegend(playerLegend);
        string[] teammates = teammateLegends.Select(CanonicalLegend).ToArray();
        _ = await clipsStatements.GetClipById(clipId) ?? throw new NotFoundException("Clip", clipId);
        await statements.SetLabelAsync(clipId, player, teammates, labelledBy);
    }

    public async Task DeleteLabelAsync(Guid clipId)
    {
        await statements.DeleteLabelAsync(clipId);
    }

    private static string CanonicalLegend(string legend)
    {
        return ApexLegendNames.Canonicalize(legend)
               ?? throw new BadRequestException($"'{legend}' is not a legend on the reference sheet");
    }

    public async Task<List<LegendDetectionUsage>> GetUsageAsync()
    {
        return (await statements.GetUsageAsync(ConfidentAt))
            .Select(row => new LegendDetectionUsage(
                row.Provider,
                row.Model,
                row.ReasoningEffort,
                row.PromptVersion,
                (int)row.Succeeded,
                (int)row.Failed,
                (int)row.Queued,
                row.InputTokens,
                row.CachedInputTokens,
                row.OutputTokens,
                row.AverageDurationMs,
                (int)row.Labelled,
                (int)row.PlayerCorrect,
                (int)row.SquadLabelled,
                (int)row.TeammatesCorrect,
                (int)row.ConfidentMistakes,
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

        try
        {
            LegendFrame[] ownerPanels = await Task.WhenAll(
                frames.Select(frame => LegendHudCropper.CropOwnerPanelAsync(frame, cancellationToken)));
            Stopwatch stopwatch = Stopwatch.StartNew();
            ILegendRecognizer recognizer = recognizerFactory.Create(run.Provider, run.Model, run.ReasoningEffort);
            LegendRecognition recognition = await recognizer.RecognizeAsync(
                new LegendClipImages(frames[0], ownerPanels), cancellationToken);
            await statements.CompleteRunAsync(run.Id, resources.PromptVersion, frames.Count, recognition,
                (int)stopwatch.ElapsedMilliseconds);

            logger.LogInformation(
                "Legend detection for clip {ClipId} with {Provider}/{Model} ({ReasoningEffort}): {Legend} ({Confidence:0.00})",
                run.ClipId, run.Provider, run.Model, run.ReasoningEffort ?? "default effort",
                recognition.Result.Player.Legend ?? "none",
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

    /// <summary>
    /// The provider, model and reasoning effort for a run, each falling back to configuration when not given.
    /// </summary>
    private (string Provider, string Model, string? ReasoningEffort) Resolve(
        string? provider,
        string? model,
        string? reasoningEffort)
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

        string? resolvedEffort;
        try
        {
            resolvedEffort = LegendReasoningEffort.Normalize(
                string.IsNullOrWhiteSpace(reasoningEffort) ? current.ReasoningEffort : reasoningEffort);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        return (resolvedProvider.ToLowerInvariant(), resolvedModel, resolvedEffort);
    }
}
