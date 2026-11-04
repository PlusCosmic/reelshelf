import { useEffect, useReducer, useRef, useState } from "react";
import type { AccountPlan } from "@/shared/services/plans";

/** Where the browser came back from: Checkout (`success`, `cancelled`) or the Customer Portal (`portal`). */
export type BillingReturn = "success" | "cancelled" | "portal" | undefined;

/**
 * Coming back from Checkout or the portal, while Stripe's webhook catches up: `idle` when there is nothing to wait for
 * (no return, or a cancelled Checkout), `waiting` while the plan is refetched, `confirmed` once what was bought shows
 * up (Checkout only, even after the window), and `timedOut` when the window ran out first (a portal return always
 * ends so, quietly).
 */
export type BillingReturnPhase = "idle" | "waiting" | "confirmed" | "timedOut";

export type BillingReturnEvent = "confirmed" | "timedOut";

/** After Checkout or the portal, Stripe's webhook updates the plan within seconds; poll for that long, from the return. */
export const pollAfterReturnMs = 30_000;

/** How often the plan is refetched while waiting. */
export const pollIntervalMs = 2_000;

/** Statuses whose subscription is the account's current plan. */
const currentStatuses = new Set(["active", "trialing", "past_due"]);

export function initialBillingReturnPhase(
  billingReturn: BillingReturn,
): BillingReturnPhase {
  return billingReturn === "success" || billingReturn === "portal"
    ? "waiting"
    : "idle";
}

/**
 * A wait ends once, by confirmation or timeout. A purchase seen after the window (the plan refetched on focus, say)
 * still confirms it; nothing undoes a confirmation.
 */
export function billingReturnReducer(
  phase: BillingReturnPhase,
  event: BillingReturnEvent,
): BillingReturnPhase {
  if (phase === "waiting") return event;
  if (phase === "timedOut" && event === "confirmed") return "confirmed";
  return phase;
}

/**
 * Whether the purchase made in Checkout has landed. Checkout refuses an account with an open subscription, so a
 * current one on the account now is the new one; a granted tier or a lapsed subscription row never counts. When the
 * lookup key bought is known, the subscription must be for it.
 */
export function checkoutLanded(
  plan: AccountPlan | undefined,
  boughtLookupKey: string | null,
): boolean {
  const subscription = plan?.subscription;
  if (!subscription || !currentStatuses.has(subscription.status)) return false;
  return boughtLookupKey === null || subscription.lookupKey === boughtLookupKey;
}

const checkoutStorageKey = "reelshelf.billing.checkoutLookupKey";

/** Remembers, for this tab, the price a Checkout is about to be started for, so the return can look for it. */
export function rememberCheckout(lookupKey: string): void {
  try {
    window.sessionStorage.setItem(checkoutStorageKey, lookupKey);
  } catch {
    // Without storage the return accepts any new subscription.
  }
}

/** The price this tab last started a Checkout for, if it was remembered. */
export function rememberedCheckout(): string | null {
  try {
    return window.sessionStorage.getItem(checkoutStorageKey);
  } catch {
    return null;
  }
}

export function forgetCheckout(): void {
  try {
    window.sessionStorage.removeItem(checkoutStorageKey);
  } catch {
    // Nothing to forget.
  }
}

/**
 * The return from Checkout or the portal as one phase. The plan is refetched every couple of seconds for one fixed
 * window from the return, which nothing restarts, and polling stops as soon as the phase leaves `waiting`.
 */
export function useBillingReturn(
  billingReturn: BillingReturn,
  plan: AccountPlan | undefined,
  refetch: () => unknown,
): BillingReturnPhase {
  // Read once: the route cleans the URL at once, and this tab's next Checkout writes a new one.
  const [boughtLookupKey] = useState(() =>
    billingReturn === "success" ? rememberedCheckout() : null,
  );
  const [phase, dispatch] = useReducer(
    billingReturnReducer,
    billingReturn,
    initialBillingReturnPhase,
  );
  const refetchRef = useRef(refetch);
  useEffect(() => {
    refetchRef.current = refetch;
  }, [refetch]);

  useEffect(() => {
    if (billingReturn === "success" || billingReturn === "cancelled") {
      forgetCheckout();
    }
  }, [billingReturn]);

  const landed =
    billingReturn === "success" && checkoutLanded(plan, boughtLookupKey);
  useEffect(() => {
    if (landed) dispatch("confirmed");
  }, [landed]);

  const waiting = phase === "waiting" && !landed;
  useEffect(() => {
    if (!waiting) return;
    const interval = window.setInterval(
      () => void refetchRef.current(),
      pollIntervalMs,
    );
    const timeout = window.setTimeout(
      () => dispatch("timedOut"),
      pollAfterReturnMs,
    );
    return () => {
      window.clearInterval(interval);
      window.clearTimeout(timeout);
    };
  }, [waiting]);

  // Confirmed from the render the plan shows it in, without a frame of "confirming" first.
  return landed ? billingReturnReducer(phase, "confirmed") : phase;
}
