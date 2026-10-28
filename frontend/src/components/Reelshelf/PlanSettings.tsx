import { Link } from "@tanstack/react-router";
import { type ReactNode, useEffect, useState } from "react";
import {
  useChangePlan,
  useKeepPlan,
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
  upgradeKeepsMoreClips,
} from "@/shared/services/plans";
import { ApiError } from "@/shared/services/apiError";
import { commonCopy } from "@/copy/common";
import { planCopy } from "@/copy/plan";

const copy = planCopy.settings;
const alerts = planCopy.alerts;
const returnCopy = planCopy.returnBanner;
const offerCopy = planCopy.offers;

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

/** The plan the cards point people to: Pro is the best value per clip. */
const recommendedTier = "pro";

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
  /** Dropping a downgrade waiting for the end of the period, done here rather than in the portal. */
  keep?: string;
};

/** Clips each tier keeps, from the plans list; null is no limit. */
type TierLimitsByName = Record<string, number | null | undefined>;

/** What a subscriber can change their plan from, when changing plan in Settings is on. */
type PlanChangeContext = {
  currentLookupKey: string;
  scheduledLookupKey: string | null;
  /** When a downgrade would take effect: the end of the paid period. */
  switchesOn: Date | null;
  clipCount: number;
  graceDays: number | null;
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
  const keep = useKeepPlan();

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
        {copy.loadFailed}
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
  // Subscribers change plan here too: upgrades are confirmed at Stripe, downgrades wait for the period's end.
  const changeable =
    current.billingEnabled &&
    current.canChangePlan &&
    subscription !== null &&
    (subscription.status === "active" || subscription.status === "trialing") &&
    planEnd(current) === null &&
    subscription.lookupKey !== null &&
    subscription.lookupKey !== undefined;
  const tierLimits: TierLimitsByName = Object.fromEntries(
    (offers.data ?? []).map((offer) => [offer.tier, offer.clipLimit]),
  );
  // Only offer an upgrade for room when a plan that can be bought keeps more clips than this one.
  const alert = planAlert(
    current,
    (canUpgrade || changeable) &&
      upgradeKeepsMoreClips(offers.data, current.clipLimit),
    tierLimits,
  );
  const scheduled = subscription?.scheduledTier ?? null;
  // A plan that ended can be taken up again; its card says so.
  const lapsedTier =
    subscription !== null && !hasOpenSubscription ? subscription.tier : null;

  const openPortal = () => portal.mutate();
  const keepPlan = () => keep.mutate();

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
              ? copy.clipsUpTo(formatClipLength(current.maxClipSeconds))
              : copy.anyLength}
          </p>
        ) : null}
        <SubscriptionLine plan={current} />
      </div>

      {alert ? (
        <PlanAlert tone={alert.tone} title={alert.title}>
          {alert.body ? <p>{alert.body}</p> : null}
          {alert.portal || alert.planPage || alert.keep ? (
            <div className="rs-plan-actions">
              {alert.keep ? (
                <button
                  className="rs-primary"
                  type="button"
                  disabled={keep.isPending}
                  onClick={keepPlan}
                >
                  {alert.keep}
                </button>
              ) : null}
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
                  {copy.chooseClipsToKeep}
                </Link>
              ) : null}
              {alert.portal ? <PortalHint /> : null}
            </div>
          ) : null}
        </PlanAlert>
      ) : null}

      {current.canManageBilling && !alert?.portal ? (
        <div className="rs-plan-actions">
          {scheduled && !alert?.keep ? (
            <button
              className="rs-secondary"
              type="button"
              disabled={keep.isPending}
              onClick={keepPlan}
            >
              {copy.keep(tier)}
            </button>
          ) : null}
          <button
            className="rs-secondary"
            type="button"
            disabled={portal.isPending}
            onClick={openPortal}
          >
            {hasOpenSubscription ? copy.manageBilling : copy.billingHistory}
          </button>
          <PortalHint />
        </div>
      ) : null}
      {portal.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          {copy.portalFailed}
        </p>
      ) : null}
      {keep.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          {copy.keepFailed}
        </p>
      ) : null}

      {/* Hidden until a payment just made shows up, so it can't be made twice. */}
      {(canUpgrade || changeable) && offers.data && !waitingForCheckout ? (
        <PlanOffers
          offers={offers.data}
          currentTier={current.tier}
          lapsedTier={lapsedTier}
          change={
            changeable && subscription?.lookupKey
              ? {
                  currentLookupKey: subscription.lookupKey,
                  scheduledLookupKey: subscription.scheduledTier
                    ? `${subscription.scheduledTier}_${subscription.scheduledInterval === "year" ? "yearly" : "monthly"}`
                    : null,
                  switchesOn: subscription.currentPeriodEnd ?? null,
                  clipCount: current.clipCount,
                  graceDays: current.overLimitGraceDays ?? null,
                }
              : null
          }
          tierLimits={tierLimits}
        />
      ) : null}
    </div>
  );
}

function PortalHint() {
  return <span className="rs-plan-hint">{copy.portalHint}</span>;
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
    return copy.title.subscribed(tier, subscription.interval);
  }
  // A tier an admin granted, when no subscription does as well (ADR-0008).
  if (plan.grantedTier === plan.tier) return copy.title.granted(tier);
  return tier;
}

/** When the plan renews or ends; payment problems are the alert's. */
function SubscriptionLine({ plan }: { plan: AccountPlan }) {
  if (plan.tier === "unlimited") {
    return <p className="rs-sidebar-copy">{planCopy.subscription.unlimited}</p>;
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
            ? planCopy.subscription.endsOn(endsOn, tier)
            : (scheduledLine(plan) ??
              (periodEnd ? planCopy.subscription.renewsOn(periodEnd) : null))}
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
          {planCopy.subscription.endedOn(tier, ended)}
        </p>
      ) : null;
    }
  }
}

/** "Switches to Plus (monthly) on 24 November 2026. You keep Pro until then." */
function scheduledLine(plan: AccountPlan): string | null {
  const subscription = plan.subscription;
  if (!subscription?.scheduledTier || !subscription.scheduledAt) return null;
  if (subscription.scheduledTier === subscription.tier) {
    return planCopy.subscription.switchesBilling(
      subscription.scheduledInterval,
      formatDate(subscription.scheduledAt),
    );
  }
  return planCopy.subscription.switchesTo(
    planName(subscription.scheduledTier, subscription.scheduledInterval),
    formatDate(subscription.scheduledAt),
    subscriptionTierName(plan),
  );
}

/** "Plus (monthly)". */
function planName(tier: string, interval: string | null | undefined): string {
  const name = tierNames[tier] ?? tier;
  return interval ? copy.planName(name, interval) : name;
}

function subscriptionTierName(plan: AccountPlan): string {
  const tier = plan.subscription?.tier;
  return tier ? (tierNames[tier] ?? tier) : copy.yourPlan;
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
function planAlert(
  plan: AccountPlan,
  canUpgrade: boolean,
  tierLimits: TierLimitsByName,
): Alert | null {
  const subscription = plan.subscription;
  const over = plan.clipsOverLimit;
  const deletesOn = formatDate(plan.overLimitDeletesAt);
  const limit = plan.clipLimit;
  const freeLimit = plan.freeClipLimit ?? null;
  const graceDays = plan.overLimitGraceDays ?? null;
  const tier = subscriptionTierName(plan);

  const overTitle = deletesOn
    ? alerts.overDeletesOn(over, deletesOn)
    : alerts.overLimit(over);

  if (subscription && paymentProblemStatuses.has(subscription.status)) {
    const pastDue = subscription.status === "past_due";
    if (over > 0) {
      return {
        tone: deletesOn ? "error" : "warning",
        title: overTitle,
        body: alerts.paymentProblemOver(pastDue, tier),
        portal: alerts.updatePaymentMethod,
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
            ? alerts.pastDue.deletedLater(overFree, graceDays)
            : alerts.pastDue.cantAdd(overFree);
      return {
        tone: "warning",
        title: alerts.pastDue.title(tier),
        body: alerts.pastDue.body(tier, clipsCost),
        portal: alerts.updatePaymentMethod,
      };
    }
    return {
      tone: "warning",
      title: alerts.onHold.title(tier),
      body: alerts.onHold.body,
      portal: alerts.updatePaymentMethod,
    };
  }

  if (over > 0 && limit !== null) {
    return {
      tone: deletesOn ? "error" : "warning",
      title: overTitle,
      body: deletesOn
        ? alerts.over.deletes(limit, canUpgrade)
        : alerts.over.blocked(limit),
      planPage: true,
    };
  }

  // A downgrade waiting for the end of the period, to a plan that keeps fewer clips than the account has.
  const scheduledTier = subscription?.scheduledTier;
  const scheduledLimit = scheduledTier ? tierLimits[scheduledTier] : null;
  if (
    subscription?.scheduledAt &&
    scheduledTier &&
    scheduledLimit !== null &&
    scheduledLimit !== undefined &&
    plan.clipCount > scheduledLimit
  ) {
    const at = subscription.scheduledAt;
    const overScheduled = plan.clipCount - scheduledLimit;
    const scheduledName = tierNames[scheduledTier] ?? scheduledTier;
    const deletes =
      graceDays !== null
        ? formatDate(new Date(at.getTime() + graceDays * 86_400_000))
        : null;
    return {
      tone: "warning",
      title: alerts.scheduled.title(
        scheduledName,
        formatDate(at),
        overScheduled,
        scheduledLimit,
      ),
      body: deletes
        ? alerts.scheduled.deletedLater(graceDays, deletes, tier)
        : alerts.scheduled.blocked(tier),
      keep: copy.keep(tier),
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
      title: alerts.ending.title(tier, formatDate(end), overFree, freeLimit),
      body: deletes
        ? alerts.ending.deletedLater(graceDays, deletes, tier)
        : alerts.ending.blocked(tier),
      portal: copy.keep(tier),
    };
  }

  if (limit !== null && plan.clipCount >= limit * nearLimitFraction) {
    const full = plan.clipCount >= limit;
    const way = canUpgrade
      ? alerts.full.deleteOrUpgrade
      : alerts.full.deleteSome;
    return {
      tone: "warning",
      title: full ? alerts.full.title : alerts.full.nearlyTitle,
      body: full
        ? alerts.full.body(way)
        : alerts.full.nearlyBody(plan.clipCount, limit, way),
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
      <PlanAlert tone="info" title={returnCopy.cancelled.title}>
        <p>{returnCopy.cancelled.body}</p>
      </PlanAlert>
    );
  }
  if (billingReturn !== "success") return null;
  if (upgradedTo) {
    return (
      <PlanAlert tone="success" title={returnCopy.upgraded.title(upgradedTo)}>
        <p>{returnCopy.upgraded.body}</p>
      </PlanAlert>
    );
  }
  if (stillWaiting) {
    return (
      <PlanAlert tone="warning" title={returnCopy.slow.title}>
        <p>{returnCopy.slow.body}</p>
      </PlanAlert>
    );
  }
  return (
    <PlanAlert tone="info" title={returnCopy.confirming.title} busy>
      <p>{returnCopy.confirming.body}</p>
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
    ? offerCopy.perMonthFree(perMonth, monthsFree)
    : offerCopy.perMonth(perMonth);
}

const tierRank: Record<string, number> = { free: 0, plus: 1, pro: 2 };

/** Charged now (a higher tier, or yearly instead of monthly) rather than waited for, as the API decides. */
function isUpgrade(
  from: { tier: string; interval: string },
  to: { tier: string; interval: string },
): boolean {
  const fromRank = tierRank[from.tier] ?? 0;
  const toRank = tierRank[to.tier] ?? 0;
  return (
    toRank > fromRank ||
    (toRank === fromRank && to.interval === "year" && from.interval === "month")
  );
}

function lookupKeyParts(lookupKey: string): { tier: string; interval: string } {
  const [tier, period] = lookupKey.split("_");
  return { tier, interval: period === "yearly" ? "year" : "month" };
}

function PlanOffers({
  offers,
  currentTier,
  lapsedTier,
  change,
  tierLimits,
}: {
  offers: PlanOffer[];
  currentTier: string;
  lapsedTier: string | null;
  /** Set for a subscriber: the cards change their plan instead of starting a checkout. */
  change: PlanChangeContext | null;
  tierLimits: TierLimitsByName;
}) {
  const current = change ? lookupKeyParts(change.currentLookupKey) : null;
  const [interval, setBillingInterval] = useState<"month" | "year">(
    current?.interval === "month" ? "month" : "year",
  );
  // A downgrade is confirmed here first: it is scheduled at once, with no Stripe page in between.
  const [confirming, setConfirming] = useState<PlanOfferPrice | null>(null);
  const checkout = useStartCheckout();
  const changePlan = useChangePlan();
  const paid = offers.filter((offer) => offer.prices.length > 0);
  if (paid.length === 0) return null;
  // Free is priced in the same currency as the paid plans beside it.
  const currency = paid[0].prices[0].currency;
  const pending = checkout.isPending || changePlan.isPending;

  const failure = checkout.error ?? changePlan.error;
  const error =
    failure instanceof ApiError
      ? failure.message
      : failure
        ? change
          ? offerCopy.changeFailed
          : offerCopy.checkoutFailed
        : null;

  function action(offer: PlanOffer, price: PlanOfferPrice | undefined) {
    const name = tierNames[offer.tier] ?? offer.tier;
    if (!change || !current) {
      if (offer.tier === currentTier) {
        return (
          <span className="rs-plan-card-current">{offerCopy.currentPlan}</span>
        );
      }
      return price ? (
        <button
          className="rs-primary"
          type="button"
          disabled={pending}
          onClick={() => checkout.mutate(price.lookupKey)}
        >
          {offer.tier === lapsedTier
            ? offerCopy.resubscribeTo(name)
            : offerCopy.upgradeTo(name)}
        </button>
      ) : null;
    }

    // Free is reached by cancelling, which is the billing portal's.
    if (!price) return null;
    if (price.lookupKey === change.currentLookupKey) {
      return (
        <span className="rs-plan-card-current">{offerCopy.currentPlan}</span>
      );
    }
    if (price.lookupKey === change.scheduledLookupKey) {
      return (
        <span className="rs-plan-card-current">
          {offerCopy.from(formatDate(change.switchesOn))}
        </span>
      );
    }
    const target = lookupKeyParts(price.lookupKey);
    const sameTier = target.tier === current.tier;
    const label = sameTier
      ? offerCopy.switchToInterval(target.interval)
      : isUpgrade(current, target)
        ? offerCopy.upgradeTo(name)
        : offerCopy.switchTo(name);
    return isUpgrade(current, target) ? (
      <button
        className="rs-primary"
        type="button"
        disabled={pending}
        onClick={() => changePlan.mutate(price.lookupKey)}
      >
        {label}
      </button>
    ) : (
      <button
        className="rs-secondary"
        type="button"
        disabled={pending}
        onClick={() => setConfirming(price)}
      >
        {label}
      </button>
    );
  }

  return (
    <div className="rs-plan-offers">
      <div
        className="rs-plan-interval"
        role="group"
        aria-label={offerCopy.billingPeriod}
      >
        {(["year", "month"] as const).map((value) => (
          <button
            key={value}
            type="button"
            className="rs-plan-segment"
            aria-pressed={interval === value}
            onClick={() => setBillingInterval(value)}
          >
            {offerCopy.interval[value]}
          </button>
        ))}
      </div>
      <div className="rs-plan-cards">
        {offers.map((offer) => {
          const name = tierNames[offer.tier] ?? offer.tier;
          const price = offer.prices.find((p) => p.interval === interval);
          const isCurrent = change
            ? price?.lookupKey === change.currentLookupKey
            : offer.tier === currentTier;
          const recommended =
            offer.tier === recommendedTier && offer.tier !== currentTier;
          const note =
            offer.tier === "free"
              ? offerCopy.noCard
              : price && interval === "year"
                ? yearlyNote(
                    price,
                    offer.prices.find((p) => p.interval === "month"),
                  )
                : price
                  ? offerCopy.billedMonthly
                  : "";
          return (
            <div
              key={offer.tier}
              className="rs-plan-card"
              data-current={isCurrent || undefined}
              data-recommended={recommended || undefined}
            >
              <div className="rs-plan-card-name">
                {name}
                {recommended ? (
                  <span className="rs-plan-card-badge">
                    {offerCopy.bestValue}
                  </span>
                ) : null}
              </div>
              <div className="rs-plan-card-price">
                {offer.tier === "free"
                  ? formatPrice({
                      lookupKey: "",
                      interval,
                      unitAmount: 0,
                      currency,
                    })
                  : price
                    ? offerCopy.price(formatPrice(price), interval)
                    : offerCopy.notAvailable}
              </div>
              <div className="rs-plan-card-note">{note || "\u00a0"}</div>
              <div className="rs-plan-card-clips">
                {offer.clipLimit !== null
                  ? offerCopy.clipLimit(offer.clipLimit)
                  : offerCopy.unlimitedClips}
              </div>
              <div className="rs-plan-card-length">
                {offer.maxClipSeconds !== null
                  ? offerCopy.upTo(formatClipLength(offer.maxClipSeconds))
                  : offerCopy.anyLength}
              </div>
              <div className="rs-plan-card-action">{action(offer, price)}</div>
            </div>
          );
        })}
      </div>
      {change && confirming ? (
        <DowngradeConfirmation
          price={confirming}
          change={change}
          limit={tierLimits[lookupKeyParts(confirming.lookupKey).tier]}
          currentName={tierNames[currentTier] ?? currentTier}
          pending={changePlan.isPending}
          onConfirm={() =>
            changePlan.mutate(confirming.lookupKey, {
              onSuccess: () => setConfirming(null),
            })
          }
          onCancel={() => setConfirming(null)}
        />
      ) : null}
      <p className="rs-plan-included">{offerCopy.included}</p>
      <p className="rs-plan-footnote">
        {change ? offerCopy.changeFootnote(formatDate(change.switchesOn)) : ""}
        {offerCopy.pricesFootnote(currency.toUpperCase(), !change)}
      </p>
      {error ? (
        <p className="rs-linked-notice" data-tone="error">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** Confirms a downgrade, which waits for the end of the paid period, and what it costs in clips. */
function DowngradeConfirmation({
  price,
  change,
  limit,
  currentName,
  pending,
  onConfirm,
  onCancel,
}: {
  price: PlanOfferPrice;
  change: PlanChangeContext;
  limit: number | null | undefined;
  currentName: string;
  pending: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const target = lookupKeyParts(price.lookupKey);
  const name = planName(target.tier, target.interval);
  const on = formatDate(change.switchesOn);
  const over =
    limit !== null && limit !== undefined
      ? Math.max(0, change.clipCount - limit)
      : 0;
  const cost =
    over === 0
      ? ""
      : change.graceDays !== null
        ? planCopy.downgrade.deletedLater(over, limit!, change.graceDays)
        : planCopy.downgrade.blocked(over, limit!);
  return (
    <PlanAlert
      tone={over > 0 ? "warning" : "info"}
      title={planCopy.downgrade.title(name, on)}
    >
      <p>
        {planCopy.downgrade.body(
          currentName,
          formatPrice(price),
          target.interval,
          cost,
        )}
      </p>
      <div className="rs-plan-actions">
        <button
          className="rs-primary"
          type="button"
          disabled={pending}
          onClick={onConfirm}
        >
          {planCopy.downgrade.confirm(on)}
        </button>
        <button
          className="rs-secondary"
          type="button"
          disabled={pending}
          onClick={onCancel}
        >
          {commonCopy.cancel}
        </button>
      </div>
    </PlanAlert>
  );
}
