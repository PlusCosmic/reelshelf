import type { ReactNode } from "react";
import { plural } from "./plural";

/** The admin pages: accounts and content reports, each in the order it reads. */
export const adminCopy = {
  accounts: {
    adminsOnly: "Accounts are only available to admins.",
    eyebrow: "Accounts",
    title: "Roles and granted plans.",
    roles: {
      viewer: "Viewer",
      editor: "Editor",
      admin: "Admin",
    } as Record<string, string>,
    states: {
      active: "Active",
      suspended: "Suspended",
    },
    search: {
      label: "Find an account",
      placeholder: "Username, display name or Discord/Twitch id",
      failed: "The search didn't work.",
      searching: "Searching…",
      noMatch: "No account matches.",
    },
    pickAccount: "Pick an account to see or change its role and granted plan.",
    loadFailed: "That account couldn't be loaded.",
    granted: (tier: string) => `${tier} granted`,
    suspendedBadge: "Suspended",
    onTier: (tier: string, granted: boolean) =>
      `On ${tier} now${granted ? ", by a grant" : ""}.`,
    accountId: "Account ID",
    suspendedSince: (date: string) =>
      `Suspended since ${date}: it can't sign in and its share links don't work. Its clips are kept.`,
    form: {
      role: "Role",
      grantedPlan: "Granted plan",
      noGrant: "None",
      account: "Account",
      reason: "Reason",
      reasonPlaceholder: "Why, for the record",
      cantChangeSelf: "You can't change your own role or suspend yourself.",
      cantSuspendAdmin: "Admins can't be suspended; change the role first.",
    },
    changes: {
      empty: "No changes recorded.",
      when: "When",
      change: "Change",
      by: "By",
      reason: "Reason",
      suspended: "Suspended",
      suspensionLifted: "Suspension lifted",
      role: "Role",
      grantedPlan: "Granted plan",
      changed: (field: string, from: string, to: string) =>
        `${field}: ${from} → ${to}`,
      none: "none",
    },
  },
  reports: {
    adminsOnly: "Reports are only available to admins.",
    eyebrow: "Reports",
    title: (resolved: boolean) =>
      resolved ? "Reports dealt with." : "Reported shared clips.",
    toggle: (resolved: boolean) =>
      resolved ? "Show open reports" : "Show dealt with",
    loadFailed: "The reports couldn't be loaded.",
    empty: (resolved: boolean) =>
      resolved ? "No report has been dealt with yet." : "No open reports.",
    statuses: {
      open: "Open",
      taken_down: "Taken down",
      dismissed: "Dismissed",
      removed: "Removed",
    } as Record<string, string>,
    reported: (date: string) => `Reported ${date}`,
    openReportsOnClip: (count: number) =>
      `${plural(count, "open report", "open reports")} on this clip`,
    untitled: "Untitled clip",
    byOwner: (name: string) => ` by ${name}`,
    openSharedClip: "Open the shared clip",
    reporter: (email: ReactNode) => <>Reporter: {email}</>,
    childSafetyWarning: (clipId: string | null | undefined) =>
      `Don't open or download this clip. If the report looks genuine, remove it, then report it to the IWF (UK) or NCMEC's CyberTipline (US) with the clip's id (${clipId ?? "deleted"}). Its share link stays hidden until you decide.`,
    resolvedBy: (name: string) => ` by ${name}`,
    shareState: {
      removed: "Removed from Reelshelf.",
      deleted: "The clip has since been deleted.",
      blocked: "Taken down: it can't be shared.",
      hidden:
        "Its share link is hidden while a child-safety or terrorism report is open.",
      noLink: "It has no live share link.",
    },
    removal: {
      nothingKept: "Nothing was kept: the clip had already been deleted.",
      failed: (error: string | null | undefined) =>
        `The clip couldn't be kept or taken off public storage: ${error ?? "no error was recorded"}. Fix the cause, then retry it by hand (docs/moderation.md).`,
      pending: "Keeping a private copy and taking it off public storage…",
      retrying: (error: string) => ` Retrying after: ${error}`,
      kept: (date: string) =>
        `Kept privately as evidence until ${date}, and gone from public storage.`,
      notPurged:
        "The media domain wasn't purged, so it may still serve cached copies. Purge the clip's URLs in Cloudflare by hand.",
      ownerSuspended: "The owner's account is suspended.",
      ownerNotSuspended:
        "The owner's account isn't suspended (Admins can't be).",
    },
    resolve: {
      removeExplain: (owner: string | null | undefined) =>
        `Removing it deletes the clip from Reelshelf, keeps a private copy for a year for the authorities, and suspends ${owner ?? "the owner"}. They aren't emailed.`,
      note: "Note",
      notePlaceholder: (urgent: boolean) =>
        `Why, for the record (needed to ${urgent ? "remove" : "take down"})`,
      remove: "Remove clip",
      takeDown: "Take down",
      dismiss: "Dismiss",
    },
  },
} as const;
