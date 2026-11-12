using System.Net;
using Reelshelf.Billing;
using Stripe;

namespace Reelshelf.Ops.Costs;

/// <summary>
/// Stripe's revenue (not a cost) from its balance transactions, through <see cref="IStripeBilling"/> with billing's own
/// restricted key, which needs read access to balance transactions. Configured wherever billing is
/// (<c>Billing:SecretKey</c>). Gross is charges' amounts; net is every transaction's net but payouts, transfers and
/// top-ups, so Stripe's fees (Managed Payments' come later, as their own transactions), refunds and disputes are
/// taken off. Amounts are in the account's settlement currency.
/// </summary>
public sealed class StripeRevenueSource(IServiceProvider services) : ICostSource
{
    public const string Name = "stripe";

    /// <summary>Reporting categories that move money out of the balance without being revenue or its costs.</summary>
    private static readonly HashSet<string> NotRevenue =
    [
        "payout", "payout_reversal", "transfer", "transfer_reversal", "topup", "topup_reversal",
        "connect_reserved_funds"
    ];

    /// <summary>Currencies without a minor unit (Stripe's zero-decimal currencies).</summary>
    private static readonly HashSet<string> ZeroDecimal =
    [
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof",
        "xpf"
    ];

    public string Provider => Name;

    public string Kind => CostKinds.Revenue;

    public bool IsConfigured => services.GetService<IStripeBilling>() is not null;

    public async Task<ProviderCosts> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        IStripeBilling stripe = services.GetRequiredService<IStripeBilling>();
        DateOnly start = CostWindow.ReadStart(today);
        List<StripeBalanceEntry> transactions;
        try
        {
            transactions = await stripe.ListBalanceTransactionsAsync(
                new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), cancellationToken);
        }
        catch (StripeException ex) when (ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new CostSourceException(
                "Billing:SecretKey's restricted key can't read balance transactions. Give it Read on Balance " +
                "transactions to see revenue here.", ex);
        }
        catch (StripeException ex)
        {
            throw new CostSourceException($"Stripe refused: {ex.Message}", ex);
        }

        return Summarize(transactions, today);
    }

    internal static ProviderCosts Summarize(IReadOnlyCollection<StripeBalanceEntry> transactions, DateOnly today)
    {
        // One settlement currency in practice; should there be more, the busiest one.
        string currency = transactions
            .GroupBy(t => t.Currency.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "usd";
        decimal unit = ZeroDecimal.Contains(currency) ? 1m : 100m;
        List<StripeBalanceEntry> counted = transactions
            .Where(t => t.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase) &&
                        !NotRevenue.Contains(t.ReportingCategory) && !NotRevenue.Contains(t.Type))
            .ToList();

        DateOnly monthStart = CostWindow.MonthStart(today);
        DateOnly lastMonthStart = CostWindow.LastMonthStart(today);
        bool ThisMonth(StripeBalanceEntry t) => CostHttp.Day(t.Created) >= monthStart;
        bool LastMonth(StripeBalanceEntry t) => CostHttp.Day(t.Created) >= lastMonthStart && !ThisMonth(t);
        bool Charge(StripeBalanceEntry t) => t.ReportingCategory == "charge";

        // The chart is gross charges per day.
        List<CostEntry> gross = counted.Where(Charge)
            .Select(t => new CostEntry(CostHttp.Day(t.Created), t.ReportingCategory, t.Amount / unit))
            .ToList();
        ProviderCosts summary = CostWindow.Summarize(currency, gross, today);
        OpsRevenue revenue = new(
            counted.Where(t => Charge(t) && ThisMonth(t)).Sum(t => t.Amount) / unit,
            counted.Where(ThisMonth).Sum(t => t.Net) / unit,
            counted.Where(t => Charge(t) && LastMonth(t)).Sum(t => t.Amount) / unit,
            counted.Where(LastMonth).Sum(t => t.Net) / unit);
        return summary with { Breakdown = [], Revenue = revenue };
    }
}
