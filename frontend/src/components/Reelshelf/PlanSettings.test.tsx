// @vitest-environment jsdom
import type { ReactNode } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  AccountPlanResponse,
  PlanOffer,
  PlanSubscription,
} from "@/api-client";
import { PlanSettings } from "./PlanSettings";

const mocks = vi.hoisted(() => ({
  plan: undefined as unknown,
  offers: undefined as unknown,
  checkout: vi.fn(),
  portal: vi.fn(),
  changePlan: vi.fn(),
  keep: vi.fn(),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
}));

vi.mock("@/hooks/queries", () => ({
  usePlan: () => ({ data: mocks.plan, isError: false, refetch: vi.fn() }),
  usePlanOffers: () => ({ data: mocks.offers }),
  useStartCheckout: () => ({
    mutate: mocks.checkout,
    isPending: false,
    error: null,
  }),
  useChangePlan: () => ({
    mutate: mocks.changePlan,
    isPending: false,
    error: null,
  }),
  useKeepPlan: () => ({
    mutate: mocks.keep,
    isPending: false,
    isError: false,
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
  tier: "plus",
  interval: "month",
  currentPeriodEnd: new Date("2026-11-24T12:00:00Z"),
  cancelAtPeriodEnd: false,
  cancelAt: null,
  endedAt: null,
  lookupKey: "plus_monthly",
  scheduledTier: null,
  scheduledInterval: null,
  scheduledAt: null,
  ...overrides,
});

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
  {
    tier: "pro",
    clipLimit: 5000,
    maxClipSeconds: 120,
    newClipLimit: 10000,
    hasTranscripts: true,
    prices: [
      {
        lookupKey: "pro_monthly",
        interval: "month",
        unitAmount: 800,
        currency: "gbp",
      },
      {
        lookupKey: "pro_yearly",
        interval: "year",
        unitAmount: 8000,
        currency: "gbp",
      },
    ],
  },
];

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function alerts() {
  return document.querySelectorAll(".rs-plan-alert");
}

describe("PlanSettings", () => {
  it("shows a free account its usage and the plans to upgrade to, yearly first", () => {
    mocks.plan = free;
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByRole("heading", { name: "Free" })).toBeTruthy();
    expect(screen.getByText("37 of 100 clips")).toBeTruthy();
    expect(screen.getByText("Clips up to 1 min long")).toBeTruthy();
    expect(screen.getByText("£0")).toBeTruthy();
    expect(screen.getByText("£2.50/mo · 2 months free")).toBeTruthy();
    expect(screen.getByText("Current plan")).toBeTruthy();
    expect(screen.getByText("Best value")).toBeTruthy();
    expect(alerts()).toHaveLength(0);

    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Plus" }));
    expect(mocks.checkout).toHaveBeenCalledWith("plus_yearly");

    fireEvent.click(screen.getByRole("button", { name: "Monthly" }));
    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Plus" }));
    expect(mocks.checkout).toHaveBeenLastCalledWith("plus_monthly");
  });

  it("names a granted plan as granted and still offers the plans above it", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      clipLimit: 1000,
      grantedTier: "plus",
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(
      screen.getByRole("heading", { name: "Plus (granted)" }),
    ).toBeTruthy();
    expect(screen.getByText("Current plan")).toBeTruthy();
    expect(
      screen.queryByRole("button", { name: "Upgrade to Plus" }),
    ).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Pro" }));
    expect(mocks.checkout).toHaveBeenCalledWith("pro_yearly");
  });

  it("says in words when the library is nearly full", () => {
    mocks.plan = { ...free, clipCount: 93 };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText("Your library is nearly full.")).toBeTruthy();
    expect(
      screen.getByText(
        /At 100 you can't add more until you delete some or upgrade/,
      ),
    ).toBeTruthy();
  });

  it("names a subscriber's plan and sends them to Manage billing instead of offering plans", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      clipLimit: 1000,
      canManageBilling: true,
      subscription: subscription({ interval: "year" }),
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByRole("heading", { name: "Plus, yearly" })).toBeTruthy();
    expect(screen.getByText(/Renews on/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Manage billing" }));
    expect(mocks.portal).toHaveBeenCalled();
  });

  it("warns a plan set to end what it will cost, and offers to keep it", () => {
    mocks.plan = {
      ...free,
      tier: "pro",
      clipCount: 1820,
      clipLimit: 5000,
      canManageBilling: true,
      subscription: subscription({
        tier: "pro",
        cancelAtPeriodEnd: true,
        currentPeriodEnd: new Date("2027-03-02T12:00:00Z"),
      }),
    };
    render(<PlanSettings billingReturn={undefined} />);

    expect(
      screen.getByText(
        /When Pro ends on .*, 1,720 clips will be over Free's limit of 100\./,
      ),
    ).toBeTruthy();
    expect(screen.getByText(/deleted 14 days later/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Keep Pro" }));
    expect(mocks.portal).toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Manage billing" })).toBeNull();
  });

  it("leads with the clips over the limit and when they go", () => {
    mocks.plan = {
      ...free,
      clipCount: 130,
      clipsOverLimit: 30,
      overLimitDeletesAt: new Date("2026-11-06T12:00:00Z"),
      canManageBilling: true,
      subscription: subscription({
        status: "canceled",
        endedAt: new Date("2026-10-23T12:00:00Z"),
      }),
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText(/30 clips will be deleted on/)).toBeTruthy();
    expect(screen.getByText("Choose clips to keep")).toBeTruthy();
    expect(
      screen.getByRole("button", { name: "Resubscribe to Plus" }),
    ).toBeTruthy();
  });

  it("tells a failing payment what the last retry costs", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      clipCount: 412,
      clipLimit: 1000,
      canManageBilling: true,
      subscription: subscription({ status: "past_due" }),
    };
    render(<PlanSettings billingReturn={undefined} />);

    expect(screen.getByText(/didn't go through/)).toBeTruthy();
    expect(
      screen.getByText(/the 312 clips over them will be deleted 14 days later/),
    ).toBeTruthy();
    fireEvent.click(
      screen.getByRole("button", { name: "Update payment method" }),
    );
    expect(mocks.portal).toHaveBeenCalled();
  });

  it("puts a plan on hold and the clips it will cost in one alert", () => {
    mocks.plan = {
      ...free,
      clipCount: 412,
      canManageBilling: true,
      clipsOverLimit: 312,
      overLimitDeletesAt: new Date("2026-11-07T12:00:00Z"),
      subscription: subscription({ status: "unpaid", currentPeriodEnd: null }),
    };
    render(<PlanSettings billingReturn={undefined} />);

    expect(alerts()).toHaveLength(1);
    expect(screen.getByText(/312 clips will be deleted on/)).toBeTruthy();
    expect(
      screen.getByText(/Plus is on hold because a payment failed/),
    ).toBeTruthy();
    expect(
      screen.getByRole("button", { name: "Update payment method" }),
    ).toBeTruthy();
    expect(screen.getByText("Choose clips to keep")).toBeTruthy();
  });

  it("hides the plans while a payment just made is confirmed, so it can't be made twice", () => {
    mocks.plan = free;
    mocks.offers = offers;
    render(<PlanSettings billingReturn="success" />);

    expect(screen.getByText("Confirming your payment…")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
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
    expect(screen.getByText("37 clips")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
    unmount();

    mocks.plan = { ...free, billingEnabled: false };
    render(<PlanSettings billingReturn={undefined} />);
    expect(screen.queryByRole("button", { name: /Upgrade/ })).toBeNull();
  });

  it("lets a subscriber upgrade from the cards, confirmed at Stripe", () => {
    mocks.plan = {
      ...free,
      tier: "plus",
      clipLimit: 1000,
      canManageBilling: true,
      canChangePlan: true,
      subscription: subscription({}),
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    // Their own period is shown first, with their plan marked.
    expect(screen.getByText("Current plan")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Upgrade to Pro" }));
    expect(mocks.changePlan).toHaveBeenCalledWith("pro_monthly");
    fireEvent.click(screen.getByRole("button", { name: "Yearly" }));
    fireEvent.click(screen.getByRole("button", { name: "Switch to yearly" }));
    expect(mocks.changePlan).toHaveBeenLastCalledWith("plus_yearly");
    expect(mocks.checkout).not.toHaveBeenCalled();
  });

  it("confirms a downgrade first, with when it happens and the clips it costs", () => {
    mocks.plan = {
      ...free,
      tier: "pro",
      clipCount: 1820,
      clipLimit: 5000,
      canManageBilling: true,
      canChangePlan: true,
      subscription: subscription({
        tier: "pro",
        interval: "year",
        lookupKey: "pro_yearly",
        currentPeriodEnd: new Date("2027-03-02T12:00:00Z"),
      }),
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    fireEvent.click(screen.getByRole("button", { name: "Switch to Plus" }));
    expect(mocks.changePlan).not.toHaveBeenCalled();
    expect(
      screen.getByText(/Switch to Plus \(yearly\) on .*2027\?/),
    ).toBeTruthy();
    expect(
      screen.getByText(/820 clips will then be over its limit of 1,000/),
    ).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /^Switch on / }));
    expect(mocks.changePlan.mock.calls[0][0]).toBe("plus_yearly");
  });

  it("shows a downgrade waiting for the period's end, and lets them keep their plan", () => {
    mocks.plan = {
      ...free,
      tier: "pro",
      clipCount: 1820,
      clipLimit: 5000,
      canManageBilling: true,
      canChangePlan: true,
      subscription: subscription({
        tier: "pro",
        lookupKey: "pro_monthly",
        scheduledTier: "plus",
        scheduledInterval: "month",
        scheduledAt: new Date("2026-11-24T12:00:00Z"),
      }),
    };
    mocks.offers = offers;
    render(<PlanSettings billingReturn={undefined} />);

    expect(
      screen.getByText(
        /Switches to Plus \(monthly\) on .*You keep Pro until then\./,
      ),
    ).toBeTruthy();
    expect(
      screen.getByText(/820 clips will be over its limit of 1,000\./),
    ).toBeTruthy();
    expect(screen.getByText(/^From /)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Keep Pro" }));
    expect(mocks.keep).toHaveBeenCalled();
  });
});
