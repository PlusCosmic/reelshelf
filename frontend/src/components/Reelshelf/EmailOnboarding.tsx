import { EmailField } from "@/components/Reelshelf/EmailField";
import type { CurrentUser } from "@/shared/services/user";

/**
 * First-sign-in step: confirm where account mail should go. The address a provider reported is
 * prefilled; the user can change it, or skip and set one later in Settings.
 */
export function EmailOnboarding({ user }: { user: CurrentUser }) {
  return (
    <section className="rs-section rs-onboarding">
      <div className="rs-onboarding-card">
        <div className="rs-eyebrow">One more thing</div>
        <h1 className="rs-display rs-h1">
          Where should we <em>reach you</em>?
        </h1>
        <p className="rs-sidebar-copy">
          We email you when a new sign-in method is linked to your account and
          when your clip storage is nearly full. Nothing else.
        </p>
        <EmailField
          initialEmail={user.suggestedEmail}
          submitLabel="Use this address"
          allowSkip
        />
      </div>
    </section>
  );
}
