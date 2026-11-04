import { describe, expect, it } from "vitest";
import type { AccountPlanResponse, PlanSubscription } from "@/api-client";
import {
  billingReturnReducer,
  checkoutLanded,
  initialBillingReturnPhase,
} from "./billingReturn";

const free: AccountPlanResponse = {
  tier: "free",
  clipCount: 37,
  clipLimit: 100,
  newClipsLast30Days: 12,
  newClipLimit: 200,
  maxClipSeconds: 60,
  storageUsedBytes: 1_000,
  hasTranscripts: false,
  billingEnabled: true,
  canManageBilling: false,
  subscription: null,
  clipsOverLimit: 0,
  overLimitDeletesAt: null,
  freeClipLimit: 100,
  overLimitGraceDays: 14,
  canChangePlan: false,
  grantedTier: null,
};

const subscription = (
  overrides: Partial<PlanSubscription>,
): PlanSubscription => ({
  status: "active",
  tier: "pro",
  interval: "month",
  currentPeriodEnd: new Date("2026-12-04T12:00:00Z"),
  cancelAtPeriodEnd: false,
  cancelAt: null,
  endedAt: null,
  lookupKey: "pro_monthly",
  scheduledTier: null,
  scheduledInterval: null,
  scheduledAt: null,
  ...overrides,
});

describe("checkoutLanded", () => {
  it("waits while the plan hasn't loaded or has no subscription", () => {
    expect(checkoutLanded(undefined, "pro_monthly")).toBe(false);
    expect(checkoutLanded(free, "pro_monthly")).toBe(false);
  });

  it("doesn't take a granted tier for the purchase", () => {
    const granted = { ...free, tier: "plus", grantedTier: "plus" };
    expect(checkoutLanded(granted, "pro_monthly")).toBe(false);
    expect(checkoutLanded(granted, null)).toBe(false);
  });

  it("doesn't take a lapsed or cancelled subscription for the purchase", () => {
    for (const status of ["canceled", "incomplete_expired", "unpaid"]) {
      const lapsed = {
        ...free,
        subscription: subscription({
          status,
          endedAt: new Date("2026-10-01T00:00:00Z"),
        }),
      };
      expect(checkoutLanded(lapsed, "pro_monthly")).toBe(false);
    }
  });

  it("confirms a current subscription for the price bought", () => {
    for (const status of ["active", "trialing", "past_due"]) {
      const paid = {
        ...free,
        tier: "pro",
        subscription: subscription({ status }),
      };
      expect(checkoutLanded(paid, "pro_monthly")).toBe(true);
    }
  });

  it("waits while the subscription is for another price", () => {
    const plus = {
      ...free,
      tier: "plus",
      subscription: subscription({ tier: "plus", lookupKey: "plus_monthly" }),
    };
    expect(checkoutLanded(plus, "pro_monthly")).toBe(false);
  });

  it("accepts any current subscription when the price bought wasn't remembered", () => {
    const paid = { ...free, tier: "pro", subscription: subscription({}) };
    expect(checkoutLanded(paid, null)).toBe(true);
  });
});

describe("billing return phase", () => {
  it("waits after Checkout or the portal, and not otherwise", () => {
    expect(initialBillingReturnPhase("success")).toBe("waiting");
    expect(initialBillingReturnPhase("portal")).toBe("waiting");
    expect(initialBillingReturnPhase("cancelled")).toBe("idle");
    expect(initialBillingReturnPhase(undefined)).toBe("idle");
  });

  it("ends a wait once, by confirmation or timeout", () => {
    expect(billingReturnReducer("waiting", "confirmed")).toBe("confirmed");
    expect(billingReturnReducer("waiting", "timedOut")).toBe("timedOut");
    expect(billingReturnReducer("confirmed", "timedOut")).toBe("confirmed");
    expect(billingReturnReducer("timedOut", "timedOut")).toBe("timedOut");
    expect(billingReturnReducer("idle", "confirmed")).toBe("idle");
    expect(billingReturnReducer("idle", "timedOut")).toBe("idle");
  });

  it("still confirms a purchase seen after the window ran out", () => {
    expect(billingReturnReducer("timedOut", "confirmed")).toBe("confirmed");
  });
});
