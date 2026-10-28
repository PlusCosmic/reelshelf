import type { ReactNode } from "react";

/** The public page a share link opens, and its form for reporting the clip. */
export const shareCopy = {
  loading: "Loading clip…",
  unavailable:
    "This link doesn't work any more. The clip may have been deleted or unshared.",
  loadFailed: "This clip didn't load. Try again in a moment.",
  report: {
    button: "Report this clip",
    eyebrow: "Report",
    title: "What's wrong with this clip?",
    reason: "Reason",
    childSafety: (iwf: ReactNode, ncmec: ReactNode) => (
      <>
        You can also report it to the {iwf} or, in the US, {ncmec}. Please don't
        download or share it.
      </>
    ),
    iwf: "Internet Watch Foundation",
    ncmec: "NCMEC's CyberTipline",
    details: "Anything else we should know? (optional)",
    email: "Your email, if you'd like to hear back (optional)",
    emailCopyright: ". For copyright, include it so we can follow up",
    sending: "Sending…",
    send: "Send report",
    failed: "The report couldn't be sent. Try again.",
    thanks: {
      title: "Thanks for telling us",
      note: "We'll review this clip and remove it if it breaks our terms.",
      linkHidden: " Its link has stopped working while we do.",
    },
  },
} as const;
