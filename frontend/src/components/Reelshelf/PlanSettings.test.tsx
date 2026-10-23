// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { AccountPlanResponse, PlanOffer } from "@/api-client";
import { PlanSettings } from "./PlanSettings";

const mocks = vi.hoisted(() => ({
  plan: undefined as unknown,
  offers: undefined as unknown,
  checkout: vi.fn(),
  portal: vi.fn(),
}));

vi.mock("@/hooks/queries", () => ({
  usePlan: () => ({ data: mocks.plan, isError: false, refetch: vi.fn() }),
  usePlanOffers: () => ({ data: mocks.offers }),
  useStartCheckout: () => ({
    mutate: mocks.checkout,
    isPending: false,
    error: null,
  }),
  useOpenBillingPortal: () => ({
    mutate: mocks.portal,
    isPending: false,
    isError: false,
  }),
}));

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
};

const offers: PlanOffer[] = [
  {
    tier: "free",
    clipLimit: 100,
    maxClipSeconds: 60,
    newClipLimit: 200,
    hasTranscripts: false,
    prices: [],
  },
  {
    tier: "plus",
    clipLimit: 1000,
    maxClipSeconds: 120,
    newClipLimit: 2000,
    hasTranscripts: false,
    prices: [
      {
        lookupKey: "plus_monthly",
        interval: "month",
        unitAmount: 300,
        currency: "gbp",
      },
      {
        lookupKey: "plus_yearly",
        interval: "year",
        unitAmount: 3000,
        currency: "gbp",
      },
    ],
  },
];

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("PlanSettings", () => {
  it("shows a free account its usage and the plans to upgrade to, yearly first", () => {
    mocks.plan = free;
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText("37 of 100 clips · Free")).toBeTruthy();
    expect(
      screen.getByText(
        "Clips up to 1 min · 12 of 200 new clips in the last 30 days",
      ),
    ).toBeTruthy();
    expect(screen.getByText("Current plan")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Plus" }));
    expect(mocks.checkout).toHaveBeenCalledWith("plus_yearly");

    fireEvent.click(screen.getByRole("button", { name: "Monthly" }));
    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Plus" }));
    expect(mocks.checkout).toHaveBeenLastCalledWith("plus_monthly");
  });

  it("sends a subscriber to Manage billing instead of offering plans", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      clipLimit: 1000,
      canManageBilling: true,
      subscription: {
        status: "active",
        tier: "plus",
        interval: "year",
        currentPeriodEnd: new Date("2027-10-23T00:00:00Z"),
        cancelAtPeriodEnd: false,
        cancelAt: null,
        endedAt: null,
      },
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText(/Plus \(yearly\) renews on/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Manage billing" }));
    expect(mocks.portal).toHaveBeenCalled();
  });

  it("warns when a payment failed", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      canManageBilling: true,
      subscription: {
        status: "past_due",
        tier: "plus",
        interval: "month",
        currentPeriodEnd: null,
        cancelAtPeriodEnd: false,
        cancelAt: null,
        endedAt: null,
      },
    };
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText(/didn't go through/)).toBeTruthy();
  });

  it("offers nothing to an unlimited account or while billing is off", () => {
    mocks.plan = {
      ...free,
      tier: "unlimited",
      clipLimit: null,
      newClipLimit: null,
      maxClipSeconds: null,
    };
    mocks.offers = offers;
    const { unmount } = render(<PlanSettings billingReturn={undefined} />);
    expect(screen.getByText("37 clips · Unlimited")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
    unmount();

    mocks.plan = { ...free, billingEnabled: false };
    render(<PlanSettings billingReturn={undefined} />);
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
  });
});
