import type { ReactNode } from "react";

/** The Settings page, the first-sign-in email step, linked accounts and deleting the account. */
export const settingsCopy = {
  eyebrow: "Settings",
  title: (name: string) => (
    <>
      Settings for <em>{name}</em>.
    </>
  ),
  /** Stands in for the account's name before it has loaded. */
  yourAccount: "your account",
  sections: {
    plan: "Plan",
    email: {
      title: "Account email",
      copy: "For things about your account, like a new sign-in being linked or a warning before any clips are deleted. No newsletters. Leave it empty and we won't email you, warnings included.",
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
    copy: "Only for things about your account, like a new sign-in being linked or a warning before any clips are deleted. No newsletters.",
    submit: "Use this address",
  },

  linkedAccounts: {
    /** By the `link_error` the API sends the browser back with. */
    linkErrors: {
      session_expired:
        "You were signed out while linking. Sign in and try again.",
      provider_already_linked:
        "Your account already has a sign-in from there. Unlink it first.",
      linked_to_another_account:
        "That sign-in already belongs to another Reelshelf account.",
      provider_failed: "Linking didn't finish, so nothing changed.",
    } as Record<string, string>,
    linkFailed: "Linking didn't finish, so nothing changed.",
    linked: (provider: string) => `${provider} is now linked to your account.`,
    loading: "Loading linked accounts…",
    loadFailed: "Your linked accounts didn't load.",
    primary: "primary",
    handle: (provider: string, username: string) =>
      `${provider} · @${username}`,
    unlink: "Unlink",
    cantUnlink: "Link another sign-in before removing this one",
    unlinkFailed: "That didn't unlink. Try again.",
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
