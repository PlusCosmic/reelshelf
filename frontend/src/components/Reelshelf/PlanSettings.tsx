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
      <p className="rs-plan-limits">{describeLimits(current)}</p>
      <SubscriptionStatus plan={current} />

      {current.canManageBilling ? (
        <div className="rs-email-actions">
          <button
            className="rs-secondary"
            type="button"
            disabled={portal.isPending}
            onClick={() => portal.mutate()}
          >
            {hasOpenSubscription ? "Manage billing" : "Billing history"}
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

function describeLimits(plan: AccountPlan): string {
  const parts: string[] = [];
  parts.push(
    plan.maxClipSeconds !== null
      ? `Clips up to ${formatClipLength(plan.maxClipSeconds)}`
      : "Clips of any length",
  );
  if (plan.newClipLimit !== null) {
    parts.push(
      `${plan.newClipsLast30Days.toLocaleString()} of ${plan.newClipLimit.toLocaleString()} new clips in the last 30 days`,
    );
  }
  return parts.join(" · ");
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
          Your last payment for {tier} didn't go through. Update your card in
          Manage billing to keep it.
        </p>
      );
    case "unpaid":
    case "paused":
      return (
        <p className="rs-linked-notice" data-tone="error">
          {tier} is on hold because payment failed. Update your card in Manage
          billing to get it back.
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
                  ? "Free"
                  : price
                    ? `${formatPrice(price)} / ${interval}`
                    : "Not available"}
              </div>
              <ul className="rs-plan-card-features">
                <li>
                  {offer.clipLimit !== null
                    ? `Keep up to ${offer.clipLimit.toLocaleString()} clips`
                    : "Unlimited clips"}
                </li>
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
