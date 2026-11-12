using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// Every provider's last read, in memory: <see cref="RefreshDueAsync"/> (from <see cref="OpsCostsBackgroundService"/>)
/// reads each one whose turn has come, and <see cref="Snapshot"/> answers <c>GET /api/admin/ops/costs</c> from what is
/// kept, never calling a provider for a request. Each instance keeps its own (two may run during a deploy; that only
/// costs AWS an extra request).
/// </summary>
public sealed class OpsCosts(
    IEnumerable<ICostSource> sources,
    IOptionsMonitor<OpsCostsOptions> options,
    TimeProvider timeProvider,
    ILogger<OpsCosts> logger)
{
    /// <summary>The order the providers are shown in.</summary>
    private readonly List<ICostSource> _sources = sources.ToList();

    private readonly ConcurrentDictionary<string, State> _states = new();

    /// <summary>A provider's last read: the last good numbers, and how the latest attempt went.</summary>
    private sealed record State(ProviderCosts? Costs, DateTimeOffset? LastSuccessAt, DateTimeOffset LastAttemptAt,
        string? Error);

    /// <summary>Reads every configured provider whose numbers are older than its interval, or whose retry is due.</summary>
    public async Task RefreshDueAsync(CancellationToken cancellationToken)
    {
        OpsCostsOptions current = options.CurrentValue;
        foreach (ICostSource source in _sources.Where(s => s.IsConfigured))
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_states.TryGetValue(source.Provider, out State? state))
            {
                TimeSpan wait = state.Error is null ? source.RefreshEvery(current) : current.RetryAfter;
                if (now - state.LastAttemptAt < wait)
                {
                    continue;
                }
            }

            await RefreshAsync(source, now, cancellationToken);
        }
    }

    private async Task RefreshAsync(ICostSource source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        State? previous = _states.GetValueOrDefault(source.Provider);
        try
        {
            ProviderCosts costs = await source.ReadAsync(CostWindow.Today(timeProvider), cancellationToken);
            _states[source.Provider] = new State(costs, timeProvider.GetUtcNow(), now, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            string error = ex switch
            {
                CostSourceException => ex.Message,
                HttpRequestException or TaskCanceledException => $"Couldn't reach it: {ex.Message}",
                _ => $"Couldn't read it: {ex.GetType().Name}"
            };
            logger.LogWarning(ex, "Couldn't read {Provider}'s costs", source.Provider);
            _states[source.Provider] = new State(previous?.Costs, previous?.LastSuccessAt, now, error);
        }
    }

    /// <summary>What each provider last reported.</summary>
    public OpsCostsResponse Snapshot()
    {
        List<OpsCostProvider> providers = _sources.Select(Provider).ToList();
        decimal monthToDate = providers
            .Where(p => p.Kind == CostKinds.Cost && p.Currency == "USD")
            .Sum(p => p.MonthToDate ?? 0m);
        return new OpsCostsResponse(timeProvider.GetUtcNow(), monthToDate, providers);
    }

    private OpsCostProvider Provider(ICostSource source)
    {
        if (!source.IsConfigured)
        {
            return new OpsCostProvider(source.Provider, source.Kind, CostStatuses.NotConfigured, null, null, null,
                null, null, [], [], null, null);
        }

        State? state = _states.GetValueOrDefault(source.Provider);
        ProviderCosts? costs = state?.Costs;
        string status = state is null ? CostStatuses.Pending :
            state.Error is null ? CostStatuses.Ok : CostStatuses.Failed;
        return new OpsCostProvider(source.Provider, source.Kind, status, state?.Error, state?.LastSuccessAt,
            costs?.Currency, costs?.MonthToDate, costs?.LastMonth, costs?.Daily ?? [], costs?.Breakdown ?? [],
            costs?.Balance, costs?.Revenue);
    }
}

/// <summary>
/// Reads the providers when the API starts and checks every <see cref="Tick"/> which are due (each every hour, AWS
/// every 6 h, a failed one after <see cref="OpsCostsOptions.RetryAfter"/>). Never runs in <c>OpenApi</c>.
/// </summary>
public sealed class OpsCostsBackgroundService(OpsCosts costs, ILogger<OpsCostsBackgroundService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await costs.RefreshDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to refresh the ops costs");
            }

            await Task.Delay(Tick, stoppingToken);
        }
    }
}
