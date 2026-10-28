import type { ReactNode } from "react";
import { formatCount, planLimits } from "./planLimits";

export const signInCopy = {
  eyebrow: "Sign in or create an account",
  title: (
    <>
      Open your <em>shelf</em>.
    </>
  ),
  intro: `Continue with any provider below. If you're new, this creates your account. It's free for up to ${formatCount(planLimits.free.clips)} clips. Link the others later from Settings and any of them will open the same shelf.`,
  continueWith: (provider: string) => `Continue with ${provider}`,
  accountDeleted:
    "Your account has been deleted. Its clips are being removed from storage now.",
  suspended: (contact: ReactNode) => (
    <>
      This account has been suspended for breaking our terms of service. If you
      think that's a mistake, email {contact}.
    </>
  ),
  authFailed: "The provider didn't complete the sign-in. Try again.",
  agreeToTerms: (terms: ReactNode, privacy: ReactNode) => (
    <>
      By continuing you agree to the {terms} and {privacy}.
    </>
  ),
  termsLink: "terms of service",
  privacyLink: "privacy policy",
} as const;
