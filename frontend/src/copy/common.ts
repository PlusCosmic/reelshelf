import { plural } from "./plural";

/** Words used across the app. Area-specific copy lives in its own module beside this one. */
export const commonCopy = {
  brand: "Reelshelf",
  loading: "Loading…",
  cancel: "Cancel",
  close: "Close",
  save: "Save",
  saving: "Saving…",
  delete: "Delete",
  deleting: "Deleting…",
  tryAgain: "Try again",
  backToFrontPage: "Back to the front page",
  termsLink: "Terms of service",
  privacyLink: "Privacy policy",
  legalNav: "Legal",
  switchTheme: (next: "light" | "dark") => `Switch to ${next} mode`,
  clips: (count: number) => plural(count, "clip", "clips"),
  seconds: (seconds: number, digits = 1) => `${seconds.toFixed(digits)}s`,
} as const;
