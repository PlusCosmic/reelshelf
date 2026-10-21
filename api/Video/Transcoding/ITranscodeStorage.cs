namespace Reelshelf.Video.Transcoding;

/// <summary>
/// The storage transcode jobs read from and write to: R2 (<see cref="R2.R2Storage"/>) when it is configured, else
/// <see cref="UnconfiguredTranscodeStorage"/>. The queue asks it for a claimed job's presigned URLs and for the
/// clean-up a finished job leaves (the uploaded source, a replaced version).
/// </summary>
public interface ITranscodeStorage
{
    /// <summary>
    /// Whether this is real storage. Without it the claim hands out no <c>migrate</c> job, since its worker would
    /// encode a Bunny clip with nowhere to put the result, and the migration backfill queues none.
    /// </summary>
    bool IsConfigured => true;

    /// <summary>
    /// The presigned URLs a worker needs for a job, valid for <paramref name="lifetime"/>: a GET for its input and a
    /// PUT for each file it writes under <c>clips/{clipId}/v{videoVersion}/</c>. A <c>migrate</c> job reads from Bunny,
    /// so it gets no GET here.
    /// </summary>
    /// <param name="videoVersion">The version the job writes (or, for a thumbnails job, reads and rewrites).</param>
    /// <param name="currentVersion">The clip's current version, which a trim reads from.</param>
    TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime);

    /// <summary>Deletes the clip's uploaded source. Idempotent.</summary>
    Task DeleteSourceAsync(Guid clipId);

    /// <summary>Deletes one version's outputs. Idempotent.</summary>
    Task DeleteVersionAsync(Guid clipId, int videoVersion);

    /// <summary>
    /// Deletes every version's outputs under <c>clips/{clipId}/</c> except <paramref name="keepVersion"/>'s, as a
    /// finished <c>migrate</c> leaves the versions of an earlier move. Never touches the uploaded source. Idempotent.
    /// </summary>
    Task DeleteOtherVersionsAsync(Guid clipId, int keepVersion);

    /// <summary>Deletes every version's outputs under <c>clips/{clipId}/</c>, but not the uploaded source. Idempotent.</summary>
    Task DeleteAllVersionsAsync(Guid clipId);
}

/// <summary>A claimed job's URLs, as the claim response carries them (both null when there is no storage).</summary>
public sealed record TranscodeJobUrls(string? SourceUrl, IReadOnlyDictionary<string, string>? UploadUrls)
{
    public static readonly TranscodeJobUrls None = new(null, null);
}

/// <summary>
/// Stands in while R2 is not configured: nothing can be uploaded to R2 then, so no job should exist, and any that
/// does is handed out without URLs (the worker fails it) and leaves nothing to clean up. A <c>migrate</c> job is never
/// handed out at all (<see cref="IsConfigured"/>): it would otherwise fail and be left for a person to look at.
/// </summary>
public sealed class UnconfiguredTranscodeStorage(ILogger<UnconfiguredTranscodeStorage> logger) : ITranscodeStorage
{
    public bool IsConfigured => false;

    public TranscodeJobUrls UrlsFor(Guid clipId, string jobType, int videoVersion, int currentVersion, TimeSpan lifetime)
    {
        logger.LogWarning("Handing out {Type} job for clip {ClipId} without URLs: R2 is not configured", jobType, clipId);
        return TranscodeJobUrls.None;
    }

    public Task DeleteSourceAsync(Guid clipId) => Task.CompletedTask;

    public Task DeleteVersionAsync(Guid clipId, int videoVersion) => Task.CompletedTask;

    public Task DeleteOtherVersionsAsync(Guid clipId, int keepVersion) => Task.CompletedTask;

    public Task DeleteAllVersionsAsync(Guid clipId) => Task.CompletedTask;
}
