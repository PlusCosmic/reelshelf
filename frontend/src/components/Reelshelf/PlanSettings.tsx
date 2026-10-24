import { Link } from "@tanstack/react-router";
import { type ReactNode, useEffect, useState } from "react";
import {
  useOpenBillingPortal,
  usePlan,
  usePlanOffers,
  useStartCheckout,
} from "@/hooks/queries";
import { PlanMeter, nearLimitFraction } from "@/components/Reelshelf/PlanMeter";
import {
  type AccountPlan,
  type PlanOffer,
  type PlanOfferPrice,
  formatClipLength,
  formatPrice,
  tierNames,
} from "@/shared/services/plans";
import { ApiError } from "@/shared/services/apiError";

/** Where the browser came back from: Checkout (`success`, `cancelled`) or the Customer Portal (`portal`). */
export type BillingReturn = "success" | "cancelled" | "portal" | undefined;

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

/** Statuses whose subscription is the account's current plan. */
const currentStatuses = new Set(["active", "trialing", "past_due"]);

/** After Checkout or the portal, Stripe's webhook updates the plan within seconds; poll for that long. */
const pollAfterReturnMs = 30_000;

type Tone = "info" | "success" | "warning" | "error";

/** The one thing the account most needs to know, if anything, with the actions that deal with it. */
type Alert = {
  tone: Tone;
  title: string;
  body?: ReactNode;
  /** The billing portal button, when it's the way out; the plain billing button below is then left out. */
  portal?: string;
  /** The plan page, to download or pick clips to keep. */
  planPage?: boolean;
};

export function PlanSettings({
  billingReturn,
}: {
  billingReturn: BillingReturn;
}) {
  const [polling, setPolling] = useState(
    billingReturn === "success" || billingReturn === "portal",
  );
  const plan = usePlan();
  const { refetch } = plan;
  const offers = usePlanOffers();
  const portal = useOpenBillingPortal();

  // Until the webhook lands the account still reads as before, so refetch every couple of seconds for a while.
  const upgraded = plan.data
    ? plan.data.tier !== "free" || plan.data.subscription !== null
    : false;
  const waitingForCheckout = billingReturn === "success" && !upgraded;
  useEffect(() => {
    if (!polling || (billingReturn === "success" && upgraded)) return;
    const interval = window.setInterval(() => void refetch(), 2_000);
    const timeout = window.setTimeout(
      () => setPolling(false),
      pollAfterReturnMs,
    );
    return () => {
      window.clearInterval(interval);
      window.clearTimeout(timeout);
    };
  }, [polling, billingReturn, upgraded, refetch]);

  if (!plan.data) {
    return plan.isError ? (
      <p className="rs-linked-notice" data-tone="error">
        Your plan couldn't be loaded. Refresh to try again.
      </p>
    ) : null;
  }

  const current = plan.data;
  const tier = tierNames[current.tier] ?? current.tier;
  const subscription = current.subscription;
  const hasOpenSubscription =
    subscription !== null && openStatuses.has(subscription.status);
  const canUpgrade =
    current.billingEnabled &&
    current.tier !== "unlimited" &&
    !hasOpenSubscription;
  const alert = planAlert(current, canUpgrade);
  // A plan that ended can be taken up again; its card says so.
  const lapsedTier =
    subscription !== null && !hasOpenSubscription ? subscription.tier : null;

  const openPortal = () => portal.mutate();

  return (
    <div className="rs-plan">
      <ReturnBanner
        billingReturn={billingReturn}
        upgradedTo={upgraded ? tier : null}
        stillWaiting={waitingForCheckout && !polling}
      />

      <div className="rs-plan-summary">
        <h3 className="rs-plan-name">{planTitle(current)}</h3>
        <PlanMeter plan={current} showTier={false} />
        {current.tier !== "unlimited" ? (
          <p className="rs-plan-limits">
            {current.maxClipSeconds !== null
              ? `Clips up to ${formatClipLength(current.maxClipSeconds)} long`
              : "Clips of any length"}
          </p>
        ) : null}
        <SubscriptionLine plan={current} />
      </div>

      {alert ? (
        <PlanAlert tone={alert.tone} title={alert.title}>
          {alert.body ? <p>{alert.body}</p> : null}
          {alert.portal || alert.planPage ? (
            <div className="rs-plan-actions">
              {alert.portal ? (
                <button
                  className="rs-primary"
                  type="button"
                  disabled={portal.isPending}
                  onClick={openPortal}
                >
                  {alert.portal}
                </button>
              ) : null}
              {alert.planPage ? (
                <Link to="/plan" className="rs-secondary rs-plan-link-button">
                  Choose clips to keep
                </Link>
              ) : null}
              {alert.portal ? <PortalHint /> : null}
            </div>
          ) : null}
        </PlanAlert>
      ) : null}

      {current.canManageBilling && !alert?.portal ? (
        <div className="rs-plan-actions">
          <button
            className="rs-secondary"
            type="button"
            disabled={portal.isPending}
            onClick={openPortal}
          >
            {hasOpenSubscription ? "Manage billing" : "Billing history"}
          </button>
          <PortalHint />
        </div>
      ) : null}
      {portal.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          Billing couldn't be opened. Try again.
        </p>
      ) : null}

      {/* Hidden until a payment just made shows up, so it can't be made twice. */}
      {canUpgrade && offers.data && !waitingForCheckout ? (
        <PlanOffers
          offers={offers.data}
          currentTier={current.tier}
          lapsedTier={lapsedTier}
        />
      ) : null}
    </div>
  );
}

function PortalHint() {
  return <span className="rs-plan-hint">Opens Stripe's billing portal</span>;
}

/** "Plus, monthly" for a paid plan, else the tier. */
function planTitle(plan: AccountPlan): string {
  const tier = tierNames[plan.tier] ?? plan.tier;
  const subscription = plan.subscription;
  if (
    subscription !== null &&
    subscription.tier === plan.tier &&
    currentStatuses.has(subscription.status) &&
    subscription.interval
  ) {
    return `${tier}, ${subscription.interval === "year" ? "yearly" : "monthly"}`;
  }
  return tier;
}

/** When the plan renews or ends; payment problems are the alert's. */
function SubscriptionLine({ plan }: { plan: AccountPlan }) {
  if (plan.tier === "unlimited") {
    return <p className="rs-sidebar-copy">Unlimited clips of any length.</p>;
  }
  const subscription = plan.subscription;
  if (!subscription) return null;

  const tier = subscriptionTierName(plan);
  const periodEnd = formatDate(subscription.currentPeriodEnd);
  const endsOn = formatDate(planEnd(plan));
  switch (subscription.status) {
    case "active":
    case "trialing":
      return (
        <p className="rs-sidebar-copy">
          {endsOn
            ? `Ends on ${endsOn}. You keep ${tier} until then.`
            : periodEnd
              ? `Renews on ${periodEnd}.`
              : null}
        </p>
      );
    case "past_due":
    case "unpaid":
    case "paused":
      return null;
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

function subscriptionTierName(plan: AccountPlan): string {
  const tier = plan.subscription?.tier;
  return tier ? (tierNames[tier] ?? tier) : "your plan";
}

/** When an active plan set to end will end, if it is. */
function planEnd(plan: AccountPlan): Date | null {
  const subscription = plan.subscription;
  if (!subscription) return null;
  return (
    subscription.cancelAt ??
    (subscription.cancelAtPeriodEnd ? subscription.currentPeriodEnd : null)
  );
}

/**
 * The most pressing of: a failed payment, clips over the limit, a plan ending with more clips than Free keeps, a
 * nearly full library. Clips being deleted is an error; anything that can still be headed off is a warning.
 */
function planAlert(plan: AccountPlan, canUpgrade: boolean): Alert | null {
  const subscription = plan.subscription;
  const over = plan.clipsOverLimit;
  const deletesOn = formatDate(plan.overLimitDeletesAt);
  const limit = plan.clipLimit;
  const freeLimit = plan.freeClipLimit ?? null;
  const graceDays = plan.overLimitGraceDays ?? null;
  const tier = subscriptionTierName(plan);

  const overTitle = deletesOn
    ? `${clipCount(over)} will be deleted on ${deletesOn}.`
    : `${clipCount(over)} ${over === 1 ? "is" : "are"} over your plan's limit.`;

  if (subscription && paymentProblemStatuses.has(subscription.status)) {
    const pastDue = subscription.status === "past_due";
    if (over > 0) {
      return {
        tone: deletesOn ? "error" : "warning",
        title: overTitle,
        body: `${
          pastDue
            ? `Your last payment for ${tier} didn't go through.`
            : `${tier} is on hold because a payment failed, so your account has Free's limits.`
        } Update your payment method to keep them, or choose which clips to keep.`,
        portal: "Update payment method",
        planPage: true,
      };
    }
    if (pastDue) {
      // What the last retry costs: the plan, and the clips over Free's limit with it.
      const overFree =
        freeLimit !== null ? Math.max(0, plan.clipCount - freeLimit) : 0;
      const clipsCost =
        overFree === 0
          ? ""
          : graceDays !== null
            ? `, and the ${clipCount(overFree)} over them will be deleted ${graceDays} days later`
            : `, and you can't add clips while ${clipCount(overFree)} are over them`;
      return {
        tone: "warning",
        title: `Your last payment for ${tier} didn't go through.`,
        body: `We'll try it again. Update your payment method to keep ${tier}: if the last try fails, ${tier} ends and your account moves to Free's limits${clipsCost}.`,
        portal: "Update payment method",
      };
    }
    return {
      tone: "warning",
      title: `${tier} is on hold because a payment failed.`,
      body: "Your account has Free's limits until you update your payment method.",
      portal: "Update payment method",
    };
  }

  if (over > 0 && limit !== null) {
    return {
      tone: deletesOn ? "error" : "warning",
      title: overTitle,
      body: deletesOn
        ? `Your plan keeps ${limit.toLocaleString()}. Choose which clips to keep and download the rest${
            canUpgrade ? ", or pick a plan below to keep them all" : ""
          }.`
        : `Your plan keeps ${limit.toLocaleString()}, so you can't add clips until you're back within it.`,
      planPage: true,
    };
  }

  const end = planEnd(plan);
  if (end && freeLimit !== null && plan.clipCount > freeLimit) {
    const overFree = plan.clipCount - freeLimit;
    const deletes =
      graceDays !== null
        ? formatDate(new Date(end.getTime() + graceDays * 86_400_000))
        : null;
    return {
      tone: "warning",
      title: `When ${tier} ends on ${formatDate(end)}, ${clipCount(overFree)} will be over Free's limit of ${freeLimit.toLocaleString()}.`,
      body: deletes
        ? `They'll be deleted ${graceDays} days later, on ${deletes}, unless you keep ${tier}.`
        : `You won't be able to add clips until you're back within it, unless you keep ${tier}.`,
      portal: `Keep ${tier}`,
    };
  }

  if (limit !== null && plan.clipCount >= limit * nearLimitFraction) {
    const full = plan.clipCount >= limit;
    const way = canUpgrade ? "delete some or upgrade" : "delete some";
    return {
      tone: "warning",
      title: full ? "Your library is full." : "Your library is nearly full.",
      body: full
        ? `You can't add clips until you ${way}.`
        : `You've used ${plan.clipCount.toLocaleString()} of ${limit.toLocaleString()} clips. At ${limit.toLocaleString()} you can't add more until you ${way}.`,
    };
  }

  return null;
}

/** Coming back from Checkout: what happened, and whether the payment has shown up yet. */
function ReturnBanner({
  billingReturn,
  upgradedTo,
  stillWaiting,
}: {
  billingReturn: BillingReturn;
  upgradedTo: string | null;
  stillWaiting: boolean;
}) {
  if (billingReturn === "cancelled") {
    return (
      <PlanAlert tone="info" title="Checkout was cancelled.">
        <p>Nothing was charged.</p>
      </PlanAlert>
    );
  }
  if (billingReturn !== "success") return null;
  if (upgradedTo) {
    return (
      <PlanAlert tone="success" title={`You're on ${upgradedTo} now.`}>
        <p>Thanks for subscribing.</p>
      </PlanAlert>
    );
  }
  if (stillWaiting) {
    return (
      <PlanAlert
        tone="warning"
        title="Your payment is taking longer than usual to confirm."
      >
        <p>
          Refresh in a minute. If your plan still hasn't changed, email us
          before trying again.
        </p>
      </PlanAlert>
    );
  }
  return (
    <PlanAlert tone="info" title="Confirming your payment…" busy>
      <p>This usually takes a few seconds.</p>
    </PlanAlert>
  );
}

function PlanAlert({
  tone,
  title,
  busy = false,
  children,
}: {
  tone: Tone;
  title: string;
  busy?: boolean;
  children?: ReactNode;
}) {
  return (
    <div
      className="rs-plan-alert"
      data-tone={tone}
      role={tone === "error" ? "alert" : "status"}
    >
      {busy ? (
        <span className="rs-plan-alert-spinner" aria-hidden="true" />
      ) : (
        <AlertIcon tone={tone} />
      )}
      <div className="rs-plan-alert-body">
        <p className="rs-plan-alert-title">{title}</p>
        {children}
      </div>
    </div>
  );
}

function AlertIcon({ tone }: { tone: Tone }) {
  return (
    <svg
      className="rs-plan-alert-icon"
      viewBox="0 0 20 20"
      width="18"
      height="18"
      aria-hidden="true"
    >
      <circle cx="10" cy="10" r="8.25" fill="none" stroke="currentColor" />
      {tone === "success" ? (
        <path
          d="M6.5 10.2l2.4 2.4 4.6-5"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.6"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      ) : tone === "info" ? (
        <>
          <circle cx="10" cy="6.4" r="1.05" fill="currentColor" />
          <path
            d="M10 9v5"
            stroke="currentColor"
            strokeWidth="1.7"
            strokeLinecap="round"
          />
        </>
      ) : (
        <>
          <path
            d="M10 5.6v5.2"
            stroke="currentColor"
            strokeWidth="1.7"
            strokeLinecap="round"
          />
          <circle cx="10" cy="13.9" r="1.05" fill="currentColor" />
        </>
      )}
    </svg>
  );
}

function clipCount(count: number): string {
  return count === 1 ? "1 clip" : `${count.toLocaleString()} clips`;
}

function formatDate(value: Date | null | undefined): string | null {
  if (!value) return null;
  return new Date(value).toLocaleDateString(undefined, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

/** What paying yearly comes to a month, and how many months it saves against paying monthly. */
function yearlyNote(
  yearly: PlanOfferPrice,
  monthly: PlanOfferPrice | undefined,
): string {
  const perMonth = formatPrice({
    ...yearly,
    unitAmount: Math.round(yearly.unitAmount / 12),
  });
  const monthsFree =
    monthly && monthly.currency === yearly.currency && monthly.unitAmount > 0
      ? Math.round(
          (monthly.unitAmount * 12 - yearly.unitAmount) / monthly.unitAmount,
        )
      : 0;
  return monthsFree > 0
    ? `${perMonth}/mo · ${monthsFree === 1 ? "1 month" : `${monthsFree} months`} free`
    : `${perMonth}/mo`;
}

function PlanOffers({
  offers,
  currentTier,
  lapsedTier,
}: {
  offers: PlanOffer[];
  currentTier: string;
  lapsedTier: string | null;
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
        {(["year", "month"] as const).map((value) => (
          <button
            key={value}
            type="button"
            className="rs-plan-segment"
            aria-pressed={interval === value}
            onClick={() => setBillingInterval(value)}
          >
            {value === "year" ? "Yearly" : "Monthly"}
          </button>
        ))}
      </div>
      <div className="rs-plan-cards">
        {offers.map((offer) => {
          const name = tierNames[offer.tier] ?? offer.tier;
          const price = offer.prices.find((p) => p.interval === interval);
          const isCurrent = offer.tier === currentTier;
          const note =
            offer.tier === "free"
              ? "No card needed"
              : price && interval === "year"
                ? yearlyNote(
                    price,
                    offer.prices.find((p) => p.interval === "month"),
                  )
                : price
                  ? "Billed monthly"
                  : "";
          return (
            <div
              key={offer.tier}
              className="rs-plan-card"
              data-current={isCurrent || undefined}
            >
              <div className="rs-plan-card-name">{name}</div>
              <div className="rs-plan-card-price">
                {offer.tier === "free"
                  ? formatPrice({
                      lookupKey: "",
                      interval,
                      unitAmount: 0,
                      currency,
                    })
                  : price
                    ? `${formatPrice(price)} / ${interval}`
                    : "Not available"}
              </div>
              <div className="rs-plan-card-note">{note || " "}</div>
              <div className="rs-plan-card-clips">
                {offer.clipLimit !== null
                  ? `${offer.clipLimit.toLocaleString()} clips`
                  : "Unlimited clips"}
              </div>
              <div className="rs-plan-card-length">
                {offer.maxClipSeconds !== null
                  ? `Up to ${formatClipLength(offer.maxClipSeconds)} each`
                  : "Any length"}
              </div>
              <div className="rs-plan-card-action">
                {isCurrent ? (
                  <span className="rs-plan-card-current">Current plan</span>
                ) : price ? (
                  <button
                    className="rs-primary"
                    type="button"
                    disabled={checkout.isPending}
                    onClick={() => checkout.mutate(price.lookupKey)}
                  >
                    {offer.tier === lapsedTier
                      ? "Resubscribe to"
                      : "Upgrade to"}{" "}
                    {name}
                  </button>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
      <p className="rs-plan-included">
        Every plan includes legend detection for Apex Legends clips.
      </p>
      <p className="rs-plan-footnote">
        Prices are in {currency.toUpperCase()}; Checkout shows your own currency
        where it can. Plans are sold by Stripe through Link, so charges show as
        LINK.COM* REELSHELF.
      </p>
      {error ? (
        <p className="rs-linked-notice" data-tone="error">
          {error}
        </p>
      ) : null}
    </div>
  );
}
