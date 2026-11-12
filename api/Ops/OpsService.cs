using Microsoft.Extensions.Options;
using Npgsql;
using Reelshelf.ApexLegends.LegendDetection;
using Reelshelf.ClipSummary;
using Reelshelf.ClipTranscription;
using Reelshelf.QueueMetrics;
using Reelshelf.Video.R2;
using Reelshelf.Video.Transcoding;
using Reelshelf.Video.Transcoding.Overflow;

namespace Reelshelf.Ops;

/// <summary>
/// Builds the <c>/api/admin/ops/</c> responses from the database and this instance's own state, each kept for
/// <see cref="OpsSnapshotCache.Lifetime"/>. It only reads: it never calls a cloud API, and never starts, stops or
/// counts an overflow task itself.
/// </summary>
public sealed class OpsService(
    OpsStatements statements,
    OpsSnapshotCache cache,
    NpgsqlDataSource dataSource,
    IServiceProvider services,
    IOptionsMonitor<TranscodingOptions> transcodingOptions,
    IOptionsMonitor<OverflowOptions> overflowOptions,
    IOptionsMonitor<LegendDetectionOptions> legendDetectionOptions,
    IOptionsMonitor<ClipTranscriptionOptions> clipTranscriptionOptions,
    IOptionsMonitor<ClipSummaryOptions> clipSummaryOptions,
    TimeProvider timeProvider)
{
    public const string LegendDetectionQueue = "legend_detection";
    public const string ClipTranscriptionQueue = "clip_transcription";
    public const string ClipSummaryQueue = "clip_summary";

    public static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);
    public static readonly TimeSpan Month = TimeSpan.FromDays(30);

    public Task<OpsStorageResponse> GetStorageAsync(CancellationToken cancellationToken) =>
        cache.GetAsync("storage", LoadStorageAsync, cancellationToken);

    public Task<OpsTranscodingResponse> GetTranscodingAsync(CancellationToken cancellationToken) =>
        cache.GetAsync("transcoding", LoadTranscodingAsync, cancellationToken);

    public Task<OpsAiResponse> GetAiAsync(CancellationToken cancellationToken) =>
        cache.GetAsync("ai", LoadAiAsync, cancellationToken);

    private async Task<OpsStorageResponse> LoadStorageAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        OpsStatements.StorageRows rows = await statements.GetStorageAsync(cancellationToken);
        Dictionary<string, OpsStatements.TierRow> byTier = rows.Tiers.ToDictionary(row => row.Tier);
        return new OpsStorageResponse(
            now,
            ObjectStore(services.GetService<R2Options>()),
            new OpsStorageTotals(rows.Total.Clips, rows.Total.Bytes, rows.Total.BinnedClips, rows.Total.BinnedBytes),
            OpsStatements.TierNames.Select(tier => byTier.TryGetValue(tier, out OpsStatements.TierRow? row)
                    ? new OpsTierStorage(tier, row.Accounts, row.Clips, row.Bytes)
                    : new OpsTierStorage(tier, 0, 0, 0))
                .ToList(),
            rows.Accounts.Select(row => new OpsAccountStorage(row.UserId, row.Username, row.Tier, row.Clips, row.Bytes))
                .ToList(),
            new OpsExportStorage(rows.Exports.Held, rows.Exports.HeldBytes, rows.Exports.Pending, rows.Exports.Running));
    }

    /// <summary>Where clips are kept, by name only: never a key or secret.</summary>
    internal static OpsObjectStore ObjectStore(R2Options? options)
    {
        if (options is null)
        {
            return new OpsObjectStore(false, null, null, null, null, null);
        }

        string? serviceUrl = options.ServiceUrl;
        string? endpointHost = serviceUrl is not null && Uri.TryCreate(serviceUrl, UriKind.Absolute, out Uri? uri)
            ? uri.Host
            : null;
        return new OpsObjectStore(
            true,
            serviceUrl is null ? "s3" : "s3-compatible",
            options.Region.Length > 0 ? options.Region : null,
            endpointHost,
            options.Bucket,
            options.MediaBaseUrl);
    }

    private async Task<OpsTranscodingResponse> LoadTranscodingAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        TranscodeRunMetricsSource source = new(dataSource, transcodingOptions);
        List<OpsQueueCounts> types = Combine(
            await source.SampleAsync(Hour, cancellationToken),
            await source.SampleAsync(Day, cancellationToken));
        List<OpsTranscodeFailure> failures = (await statements.GetTranscodeFailuresAsync(cancellationToken))
            .Select(row => new OpsTranscodeFailure(row.RunId, row.ClipId, row.Type, row.Error, row.Attempts,
                row.FinishedAt))
            .ToList();
        long aacRemaining = await statements.CountAacBackfillRemainingAsync(cancellationToken);
        return new OpsTranscodingResponse(now, types, failures, Overflow(),
            new OpsAacBackfill(transcodingOptions.CurrentValue.AacBackfill, aacRemaining));
    }

    /// <summary>
    /// The pools in <c>Overflow:Pools</c>, with the tasks the scaler last counted in each on this instance
    /// (<see cref="OverflowMetrics"/>, registered only where the overflow pool is on).
    /// </summary>
    private OpsOverflow Overflow()
    {
        OverflowOptions current = overflowOptions.CurrentValue;
        OverflowMetrics? metrics = current.Enabled ? services.GetService<OverflowMetrics>() : null;
        List<OpsOverflowPool> pools = current.Enabled
            ? current.PoolList.Select(pool => new OpsOverflowPool(pool, current.MaxTasksFor(pool), metrics?.RunningIn(pool)))
                .ToList()
            : [];
        return new OpsOverflow(current.Enabled, current.MaxTasks, current.Enabled ? metrics?.Running : null, pools);
    }

    private async Task<OpsAiResponse> LoadAiAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Dictionary<string, List<OpsStatements.AiFailureRow>> failures =
            await statements.GetAiFailuresAsync(cancellationToken);
        List<OpsAiQueue> queues = [];
        foreach ((RunTableQueue queue, Func<int> maxAttempts) in new (RunTableQueue, Func<int>)[]
                 {
                     (QueueMetricsSetup.LegendDetectionQueue, () => legendDetectionOptions.CurrentValue.MaxAttempts),
                     (QueueMetricsSetup.ClipTranscriptionQueue, () => clipTranscriptionOptions.CurrentValue.MaxAttempts),
                     (QueueMetricsSetup.ClipSummaryQueue, () => clipSummaryOptions.CurrentValue.MaxAttempts)
                 })
        {
            RunTableMetricsSource source = new(queue, dataSource, maxAttempts);
            List<OpsQueueCounts> triggers = Combine(
                await source.SampleAsync(Hour, cancellationToken),
                await source.SampleAsync(Day, cancellationToken));
            queues.Add(new OpsAiQueue(queue.Queue, triggers, failures.GetValueOrDefault(queue.Queue, [])
                .Select(row => new OpsAiFailure(row.RunId, row.ClipId, row.Trigger, row.Provider, row.Model, row.Error,
                    row.Attempts, row.CompletedAt))
                .ToList()));
        }

        List<OpsAiSpend> spend = (await statements.GetAiSpendAsync(Day, Month, cancellationToken))
            .Select(row => new OpsAiSpend(row.Queue, row.Provider, row.Model,
                new OpsAiUsage(row.DayRuns, row.DayInputTokens, row.DayOutputTokens, row.DayCost, row.DayUnpriced),
                new OpsAiUsage(row.MonthRuns, row.MonthInputTokens, row.MonthOutputTokens, row.MonthCost,
                    row.MonthUnpriced)))
            .ToList();
        return new OpsAiResponse(now, queues, spend);
    }

    /// <summary>
    /// One queue's counts from two samples that differ only in how far back the finished counts look: the open counts
    /// are taken from the hour's.
    /// </summary>
    internal static List<OpsQueueCounts> Combine(IReadOnlyList<RunQueueSample> hour, IReadOnlyList<RunQueueSample> day)
    {
        Dictionary<string, RunQueueSample> byDay = day.ToDictionary(sample => sample.Trigger);
        return hour.Select(sample =>
            {
                RunQueueSample daySample = byDay.GetValueOrDefault(sample.Trigger, RunQueueSample.Empty(sample.Trigger));
                return new OpsQueueCounts(sample.Trigger, sample.Pending, sample.Retrying, sample.Running,
                    sample.StaleRunning, sample.OldestPendingAgeSeconds,
                    new OpsFinishedCounts(sample.SucceededLastHour, sample.FailedLastHour, sample.ExhaustedLastHour),
                    new OpsFinishedCounts(daySample.SucceededLastHour, daySample.FailedLastHour,
                        daySample.ExhaustedLastHour));
            })
            .ToList();
    }
}
