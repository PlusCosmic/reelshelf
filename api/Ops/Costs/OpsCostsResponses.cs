namespace Reelshelf.Ops.Costs;

// What GET /api/admin/ops/costs answers, for the monitor app's Costs section. Providers are read in the background
// (OpsCostsBackgroundService) and the answer is served from memory, so it is as old as each provider's LastSuccessAt.

/// <param name="MonthToDateUsd">
/// Every cost provider's month-to-date spend reported in USD, added up (revenue is not a cost and is left out).
/// </param>
public sealed record OpsCostsResponse(
    DateTimeOffset GeneratedAt,
    decimal MonthToDateUsd,
    List<OpsCostProvider> Providers);

/// <summary>
/// One provider's spend (or, for Stripe, revenue) in the currency it reports, with how its last read went. While a
/// read fails the last good numbers stay, with <see cref="Status"/> <c>failed</c> and the <see cref="Error"/>.
/// </summary>
/// <param name="Provider"><c>aws</c>, <c>openrouter</c>, <c>openai</c>, <c>anthropic</c> or <c>stripe</c>.</param>
/// <param name="Kind"><c>cost</c>, or <c>revenue</c> for Stripe.</param>
/// <param name="Status">
/// <c>ok</c>; <c>not_configured</c> (no credentials: nothing else is set); <c>pending</c> (not read yet since the
/// instance started); or <c>failed</c>.
/// </param>
/// <param name="LastSuccessAt">When the numbers shown were read; null before the first good read.</param>
/// <param name="Currency">The currency the provider reports, upper case (<c>USD</c>, <c>GBP</c>).</param>
/// <param name="MonthToDate">Since the start of this month (UTC); for Stripe, gross charges.</param>
/// <param name="LastMonth">The whole of last month; null when the provider keeps too little history to say.</param>
/// <param name="Daily">Each of the last 30 days (UTC), oldest first, for the days the provider reports.</param>
/// <param name="Breakdown">The last 30 days by service or model, largest first, where the provider splits it.</param>
/// <param name="Balance">Credit left to spend (OpenRouter), when known.</param>
/// <param name="Revenue">Stripe's gross and net, when this is revenue.</param>
public sealed record OpsCostProvider(
    string Provider,
    string Kind,
    string Status,
    string? Error,
    DateTimeOffset? LastSuccessAt,
    string? Currency,
    decimal? MonthToDate,
    decimal? LastMonth,
    List<OpsCostDay> Daily,
    List<OpsCostItem> Breakdown,
    decimal? Balance,
    OpsRevenue? Revenue);

public sealed record OpsCostDay(DateOnly Date, decimal Amount);

/// <summary>A service (AWS), line item (OpenAI) or model (OpenRouter, Anthropic) and what it cost.</summary>
public sealed record OpsCostItem(string Name, decimal Amount);

/// <summary>
/// Stripe's charges: gross is what customers paid, net is what reaches the balance after Stripe's fees (Managed
/// Payments' included), refunds and disputes. Payouts and transfers aren't counted.
/// </summary>
public sealed record OpsRevenue(decimal GrossMonthToDate, decimal NetMonthToDate, decimal GrossLastMonth, decimal NetLastMonth);
