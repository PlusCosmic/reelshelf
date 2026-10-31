using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.Billing;
using Reelshelf.FeatureFlags;

namespace Reelshelf.Plans;

public static class PlanEndpoints
{
    public static void MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("plans", GetPlans).WithName("GetPlans").RequireAuthorization();
    }

    /// <summary>
    /// Every tier's limits, and each paid one's prices as Stripe has them (in the price's own currency; Checkout
    /// shows the customer's). Paid tiers carry no prices while billing is off, the <c>paid-plans</c> flag is off for the
    /// caller, or Stripe can't be reached, so nothing offers an upgrade then.
    /// </summary>
    private static async Task<Ok<List<PlanOffer>>> GetPlans(
        AuthenticatedUser user,
        FeatureFlagService featureFlags,
        HttpContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, StripePlanPrice> prices = new Dictionary<string, StripePlanPrice>();
        if (context.RequestServices.GetService<BillingService>() is { } billing &&
            await featureFlags.IsEnabledAsync(FeatureFlagKeys.PaidPlans, user, cancellationToken))
        {
            try
            {
                prices = await billing.GetPrices(cancellationToken);
            }
            catch (Stripe.StripeException error)
            {
                loggerFactory.CreateLogger("Reelshelf.Plans.PlanEndpoints")
                    .LogError(error, "Could not read the plans' prices from Stripe");
            }
        }

        List<PlanOffer> offers = [];
        foreach (AccountTier tier in new[] { AccountTier.Free, AccountTier.Plus, AccountTier.Pro })
        {
            TierLimits limits = TierLimits.For(tier);
            List<PlanOfferPrice> tierPrices = BillingPlans.All
                .Where(plan => plan.Tier == tier && prices.ContainsKey(plan.LookupKey))
                .Select(plan => new PlanOfferPrice(plan.LookupKey, plan.Interval, prices[plan.LookupKey].UnitAmount,
                    prices[plan.LookupKey].Currency))
                .ToList();
            offers.Add(new PlanOffer(BillingStatements.TierName(tier), limits.MaxClips, limits.MaxClipSeconds,
                limits.MaxNewClipsPer30Days, TierLimits.HasTranscripts(tier), tierPrices));
        }

        return TypedResults.Ok(offers);
    }
}

/// <summary>A tier someone can be on, with its limits and, for a paid one, its prices.</summary>
public sealed record PlanOffer(
    string Tier,
    int? ClipLimit,
    int? MaxClipSeconds,
    int? NewClipLimit,
    bool HasTranscripts,
    List<PlanOfferPrice> Prices);

/// <summary>A price to start Checkout with, by its lookup key; the amount is in the currency's minor unit.</summary>
public sealed record PlanOfferPrice(string LookupKey, string Interval, long UnitAmount, string Currency);
