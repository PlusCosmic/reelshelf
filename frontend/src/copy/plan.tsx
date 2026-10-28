import type { ReactNode } from "react";
import { plural } from "./plural";

/** Counts here follow the browser's locale, as the plan's own numbers always have. */
const count = (value: number) => value.toLocaleString();

/** "1 clip", "1,000 clips". */
const clipCount = (value: number) =>
  value === 1 ? "1 clip" : `${count(value)} clips`;

/** "yearly" or "monthly", for a billing interval (`year`, `month`). */
const intervalAdjective = (interval: string | null | undefined) =>
  interval === "year" ? "yearly" : "monthly";

/** "year" or "month", for a billing interval (`year`, `month`). */
const intervalNoun = (interval: string) =>
  interval === "year" ? "year" : "month";

const deletedLater = (days: number | null, on: string | null, tier: string) =>
  `They'll be deleted ${days} days later, on ${on}, unless you keep ${tier}.`;
const blockedUnlessKept = (tier: string) =>
  `You won't be able to add clips until you're back within it, unless you keep ${tier}.`;

/** Settings → Plan, the plan meter and the /plan page. */
export const planCopy = {
  tierNames: {
    free: "Free",
    plus: "Plus",
    pro: "Pro",
    unlimited: "Unlimited",
  } as Record<string, string>,
  /** "1 min", "2 min", "90 s". */
  clipLength: {
    minutes: (minutes: number) => `${minutes} min`,
    seconds: (seconds: number) => `${seconds} s`,
  },
  clipCount,
  intervalAdjective,

  /** The meter of clips kept against the plan's limit. */
  meter: {
    label: "Clips kept",
    noLimit: (clips: number) =>
      `${count(clips)} ${clips === 1 ? "clip" : "clips"}`,
    overCompact: (clips: number, over: number) =>
      `${count(clips)} clips · ${count(over)} over the limit`,
    over: (clips: number, over: number, tier: string, limit: number) =>
      `${count(clips)} clips, ${count(over)} over ${tier}'s limit of ${count(limit)}`,
    ofLimit: (clips: number, limit: number) =>
      `${count(clips)} of ${count(limit)} clips`,
    nearlyFull: " · nearly full",
    tier: (tier: string) => ` · ${tier}`,
  },

  /** Settings → Plan. */
  settings: {
    loadFailed: "Your plan couldn't be loaded. Refresh to try again.",
    title: {
      subscribed: (tier: string, interval: string | null | undefined) =>
        `${tier}, ${intervalAdjective(interval)}`,
      granted: (tier: string) => `${tier} (granted)`,
    },
    clipsUpTo: (length: string) => `Clips up to ${length} long`,
    anyLength: "Clips of any length",
    chooseClipsToKeep: "Choose clips to keep",
    keep: (tier: string) => `Keep ${tier}`,
    manageBilling: "Manage billing",
    billingHistory: "Billing history",
    portalHint: "Opens Stripe's billing portal",
    portalFailed: "Billing couldn't be opened. Try again.",
    keepFailed: "Your plan couldn't be kept. Try again.",
    /** Stands in for the tier's name when the subscription doesn't say. */
    yourPlan: "your plan",
    /** "Plus (monthly)". */
    planName: (tier: string, interval: string) =>
      `${tier} (${intervalAdjective(interval)})`,
  },

  /** When the plan renews or ends. */
  subscription: {
    unlimited: "Unlimited clips of any length.",
    endsOn: (on: string, tier: string) =>
      `Ends on ${on}. You keep ${tier} until then.`,
    renewsOn: (on: string) => `Renews on ${on}.`,
    endedOn: (tier: string, on: string) => `${tier} ended on ${on}.`,
    switchesBilling: (interval: string | null | undefined, on: string | null) =>
      `Switches to ${intervalAdjective(interval)} billing on ${on}.`,
    switchesTo: (plan: string, on: string | null, tier: string) =>
      `Switches to ${plan} on ${on}. You keep ${tier} until then.`,
  },

  /** The one thing the account most needs to know, if anything. */
  alerts: {
    updatePaymentMethod: "Update payment method",
    overDeletesOn: (over: number, on: string) =>
      `${clipCount(over)} will be deleted on ${on}.`,
    overLimit: (over: number) =>
      `${clipCount(over)} ${over === 1 ? "is" : "are"} over your plan's limit.`,
    paymentProblemOver: (pastDue: boolean, tier: string) =>
      `${
        pastDue
          ? `Your last payment for ${tier} didn't go through.`
          : `${tier} is on hold because a payment failed, so your account has Free's limits.`
      } Update your payment method to keep them, or choose which clips to keep.`,
    pastDue: {
      title: (tier: string) =>
        `Your last payment for ${tier} didn't go through.`,
      body: (tier: string, clipsCost: string) =>
        `We'll try it again. Update your payment method to keep ${tier}: if the last try fails, ${tier} ends and your account moves to Free's limits${clipsCost}.`,
      deletedLater: (overFree: number, days: number) =>
        `, and the ${clipCount(overFree)} over them will be deleted ${days} days later`,
      cantAdd: (overFree: number) =>
        `, and you can't add clips while ${clipCount(overFree)} are over them`,
    },
    onHold: {
      title: (tier: string) => `${tier} is on hold because a payment failed.`,
      body: "Your account has Free's limits until you update your payment method.",
    },
    over: {
      deletes: (limit: number, canUpgrade: boolean) =>
        `Your plan keeps ${count(limit)}. Choose which clips to keep and download the rest${
          canUpgrade ? ", or pick a plan below to keep them all" : ""
        }.`,
      blocked: (limit: number) =>
        `Your plan keeps ${count(limit)}, so you can't add clips until you're back within it.`,
    },
    scheduled: {
      title: (plan: string, on: string | null, over: number, limit: number) =>
        `When you switch to ${plan} on ${on}, ${clipCount(over)} will be over its limit of ${count(limit)}.`,
      deletedLater,
      blocked: blockedUnlessKept,
    },
    ending: {
      title: (
        tier: string,
        on: string | null,
        over: number,
        freeLimit: number,
      ) =>
        `When ${tier} ends on ${on}, ${clipCount(over)} will be over Free's limit of ${count(freeLimit)}.`,
      deletedLater,
      blocked: blockedUnlessKept,
    },
    full: {
      title: "Your library is full.",
      nearlyTitle: "Your library is nearly full.",
      deleteOrUpgrade: "delete some or upgrade",
      deleteSome: "delete some",
      body: (way: string) => `You can't add clips until you ${way}.`,
      nearlyBody: (clips: number, limit: number, way: string) =>
        `You've used ${count(clips)} of ${count(limit)} clips. At ${count(limit)} you can't add more until you ${way}.`,
    },
  },

  /** Coming back from Checkout. */
  returnBanner: {
    cancelled: {
      title: "Checkout was cancelled.",
      body: "Nothing was charged.",
    },
    upgraded: {
      title: (tier: string) => `You're on ${tier} now.`,
      body: "Thanks for subscribing.",
    },
    slow: {
      title: "Your payment is taking longer than usual to confirm.",
      body: "Refresh in a minute. If your plan still hasn't changed, email us before trying again.",
    },
    confirming: {
      title: "Confirming your payment…",
      body: "This usually takes a few seconds.",
    },
  },

  /** The plan cards. */
  offers: {
    changeFailed: "Your plan couldn't be changed. Try again.",
    checkoutFailed: "Checkout couldn't be started. Try again.",
    currentPlan: "Current plan",
    upgradeTo: (tier: string) => `Upgrade to ${tier}`,
    resubscribeTo: (tier: string) => `Resubscribe to ${tier}`,
    switchTo: (tier: string) => `Switch to ${tier}`,
    switchToInterval: (interval: string) =>
      `Switch to ${intervalAdjective(interval)}`,
    from: (on: string | null) => `From ${on}`,
    billingPeriod: "Billing period",
    interval: { year: "Yearly", month: "Monthly" },
    bestValue: "Best value",
    noCard: "No card needed",
    billedMonthly: "Billed monthly",
    perMonth: (price: string) => `${price}/mo`,
    perMonthFree: (price: string, months: number) =>
      `${price}/mo · ${plural(months, "month", "months")} free`,
    price: (price: string, interval: string) =>
      `${price} / ${intervalNoun(interval)}`,
    notAvailable: "Not available",
    clipLimit: (limit: number) => `${count(limit)} clips`,
    unlimitedClips: "Unlimited clips",
    upTo: (length: string) => `Up to ${length} each`,
    anyLength: "Any length",
    included: "Every plan includes legend detection for Apex Legends clips.",
    changeFootnote: (switchesOn: string | null) =>
      `Upgrades are charged now, less what's left of your current plan. Moving to a smaller plan, or to monthly, waits until ${switchesOn ?? "your plan renews"}. `,
    pricesFootnote: (currency: string, checkout: boolean) =>
      `Prices are in ${currency}${checkout ? "; Checkout shows your own currency where it can" : ""}. Plans are sold by Stripe through Link, so charges show as LINK.COM* REELSHELF.`,
  },

  /** Confirming a downgrade, which waits for the end of the paid period. */
  downgrade: {
    title: (plan: string, on: string | null) =>
      `Switch to ${plan}${on ? ` on ${on}` : ""}?`,
    body: (current: string, price: string, interval: string, cost: string) =>
      `You keep ${current} until then, and pay ${price} a ${intervalNoun(interval)} from then on.${cost}`,
    deletedLater: (over: number, limit: number, days: number) =>
      ` ${clipCount(over)} will then be over its limit of ${count(limit)}, and deleted ${days} days later unless you switch back.`,
    blocked: (over: number, limit: number) =>
      ` ${clipCount(over)} will then be over its limit of ${count(limit)}, so you won't be able to add clips until you're back within it.`,
    confirm: (on: string | null) => `Switch${on ? ` on ${on}` : ""}`,
  },

  /** The /plan page: clips over the limit, picked clips and downloads. */
  page: {
    eyebrow: "Plan",
    overTitle: (over: number) => (
      <>
        <em>{clipCount(over)}</em> over your plan's limit.
      </>
    ),
    title: "Your clips and downloads.",
    loadFailed: "Your clips couldn't be loaded. Refresh to try again.",
    overIntro: (
      limit: number | null | undefined,
      over: number,
      deletesOn: string | null,
      upgradeLink: ReactNode,
    ) => (
      <>
        Your plan keeps {limit?.toLocaleString()} clips.{" "}
        {deletesOn
          ? `The ${clipCount(over)} below will be deleted on ${deletesOn}, and you'll be emailed a download of them.`
          : "The clips below are over the limit; you can't add more until you're back within it."}{" "}
        To keep them, {upgradeLink}, download them, or pick other clips to let
        go instead.
      </>
    ),
    upgradeLink: "upgrade in Settings",
    withinPlan: (settingsLink: ReactNode) => (
      <>All your clips are within your plan. {settingsLink}.</>
    ),
    settingsLink: "See your plan in Settings",
    downloads: "Downloads",
    overTheLimit: "Over the limit",
    picked: "Picked to keep",
    pickedCopy: "Clips you picked are kept before your newest ones.",
    exportFailed: "The download couldn't be started. Try again.",
    makingExport: "Making your download…",
    startExport: "Download clips over the limit",
    exports: {
      graceEnded: (clips: number) =>
        `${clipCount(clips)} deleted when your grace ended`,
      overLimit: (clips: number) => `${clipCount(clips)} over the limit`,
      readyUntil: (until: string | null) => `Ready until ${until}`,
      failed: "Couldn't be made. Email us and we'll sort it out.",
      expired: "Expired",
      making: "Being made; this page updates when it's ready",
      part: (index: number) => `Part ${index} · `,
    },
    untitledClip: "Untitled clip",
    keepInstead: "Keep instead",
    unpick: "Unpick",
    showAll: (clips: number) => `Show all ${count(clips)}`,
  },
} as const;
