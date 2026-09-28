using System.Diagnostics;
using Microsoft.Extensions.Options;
using Reelshelf.Auth;
using Reelshelf.Bunny.Models;
using Reelshelf.Core;
using Reelshelf.Exceptions;
using Reelshelf.Users;

namespace Reelshelf.ClipTranscription;

public class ClipTranscriptionService(
    ClipTranscriptionStatements statements,
    ClipsStatements clipsStatements,
    UserStatements userStatements,
    WhitelistService whitelistService,
    IClipTranscriber transcriber,
    IOptionsMonitor<ClipTranscriptionOptions> options,
    IConfiguration configuration,
    ILogger<ClipTranscriptionService> logger)
{
    /// <summary>The largest file the transcription endpoint accepts.</summary>
    public const long MaxUploadBytes = 25L * 1024 * 1024;

    /// <summary>Retry a failed attempt after this long, so a brief provider or CDN outage is not retried at once.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>A run still marked running after this long belonged to a worker that stopped; claim it again.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether a clip that just finished encoding gets its automatic run: automatic queueing is on, transcription
    /// is configured, the video is encoded and the owner is whitelisted.
    /// </summary>
    public static bool ShouldAutoQueue(ClipTranscriptionOptions options, int? videoStatus, bool ownerWhitelisted)
    {
        return options.AutoQueue
               && options.IsConfigured
               && videoStatus is (int)BunnyVideoStatus.Finished or (int)BunnyVideoStatus.ResolutionFinished
               && ownerWhitelisted;
    }

    /// <summary>
    /// Queues the automatic run for a clip whose video has finished encoding, when its owner is whitelisted. Safe
    /// to call on every webhook: a clip only ever gets one automatic run.
    /// </summary>
    public async Task QueueForEncodedClipAsync(ClipsStatements.ClipRow clip, int videoStatus)
    {
        ClipTranscriptionOptions current = options.CurrentValue;
        if (!ShouldAutoQueue(current, videoStatus, ownerWhitelisted: true))
        {
            return;
        }

        List<UserStatements.UserIdentityRow> identities = await userStatements.GetIdentitiesForUser(clip.OwnerId);
        if (!ShouldAutoQueue(current, videoStatus, whitelistService.IsWhitelisted(identities.Select(i => i.ToRef()))))
        {
            return;
        }

        if (await statements.QueueAutoRunAsync(clip.Id, current.Model.Trim()))
        {
            logger.LogInformation("Queued transcription for clip {ClipId}", clip.Id);
        }
    }

    /// <summary>Queues the automatic run for every encoded clip of a whitelisted owner that has never had one.</summary>
    public async Task<int> BackfillAsync()
    {
        string model = ConfiguredModel();
        List<Guid> owners = await GetWhitelistedOwnersAsync();
        int queued = await statements.QueueAutoRunsForOwnersAsync(owners, model);
        logger.LogInformation("Backfill queued transcription for {Count} clips from {Owners} whitelisted owners",
            queued, owners.Count);
        return queued;
    }

    /// <summary>
    /// Queues a run with the configured model for every clip whose latest run succeeded with audio but no words,
    /// made with a different model. Safe to repeat: once a clip's latest run uses the configured model, it is left
    /// alone whatever that run found.
    /// </summary>
    public async Task<int> RetryEmptyAsync()
    {
        string model = ConfiguredModel();
        int queued = await statements.QueueRetriesForEmptyRunsAsync(model);
        logger.LogInformation("Queued {Count} transcription retries with {Model} for clips that came back empty",
            queued, model);
        return queued;
    }

    /// <summary>Queues an extra run for one clip, optionally with a different model to compare against.</summary>
    public async Task<ClipTranscriptionStatements.ClipTranscriptionRunRow> QueueManualRunAsync(Guid clipId, string? model)
    {
        string configuredModel = ConfiguredModel();
        string resolvedModel = string.IsNullOrWhiteSpace(model) ? configuredModel : model.Trim();
        _ = await clipsStatements.GetClipById(clipId) ?? throw new NotFoundException("Clip", clipId);
        return await statements.QueueManualRunAsync(clipId, resolvedModel);
    }

    public async Task<List<ClipTranscriptionStatements.ClipTranscriptionRunRow>> GetRunsAsync(Guid clipId)
    {
        return await statements.GetRunsForClipAsync(clipId);
    }

    /// <summary>Whitelisted owners' clips and any other transcribed clip, each with its latest run.</summary>
    public async Task<List<ClipTranscriptionReviewClip>> GetClipsForReviewAsync()
    {
        List<ClipTranscriptionStatements.ReviewClipRow> clips =
            await statements.GetClipsForReviewAsync(await GetWhitelistedOwnersAsync());
        Dictionary<Guid, ClipTranscriptionStatements.LatestRunRow> latestRuns =
            (await statements.GetLatestRunsAsync(clips.Select(clip => clip.Id).ToList())).ToDictionary(run => run.ClipId);

        string cdnBaseUrl = configuration["BunnyCdnBaseUrl"]
                            ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
        string libraryId = configuration["BunnyLibraryId"]
                           ?? throw new InvalidOperationException("Bunny API library ID not configured");

        return clips.Select(clip =>
        {
            latestRuns.TryGetValue(clip.Id, out ClipTranscriptionStatements.LatestRunRow? latest);
            return new ClipTranscriptionReviewClip(
                clip.Id,
                clip.Title ?? "Untitled",
                clip.OwnerName,
                clip.GameName,
                clip.GameSlug,
                clip.CreatedAt,
                clip.Length,
                $"https://player.mediadelivery.net/embed/{libraryId}/{clip.VideoId}?autoplay=false",
                $"{cdnBaseUrl.TrimEnd('/')}/{clip.VideoId}/thumbnail.jpg",
                latest is null ? null : ClipTranscriptionRun.From(latest),
                (int)(latest?.RunCount ?? 0));
        }).ToList();
    }

    public async Task<List<ClipTranscriptionUsage>> GetUsageAsync()
    {
        Dictionary<string, decimal> prices = options.CurrentValue.CostPerMinuteUsd;
        return (await statements.GetUsageAsync())
            .Select(row => new ClipTranscriptionUsage(
                row.Model,
                row.PromptVersion,
                (int)row.Succeeded,
                (int)row.Failed,
                (int)row.Queued,
                (int)row.WithoutAudio,
                (int)row.WithoutSpeech,
                Math.Round(row.AudioSeconds / 60, 1),
                prices.TryGetValue(row.Model, out decimal perMinute)
                    ? Math.Round((decimal)row.AudioSeconds / 60 * perMinute, 4)
                    : null,
                row.InputTokens,
                row.OutputTokens,
                row.AverageDurationMs))
            .ToList();
    }

    /// <summary>Processes the next queued run. Returns false when there was nothing to do.</summary>
    /// <remarks>
    /// An instance without an API key never claims a run: dev and prod share the queue, and only the instance
    /// holding the key should process it. Its runs stay queued for one that can.
    /// </remarks>
    public async Task<bool> ProcessNextRunAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.IsConfigured)
        {
            return false;
        }

        ClipTranscriptionStatements.ClaimedRunRow? run = await statements.ClaimNextRunAsync(RetryAfter, StaleAfter);
        if (run is null)
        {
            return false;
        }

        ClipTranscriptionOptions current = options.CurrentValue;
        if (run.Attempts > current.MaxAttempts)
        {
            await statements.FailRunAsync(run.Id, $"Gave up after {current.MaxAttempts} attempts", null, retry: false);
            return true;
        }

        bool canRetry = run.Attempts < current.MaxAttempts;
        TranscriptionPrompt prompt = TranscriptionPrompt.For(run.GameName, run.GameSlug, run.Model);
        string audioPath = Path.Combine(Path.GetTempPath(), "clip-transcription", $"{run.Id:N}.m4a");
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ClipAudio audio = await ClipAudioExtractor.ExtractAsync(PlaylistUrl(run.VideoId), audioPath, cancellationToken);
            if (!audio.HasAudio)
            {
                await statements.CompleteRunAsync(run.Id, prompt, audio, null, (int)stopwatch.ElapsedMilliseconds);
                logger.LogInformation("Clip {ClipId} has no audio track; nothing to transcribe", run.ClipId);
                return true;
            }

            if (audio.Bytes > MaxUploadBytes)
            {
                await statements.FailRunAsync(run.Id,
                    $"The clip's audio is {audio.Bytes / (1024 * 1024)} MB, over the {MaxUploadBytes / (1024 * 1024)} MB upload limit",
                    prompt, retry: false);
                return true;
            }

            ClipTranscript transcript = await transcriber.TranscribeAsync(audioPath, run.Model, prompt, cancellationToken);
            await statements.CompleteRunAsync(run.Id, prompt, audio, transcript, (int)stopwatch.ElapsedMilliseconds);
            logger.LogInformation(
                "Transcribed clip {ClipId} with {Model}: {Seconds:0}s of audio, {Characters} characters",
                run.ClipId, run.Model, audio.Seconds, transcript.Text.Length);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Transcription attempt {Attempt} failed for clip {ClipId} with {Model}",
                run.Attempts, run.ClipId, run.Model);
            bool retry = canRetry && ex is not ClipTranscriptionException { Retryable: false };
            await statements.FailRunAsync(run.Id, ex.Message, prompt, retry);
        }
        finally
        {
            TryDelete(audioPath);
        }

        return true;
    }

    private async Task<List<Guid>> GetWhitelistedOwnersAsync()
    {
        return (await statements.GetClipOwnerIdentitiesAsync())
            .GroupBy(identity => identity.UserId)
            .Where(owner => whitelistService.IsWhitelisted(
                owner.Select(identity => new UserIdentityRef(identity.Provider, identity.ProviderUserId))))
            .Select(owner => owner.Key)
            .ToList();
    }

    private string ConfiguredModel()
    {
        ClipTranscriptionOptions current = options.CurrentValue;
        if (!current.IsConfigured)
        {
            throw new BadRequestException(
                "Clip transcription is not configured; set ClipTranscription:ApiKey and ClipTranscription:Model");
        }

        return current.Model.Trim();
    }

    private Uri PlaylistUrl(Guid videoId)
    {
        string cdnBaseUrl = configuration["BunnyCdnBaseUrl"]
                            ?? throw new InvalidOperationException("BunnyCdnBaseUrl is not configured");
        return new Uri($"{cdnBaseUrl.TrimEnd('/')}/{videoId}/playlist.m3u8");
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete transcription audio {Path}", path);
        }
    }
}
