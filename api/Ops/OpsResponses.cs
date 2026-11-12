namespace Reelshelf.Ops;

// What GET /api/admin/ops/* answer, for the monitor app. Each response is at most OpsSnapshotCache.Lifetime old;
// GeneratedAt says when it was read from the database.

/// <summary>Clip storage as the database records it (never by listing the bucket), and the store it is kept in.</summary>
public sealed record OpsStorageResponse(
    DateTimeOffset GeneratedAt,
    OpsObjectStore Store,
    OpsStorageTotals Total,
    List<OpsTierStorage> ByTier,
    List<OpsAccountStorage> TopAccounts,
    OpsExportStorage Exports);

/// <summary>
/// The object store this instance is configured with, without credentials: AWS S3 by region (<c>s3</c>), or an
/// S3-compatible endpoint by its host (<c>s3-compatible</c>). Every field but <see cref="Configured"/> is null while
/// none is configured.
/// </summary>
public sealed record OpsObjectStore(
    bool Configured,
    string? Kind,
    string? Region,
    string? EndpointHost,
    string? Bucket,
    string? MediaBaseUrl);

/// <summary>
/// Every clip, counted as the plan counts it (the larger of <c>file_size</c> and <c>storage_size</c>), binned ones
/// included; the binned ones again on their own.
/// </summary>
public sealed record OpsStorageTotals(long Clips, long Bytes, long BinnedClips, long BinnedBytes);

/// <summary>The clips of the accounts on a tier (free, plus, pro, unlimited) as their tier is now.</summary>
/// <param name="Accounts">Accounts on the tier holding at least one clip.</param>
public sealed record OpsTierStorage(string Tier, long Accounts, long Clips, long Bytes);

public sealed record OpsAccountStorage(Guid UserId, string Username, string Tier, long Clips, long Bytes);

/// <summary>Over-limit exports: those ready to download (held in storage until they expire) and those being made.</summary>
public sealed record OpsExportStorage(long Held, long HeldBytes, long Pending, long Running);

/// <summary>One queue's numbers for one trigger (or, for <c>transcode_run</c>, one job type).</summary>
/// <param name="Name">The trigger, or the job type for transcoding.</param>
/// <param name="Pending">Claimable now.</param>
/// <param name="Retrying">Pending, waiting out the retry delay after a failed attempt.</param>
/// <param name="StaleRunning">Running past the queue's stale-after: their worker stopped, and they will be claimed again.</param>
/// <param name="OldestPendingAgeSeconds">How long the oldest pending run has waited; 0 when none.</param>
public sealed record OpsQueueCounts(
    string Name,
    long Pending,
    long Retrying,
    long Running,
    long StaleRunning,
    double OldestPendingAgeSeconds,
    OpsFinishedCounts LastHour,
    OpsFinishedCounts LastDay);

/// <summary>Runs that finished in the window; exhausted ones failed because they used up their attempts.</summary>
public sealed record OpsFinishedCounts(long Succeeded, long Failed, long Exhausted);

public sealed record OpsTranscodingResponse(
    DateTimeOffset GeneratedAt,
    List<OpsQueueCounts> Types,
    List<OpsTranscodeFailure> RecentFailures,
    OpsOverflow Overflow,
    OpsAacBackfill AacBackfill);

/// <summary>A failed job, newest first. Which worker ran it isn't recorded (only logged).</summary>
public sealed record OpsTranscodeFailure(
    Guid RunId,
    Guid ClipId,
    string Type,
    string? Error,
    int Attempts,
    DateTimeOffset? FinishedAt);

/// <summary>
/// The overflow pools in use. Tasks running are as the scaler last counted them on the instance that answered; they
/// are never counted for this request, since counting may stop tasks stuck provisioning.
/// </summary>
/// <param name="Running">Every counted pool's tasks together; null while none has a count.</param>
public sealed record OpsOverflow(bool Enabled, int MaxTasks, long? Running, List<OpsOverflowPool> Pools);

/// <param name="Running">Null before the pool's first count, after one failed, or on an instance not scaling.</param>
public sealed record OpsOverflowPool(string Name, int MaxTasks, long? Running);

/// <summary>The AAC backfill: whether it runs, and the ready clips still carrying Opus audio.</summary>
public sealed record OpsAacBackfill(bool Enabled, long Remaining);

public sealed record OpsAiResponse(DateTimeOffset GeneratedAt, List<OpsAiQueue> Queues, List<OpsAiSpend> Spend);

/// <summary>
/// A run queue (<c>legend_detection</c>, <c>clip_transcription</c>, <c>clip_summary</c>) by trigger, with its
/// latest failures. Archived legend detection runs are left out, as the queue gauges leave them.
/// </summary>
public sealed record OpsAiQueue(string Queue, List<OpsQueueCounts> Triggers, List<OpsAiFailure> RecentFailures);

public sealed record OpsAiFailure(
    Guid RunId,
    Guid ClipId,
    string Trigger,
    string? Provider,
    string Model,
    string? Error,
    int Attempts,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Runs finished by one model, over the last day and the last 30 days (LastMonth). Only legend detection records a cost: what the provider
/// billed (<c>cost_usd</c>), else its tokens at the model's prices (<c>legend_model_price</c>).
/// </summary>
public sealed record OpsAiSpend(string Queue, string? Provider, string Model, OpsAiUsage LastDay, OpsAiUsage LastMonth);

/// <param name="CostUsd">The cost of the runs whose cost is known; null when none is.</param>
/// <param name="UnpricedRuns">Runs whose cost isn't known, so <paramref name="CostUsd"/> leaves them out.</param>
public sealed record OpsAiUsage(long Runs, long InputTokens, long OutputTokens, decimal? CostUsd, long UnpricedRuns);
