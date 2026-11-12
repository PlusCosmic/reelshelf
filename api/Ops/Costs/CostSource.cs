namespace Reelshelf.Ops.Costs;

/// <summary>One billing provider the Costs section reads.</summary>
public interface ICostSource
{
    /// <summary><c>aws</c>, <c>openrouter</c>, <c>openai</c>, <c>anthropic</c> or <c>stripe</c>.</summary>
    string Provider { get; }

    /// <summary><see cref="CostKinds.Cost"/>, or <see cref="CostKinds.Revenue"/>.</summary>
    string Kind => CostKinds.Cost;

    /// <summary>
    /// Whether any credentials are set. A source that is configured but wrongly (half a key, say) says so by
    /// throwing <see cref="CostSourceException"/> from <see cref="ReadAsync"/>.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>How long a good read is kept before reading again.</summary>
    TimeSpan RefreshEvery(OpsCostsOptions options) => options.RefreshInterval;

    Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken);
}

public static class CostKinds
{
    public const string Cost = "cost";
    public const string Revenue = "revenue";
}

public static class CostStatuses
{
    public const string Ok = "ok";
    public const string NotConfigured = "not_configured";
    public const string Pending = "pending";
    public const string Failed = "failed";
}

/// <summary>A read refused or misconfigured, with words fit for the monitor (never a key).</summary>
public sealed class CostSourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A provider's numbers as last read.</summary>
public sealed record ProviderCosts(
    string Currency,
    decimal? MonthToDate,
    decimal? LastMonth,
    List<OpsCostDay> Daily,
    List<OpsCostItem> Breakdown,
    decimal? Balance = null,
    OpsRevenue? Revenue = null);

/// <summary>One amount on one day for one service or model, as a provider reports it.</summary>
public sealed record CostEntry(DateOnly Date, string Name, decimal Amount);

/// <summary>The days the Costs section covers, in UTC: this month, last month and the last 30 days.</summary>
public static class CostWindow
{
    public const int Days = 30;

    /// <summary>Breakdowns show this many names; the rest are added up as <see cref="OtherName"/>.</summary>
    public const int BreakdownSize = 12;

    public const string OtherName = "Other";

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public static DateOnly MonthStart(DateOnly today) => new(today.Year, today.Month, 1);

    public static DateOnly LastMonthStart(DateOnly today) => MonthStart(today).AddMonths(-1);

    /// <summary>The first of the last 30 days, today included.</summary>
    public static DateOnly DailyStart(DateOnly today) => today.AddDays(-(Days - 1));

    /// <summary>The first day a provider is asked for: last month's first, or 30 days back when that is earlier.</summary>
    public static DateOnly ReadStart(DateOnly today)
    {
        DateOnly lastMonth = LastMonthStart(today);
        DateOnly daily = DailyStart(today);
        return lastMonth < daily ? lastMonth : daily;
    }

    /// <summary>
    /// Totals, the 30 days (every one of them, zero where nothing was spent, when <paramref name="fillDays"/>; else
    /// only the days with entries) and the breakdown from a provider's daily entries covering <see cref="ReadStart"/>
    /// to today.
    /// </summary>
    public static ProviderCosts Summarize(string currency, IReadOnlyCollection<CostEntry> entries, DateOnly today,
        bool lastMonthKnown = true, bool fillDays = true)
    {
        DateOnly monthStart = MonthStart(today);
        DateOnly lastMonthStart = LastMonthStart(today);
        DateOnly dailyStart = DailyStart(today);
        decimal monthToDate = entries.Where(e => e.Date >= monthStart && e.Date <= today).Sum(e => e.Amount);
        decimal lastMonth = entries.Where(e => e.Date >= lastMonthStart && e.Date < monthStart).Sum(e => e.Amount);
        Dictionary<DateOnly, decimal> byDay = entries
            .Where(e => e.Date >= dailyStart && e.Date <= today)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        List<OpsCostDay> daily = fillDays
            ? Enumerable.Range(0, Days)
                .Select(i => dailyStart.AddDays(i))
                .Select(day => new OpsCostDay(day, byDay.GetValueOrDefault(day)))
                .ToList()
            : byDay.OrderBy(d => d.Key).Select(d => new OpsCostDay(d.Key, d.Value)).ToList();
        return new ProviderCosts(currency.ToUpperInvariant(), monthToDate, lastMonthKnown ? lastMonth : null, daily,
            Breakdown(entries.Where(e => e.Date >= dailyStart && e.Date <= today)));
    }

    /// <summary>By name, largest first, leaving out what rounds to nothing, the tail added up as "Other".</summary>
    public static List<OpsCostItem> Breakdown(IEnumerable<CostEntry> entries)
    {
        List<OpsCostItem> items = entries
            .GroupBy(e => e.Name)
            .Select(g => new OpsCostItem(g.Key, g.Sum(e => e.Amount)))
            .Where(item => Math.Abs(item.Amount) >= 0.005m)
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        if (items.Count <= BreakdownSize)
        {
            return items;
        }

        List<OpsCostItem> shown = items.Take(BreakdownSize - 1).ToList();
        shown.Add(new OpsCostItem(OtherName, items.Skip(BreakdownSize - 1).Sum(item => item.Amount)));
        return shown;
    }
}
