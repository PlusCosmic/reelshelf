import type { ReactNode } from "react";
import { formatCount, planLimits } from "./planLimits";

export const signInCopy = {
  eyebrow: "Sign in or create an account",
  title: (
    <>
      Open your <em>shelf</em>.
    </>
  ),
  intro: `Choose Discord or Twitch. If you're new, that makes your account, free for up to ${formatCount(planLimits.free.clips)} clips. You can link the other one later.`,
  continueWith: (provider: string) => `Continue with ${provider}`,
  accountDeleted:
    "Your account has been deleted. We're removing its clips now.",
  suspended: (contact: ReactNode) => (
    <>
      This account has been suspended for breaking our terms of service. If you
      think we've got that wrong, email {contact}.
    </>
  ),
  authFailed: "Sign-in didn't finish, so nothing changed. Try again.",
  agreeToTerms: (terms: ReactNode, privacy: ReactNode) => (
    <>
      By continuing you agree to the {terms} and {privacy}.
    </>
  ),
  termsLink: "terms of service",
  privacyLink: "privacy policy",
} as const;
