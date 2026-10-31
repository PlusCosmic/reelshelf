using Microsoft.AspNetCore.Http.HttpResults;
using Reelshelf.Auth;
using Reelshelf.FeatureFlags;
using Stripe;

namespace Reelshelf.Billing;

public static class BillingEndpoints
{
    /// <summary>How old a webhook's signature may be, as Stripe's libraries default to.</summary>
    private const long WebhookToleranceSeconds = 300;

    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("billing");

        // Buying and changing a plan are rolled out behind a flag; the portal and dropping a waiting downgrade aren't,
        // so a subscriber the flag is off for can still manage, cancel or keep what they have.
        group.MapPost("/checkout", StartCheckout)
            .WithName("StartCheckout")
            .RequireAuthorization()
            .RequireFeature(FeatureFlagKeys.PaidPlans)
            .RequireRateLimiting(RateLimitPolicies.Billing);
        group.MapPost("/portal", OpenPortal)
            .WithName("OpenBillingPortal")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Billing);
        group.MapPost("/plan", ChangePlan)
            .WithName("ChangePlan")
            .RequireAuthorization()
            .RequireFeature(FeatureFlagKeys.PaidPlans)
            .RequireRateLimiting(RateLimitPolicies.Billing);
        group.MapDelete("/plan/scheduled", KeepPlan)
            .WithName("KeepPlan")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Billing);

        // Stripe signs each delivery; the signature is the only authentication, so the endpoint stays out of the
        // OpenAPI document and the generated client.
        group.MapPost("/webhook", ReceiveWebhook)
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Starts a Stripe Checkout for a paid plan, by its price's lookup key, and returns the page to send the user
    /// to. 409 when the account already has a subscription, which is changed in the Customer Portal instead.
    /// </summary>
    private static async Task<Results<Ok<BillingRedirect>, NotFound, BadRequest<string>, Conflict<string>, ProblemHttpResult>> StartCheckout(
        StartCheckoutRequest request,
        AuthenticatedUser user,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<BillingService>() is not { } billing)
        {
            return TypedResults.NotFound();
        }

        CheckoutResult result = await billing.StartCheckout(user, request.LookupKey, cancellationToken);
        return result.Outcome switch
        {
            CheckoutOutcome.Created => TypedResults.Ok(new BillingRedirect(result.Url!)),
            CheckoutOutcome.UnknownPlan => TypedResults.BadRequest("That plan doesn't exist."),
            CheckoutOutcome.AlreadySubscribed => TypedResults.Conflict(
                "You already have a subscription. Change it from Manage billing."),
            _ => TypedResults.Problem("That plan isn't available right now. Try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    /// <summary>Opens Stripe's Customer Portal. 404 when billing is off or the account has never checked out.</summary>
    private static async Task<Results<Ok<BillingRedirect>, NotFound>> OpenPortal(
        AuthenticatedUser user,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<BillingService>() is not { } billing ||
            await billing.OpenPortal(user, cancellationToken) is not { } url)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new BillingRedirect(url));
    }

    /// <summary>
    /// Changes a subscriber's plan, by its price's lookup key. An upgrade answers Stripe's page confirming it (and
    /// charging the prorated difference); a downgrade is scheduled for the end of the paid period and answers when.
    /// 404 when billing is off or the account has no subscription, 409 while a payment is failing or the plan is set
    /// to end, 503 when changing plan isn't set up.
    /// </summary>
    private static async Task<Results<Ok<PlanChangeResponse>, NotFound, BadRequest<string>, Conflict<string>, ProblemHttpResult>> ChangePlan(
        StartCheckoutRequest request,
        AuthenticatedUser user,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<BillingService>() is not { } billing)
        {
            return TypedResults.NotFound();
        }

        PlanChangeResult result = await billing.ChangePlan(user, request.LookupKey, cancellationToken);
        return result.Outcome switch
        {
            PlanChangeOutcome.Confirm => TypedResults.Ok(new PlanChangeResponse(result.Url, null)),
            PlanChangeOutcome.Scheduled => TypedResults.Ok(new PlanChangeResponse(null, result.At)),
            PlanChangeOutcome.SamePlan => TypedResults.Ok(new PlanChangeResponse(null, null)),
            PlanChangeOutcome.UnknownPlan => TypedResults.BadRequest("That plan doesn't exist."),
            PlanChangeOutcome.NotSubscribed => TypedResults.NotFound(),
            PlanChangeOutcome.NotChangeable => TypedResults.Conflict(
                "Your plan can't be changed while a payment is failing or it's set to end. Sort that out in Manage billing first."),
            _ => TypedResults.Problem("Changing plan isn't available right now. Try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    /// <summary>Drops a downgrade waiting for the end of the paid period. 404 when there is none.</summary>
    private static async Task<Results<NoContent, NotFound>> KeepPlan(
        AuthenticatedUser user,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<BillingService>() is not { } billing ||
            !await billing.KeepPlan(user, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok, NotFound, BadRequest>> ReceiveWebhook(
        HttpContext context,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<BillingService>() is not { } billing)
        {
            return TypedResults.NotFound();
        }

        BillingOptions options = context.RequestServices.GetRequiredService<BillingOptions>();
        ILogger logger = loggerFactory.CreateLogger("Reelshelf.Billing.BillingEndpoints");

        string payload;
        using (StreamReader reader = new(context.Request.Body))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        try
        {
            EventUtility.ValidateSignature(payload, context.Request.Headers["Stripe-Signature"].ToString(),
                options.WebhookSecret, WebhookToleranceSeconds);
        }
        catch (StripeException)
        {
            logger.LogWarning("Rejected a Stripe webhook with a missing or invalid signature from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            return TypedResults.BadRequest();
        }

        BillingWebhookEvent stripeEvent = BillingWebhookEvent.Parse(payload);
        // A failure answers 500, so Stripe delivers the event again later.
        await billing.HandleWebhook(stripeEvent, cancellationToken);
        return TypedResults.Ok();
    }
}

public sealed record StartCheckoutRequest(string LookupKey);

/// <summary>A Stripe-hosted page (Checkout or the Customer Portal) to send the browser to.</summary>
public sealed record BillingRedirect(string Url);

/// <summary>
/// A plan change: <see cref="Url"/> is Stripe's page confirming an upgrade; <see cref="ScheduledAt"/> is when a
/// downgrade takes effect. Both null when nothing changed.
/// </summary>
public sealed record PlanChangeResponse(string? Url, DateTimeOffset? ScheduledAt);
