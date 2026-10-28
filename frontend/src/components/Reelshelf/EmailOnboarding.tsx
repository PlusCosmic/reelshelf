import { EmailField } from "@/components/Reelshelf/EmailField";
import type { CurrentUser } from "@/shared/services/user";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.onboarding;

/**
 * First-sign-in step: confirm where account mail should go. The address a provider reported is
 * prefilled; the user can change it, or skip and set one later in Settings.
 */
export function EmailOnboarding({ user }: { user: CurrentUser }) {
  return (
    <section className="rs-section rs-onboarding">
      <div className="rs-onboarding-card">
        <div className="rs-eyebrow">{copy.eyebrow}</div>
        <h1 className="rs-display rs-h1">{copy.title}</h1>
        <p className="rs-sidebar-copy">{copy.copy}</p>
        <EmailField
          initialEmail={user.suggestedEmail}
          submitLabel={copy.submit}
          allowSkip
        />
      </div>
    </section>
  );
}
