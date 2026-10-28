import type { ReactNode } from "react";

/** The Settings page, the first-sign-in email step, linked accounts and deleting the account. */
export const settingsCopy = {
  eyebrow: "Settings",
  title: (name: string) => (
    <>
      Archive preferences for <em>{name}</em>.
    </>
  ),
  /** Stands in for the account's name before it has loaded. */
  yourAccount: "your account",
  sections: {
    plan: "Plan",
    email: {
      title: "Account email",
      copy: "Used for notices about newly linked sign-in methods and when your library is nearly full. Leave it empty to receive nothing.",
    },
    linkedAccounts: {
      title: "Linked accounts",
      copy: "Sign in with any linked account. Your name and avatar follow the primary one.",
    },
    yourData: {
      title: "Your data",
      copy: (privacy: ReactNode, terms: ReactNode) => (
        <>
          Read the {privacy} and {terms}.
        </>
      ),
      privacyLink: "privacy policy",
      termsLink: "terms of service",
    },
    deleteAccount: {
      title: "Delete account",
      copy: "Permanently delete your account, every clip in it and the collections you made. This can't be undone.",
    },
  },

  /** Where account mail goes, in Settings and on first sign-in. */
  email: {
    label: "Email",
    placeholder: "you@example.com",
    saveFailed: "Couldn't save that address.",
    saved: "Saved.",
    skip: "Skip for now",
  },

  /** The first-sign-in step asking for an email address. */
  onboarding: {
    eyebrow: "One more thing",
    title: (
      <>
        Where should we <em>reach you</em>?
      </>
    ),
    copy: "We email you when a new sign-in method is linked to your account and when your library is nearly full. Nothing else.",
    submit: "Use this address",
  },

  linkedAccounts: {
    /** By the `link_error` the API sends the browser back with. */
    linkErrors: {
      session_expired:
        "Your session changed while linking. Sign in again and retry.",
      provider_already_linked:
        "This account already has a login from that provider. Unlink it first.",
      linked_to_another_account:
        "That login already belongs to a different Reelshelf account.",
      provider_failed:
        "The provider didn't complete the sign-in. Nothing changed.",
    } as Record<string, string>,
    linkFailed: "Linking didn't complete. Nothing changed.",
    linked: (provider: string) => `${provider} is now linked to your account.`,
    loading: "Loading linked accounts…",
    loadFailed: "Couldn't load linked accounts.",
    primary: "primary",
    handle: (provider: string, username: string) =>
      `${provider} · @${username}`,
    unlink: "Unlink",
    cantUnlink: "Link another sign-in method before removing this one",
    unlinkFailed: "Couldn't unlink that account.",
    link: (provider: string) => `Link ${provider}`,
  },

  deleteAccount: {
    open: "Delete account…",
    eyebrow: "Delete account",
    title: "Delete your account?",
    intro: "This permanently deletes, and can't be undone:",
    clips: (summary: string) =>
      `every clip you've uploaded or imported${summary}, with its tags, detections and transcripts`,
    /** " (12 clips)", after the clips line. */
    clipsSummary: (clips: number) =>
      ` (${clips.toLocaleString()} ${clips === 1 ? "clip" : "clips"})`,
    collections:
      "your share links, and the collections you made, including for their collaborators",
    paidPlan: "your paid plan, which is cancelled straight away",
    signIns: "your linked Discord and Twitch sign-ins",
    confirm: (phrase: string) => (
      <>
        Type <strong>{phrase}</strong> to confirm
      </>
    ),
    failed: "Your account couldn't be deleted. Try again.",
    submit: "Permanently delete my account",
  },
} as const;
