import { Link } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import {
  useOpenBillingPortal,
  usePlan,
  usePlanOffers,
  useStartCheckout,
} from "@/hooks/queries";
import { PlanMeter } from "@/components/Reelshelf/PlanMeter";
import {
  type AccountPlan,
  type PlanOffer,
  formatClipLength,
  formatPrice,
  tierNames,
} from "@/shared/services/plans";
import { ApiError } from "@/shared/services/apiError";

export type BillingReturn = "success" | "cancelled" | undefined;

/** Statuses of a subscription that could still bill, so changes go through the Customer Portal instead of Checkout. */
const openStatuses = new Set([
  "active",
  "trialing",
  "past_due",
  "unpaid",
  "paused",
]);

/** Statuses where a payment failed, so the way back is updating the card in the Customer Portal. */
const paymentProblemStatuses = new Set(["past_due", "unpaid", "paused"]);

/** After Checkout, Stripe's webhook updates the plan within seconds; poll for that long. */
const pollAfterCheckoutMs = 30_000;

export function PlanSettings({
  billingReturn,
}: {
  billingReturn: BillingReturn;
}) {
  const [polling, setPolling] = useState(billingReturn === "success");
  const plan = usePlan();
  const { refetch } = plan;
  const offers = usePlanOffers();
  const portal = useOpenBillingPortal();

  // Until the webhook lands the account still reads as before, so refetch every couple of seconds for a while.
  const upgraded = plan.data
    ? plan.data.tier !== "free" || plan.data.subscription !== null
    : false;
  useEffect(() => {
    if (!polling || upgraded) return;
    const interval = window.setInterval(() => void refetch(), 2_000);
    const timeout = window.setTimeout(
      () => setPolling(false),
      pollAfterCheckoutMs,
    );
    return () => {
      window.clearInterval(interval);
      window.clearTimeout(timeout);
    };
  }, [polling, upgraded, refetch]);

  if (!plan.data) {
    return plan.isError ? (
      <p className="rs-linked-notice" data-tone="error">
        Your plan couldn't be loaded. Refresh to try again.
      </p>
    ) : null;
  }

  const current = plan.data;
  const subscription = current.subscription;
  const hasOpenSubscription =
    subscription !== null && openStatuses.has(subscription.status);
  const paymentProblem =
    subscription !== null && paymentProblemStatuses.has(subscription.status);
  const canUpgrade =
    current.billingEnabled &&
    current.tier !== "unlimited" &&
    !hasOpenSubscription;

  return (
    <div className="rs-plan">
      {billingReturn === "success" ? (
        <p className="rs-linked-notice" data-tone="success" role="status">
          {upgraded
            ? `Thanks! You're on ${tierNames[current.tier] ?? current.tier} now.`
            : "Thanks! Your plan updates as soon as Stripe confirms the payment."}
        </p>
      ) : null}
      {billingReturn === "cancelled" ? (
        <p className="rs-linked-notice" role="status">
          Checkout was cancelled; nothing was charged.
        </p>
      ) : null}

      <PlanMeter plan={current} />
      {current.tier !== "unlimited" ? (
        <p className="rs-plan-limits">
          {current.maxClipSeconds !== null
            ? `Clips up to ${formatClipLength(current.maxClipSeconds)}`
            : "Clips of any length"}
        </p>
      ) : null}
      <SubscriptionStatus plan={current} />
      <OverLimitNotice plan={current} />

      {current.canManageBilling ? (
        <div className="rs-email-actions">
          <button
            className={paymentProblem ? "rs-primary" : "rs-secondary"}
            type="button"
            disabled={portal.isPending}
            onClick={() => portal.mutate()}
          >
            {paymentProblem
              ? "Update payment method"
              : hasOpenSubscription
                ? "Manage billing"
                : "Billing history"}
          </button>
        </div>
      ) : null}
      {portal.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          Billing couldn't be opened. Try again.
        </p>
      ) : null}

      {canUpgrade && offers.data ? (
        <PlanOffers offers={offers.data} currentTier={current.tier} />
      ) : null}
    </div>
  );
}

/** Clips over the limit, and when they go; the plan page lists them, picks which to keep and downloads them. */
function OverLimitNotice({ plan }: { plan: AccountPlan }) {
  if (plan.clipsOverLimit <= 0) return null;
  const clips =
    plan.clipsOverLimit === 1
      ? "1 clip is"
      : `${plan.clipsOverLimit.toLocaleString()} clips are`;
  const deletesOn = formatDate(plan.overLimitDeletesAt);
  return (
    <p className="rs-linked-notice" data-tone="error">
      {clips} over your plan's limit
      {deletesOn ? ` and will be deleted on ${deletesOn}` : ""}.{" "}
      <Link to="/plan">Download them or pick which to keep</Link>.
    </p>
  );
}

function formatDate(value: Date | null | undefined): string | null {
  if (!value) return null;
  return new Date(value).toLocaleDateString(undefined, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

function SubscriptionStatus({ plan }: { plan: AccountPlan }) {
  const subscription = plan.subscription;
  if (plan.tier === "unlimited") {
    return (
      <p className="rs-sidebar-copy">
        Your account keeps unlimited clips of any length.
      </p>
    );
  }
  if (!subscription) return null;

  const tier = subscription.tier
    ? (tierNames[subscription.tier] ?? subscription.tier)
    : "your plan";
  const interval = subscription.interval === "year" ? "yearly" : "monthly";
  const periodEnd = formatDate(subscription.currentPeriodEnd);
  const endsOn =
    formatDate(subscription.cancelAt) ??
    (subscription.cancelAtPeriodEnd ? periodEnd : null);

  switch (subscription.status) {
    case "active":
    case "trialing":
      return (
        <p className="rs-sidebar-copy">
          {endsOn
            ? `${tier} (${interval}) ends on ${endsOn}. You keep it until then.`
            : `${tier} (${interval})${periodEnd ? ` renews on ${periodEnd}` : ""}.`}
        </p>
      );
    case "past_due":
      return (
        <p className="rs-linked-notice" data-tone="error">
          Your last payment for {tier} didn't go through. Update your payment
          method to keep it.
        </p>
      );
    case "unpaid":
    case "paused":
      return (
        <p className="rs-linked-notice" data-tone="error">
          {tier} is on hold because a payment failed, so your account has Free's
          limits for now. Update your payment method to get {tier} back.
        </p>
      );
    default: {
      const ended = formatDate(subscription.endedAt);
      return ended ? (
        <p className="rs-sidebar-copy">
          {tier} ended on {ended}.
        </p>
      ) : null;
    }
  }
}

function PlanOffers({
  offers,
  currentTier,
}: {
  offers: PlanOffer[];
  currentTier: string;
}) {
  const [interval, setBillingInterval] = useState<"month" | "year">("year");
  const checkout = useStartCheckout();
  const paid = offers.filter((offer) => offer.prices.length > 0);
  if (paid.length === 0) return null;
  // Free is priced in the same currency as the paid plans beside it.
  const currency = paid[0].prices[0].currency;

  const error =
    checkout.error instanceof ApiError
      ? checkout.error.message
      : checkout.error
        ? "Checkout couldn't be started. Try again."
        : null;

  return (
    <div className="rs-plan-offers">
      <div
        className="rs-plan-interval"
        role="group"
        aria-label="Billing period"
      >
        <button
          type="button"
          className={`rs-chip${interval === "year" ? " active" : ""}`}
          aria-pressed={interval === "year"}
          onClick={() => setBillingInterval("year")}
        >
          Yearly
        </button>
        <button
          type="button"
          className={`rs-chip${interval === "month" ? " active" : ""}`}
          aria-pressed={interval === "month"}
          onClick={() => setBillingInterval("month")}
        >
          Monthly
        </button>
      </div>
      <div className="rs-plan-cards">
        {offers.map((offer) => {
          const price = offer.prices.find((p) => p.interval === interval);
          const isCurrent = offer.tier === currentTier;
          return (
            <div
              key={offer.tier}
              className="rs-plan-card"
              data-current={isCurrent || undefined}
            >
              <div className="rs-plan-card-name">
                {tierNames[offer.tier] ?? offer.tier}
              </div>
              <div className="rs-plan-card-price">
                {offer.tier === "free"
                  ? `${formatPrice({ lookupKey: "", interval, unitAmount: 0, currency })} / ${interval}`
                  : price
                    ? `${formatPrice(price)} / ${interval}`
                    : "Not available"}
              </div>
              <div className="rs-plan-card-clips">
                {offer.clipLimit !== null
                  ? `${offer.clipLimit.toLocaleString()} clips`
                  : "Unlimited clips"}
              </div>
              <ul className="rs-plan-card-features">
                <li>
                  {offer.maxClipSeconds !== null
                    ? `Clips up to ${formatClipLength(offer.maxClipSeconds)}`
                    : "Clips of any length"}
                </li>
                <li>Apex legend detection</li>
              </ul>
              {isCurrent ? (
                <span className="rs-plan-card-current">Current plan</span>
              ) : price ? (
                <button
                  className="rs-primary"
                  type="button"
                  disabled={checkout.isPending}
                  onClick={() => checkout.mutate(price.lookupKey)}
                >
                  Upgrade to {tierNames[offer.tier] ?? offer.tier}
                </button>
              ) : null}
            </div>
          );
        })}
      </div>
      <p className="rs-plan-footnote">
        Checkout charges in your own currency where it can. Subscriptions are
        sold by Stripe through Link and show on your statement as LINK.COM*
        REELSHELF.
      </p>
      {error ? (
        <p className="rs-linked-notice" data-tone="error">
          {error}
        </p>
      ) : null}
    </div>
  );
}
