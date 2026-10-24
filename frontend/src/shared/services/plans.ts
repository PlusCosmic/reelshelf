import type {
  AccountPlanResponse,
  ClipExportResponse,
  OverLimitResponse,
  PlanChangeResponse,
  PlanOffer,
  PlanOfferPrice,
} from "@/api-client";
import { apiConfig } from "../config/apiConfig";
import {
  createBillingApi,
  createOverLimitApi,
  createPlanApi,
  createUserApi,
} from "./apiClients";

export type AccountPlan = AccountPlanResponse;
export type OverLimit = OverLimitResponse;
export type ClipExport = ClipExportResponse;
export type { PlanOffer, PlanOfferPrice };

export type Tier = "free" | "plus" | "pro" | "unlimited";

export const tierNames: Record<string, string> = {
  free: "Free",
  plus: "Plus",
  pro: "Pro",
  unlimited: "Unlimited",
};

/** The signed-in account's tier, limits, usage and subscription. */
export async function fetchPlan(): Promise<AccountPlan> {
  return createUserApi().getMyPlan();
}

/** Every tier's limits, with the paid ones' prices while billing is on. */
export async function fetchPlanOffers(): Promise<PlanOffer[]> {
  return createPlanApi().getPlans();
}

/** Starts Stripe Checkout for a price and returns the page to send the browser to. */
export async function startCheckout(lookupKey: string): Promise<string> {
  const redirect = await createBillingApi().startCheckout({
    startCheckoutRequest: { lookupKey },
  });
  return redirect.url;
}

/**
 * Changes a subscriber's plan: an upgrade answers Stripe's page confirming it (`url`), a downgrade when it takes
 * effect (`scheduledAt`, the end of the paid period).
 */
export async function changePlan(
  lookupKey: string,
): Promise<PlanChangeResponse> {
  return createBillingApi().changePlan({
    startCheckoutRequest: { lookupKey },
  });
}

/** Drops a downgrade waiting for the end of the paid period. */
export async function keepPlan(): Promise<void> {
  await createBillingApi().keepPlan();
}

/** Opens Stripe's Customer Portal and returns the page to send the browser to. */
export async function openBillingPortal(): Promise<string> {
  const redirect = await createBillingApi().openBillingPortal();
  return redirect.url;
}

/** The clips over the account's limit, and the ones the owner picked to keep. */
export async function fetchOverLimit(): Promise<OverLimit> {
  return createOverLimitApi().getMyOverLimit();
}

/** Picks a clip to keep when over the limit, or unpicks it. */
export async function setClipKept(
  clipId: string,
  keep: boolean,
): Promise<void> {
  await createOverLimitApi().setClipKept({
    clipId,
    setClipKeptRequest: { keep },
  });
}

/** The account's exports, newest first. */
export async function fetchExports(): Promise<ClipExport[]> {
  return createOverLimitApi().getMyExports();
}

/** Starts a download of the clips over the limit. */
export async function startExport(): Promise<ClipExport> {
  return createOverLimitApi().startMyExport();
}

/**
 * Where one zip of an export downloads from: a plain link, so the browser follows the API's redirect to storage
 * itself.
 */
export function exportPartUrl(exportId: string, index: number): string {
  return `${apiConfig.baseUrl}/api/me/exports/${exportId}/parts/${index}`;
}

/** "£3", "$4.50": a price in its minor unit, in the browser's locale. */
export function formatPrice(price: PlanOfferPrice): string {
  const amount = price.unitAmount / 100;
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: price.currency.toUpperCase(),
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
  }).format(amount);
}

/** "1 min", "2 min", "90 s". */
export function formatClipLength(seconds: number): string {
  return seconds >= 60 && seconds % 60 === 0
    ? `${seconds / 60} min`
    : `${seconds} s`;
}

/** How far past the length cap a clip may run before the API refuses it, as containers round. */
export const clipLengthToleranceSeconds = 1;
