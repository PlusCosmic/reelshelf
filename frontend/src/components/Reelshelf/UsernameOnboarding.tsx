import { UsernameField } from "@/components/Reelshelf/UsernameField";
import type { CurrentUser } from "@/shared/services/user";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.usernameOnboarding;

/**
 * First-sign-in step for a Google account, which has no username: keep the one made from the account's
 * name or pick another. The email step follows.
 */
export function UsernameOnboarding({ user }: { user: CurrentUser }) {
  return (
    <section className="rs-section rs-onboarding">
      <div className="rs-onboarding-card">
        <div className="rs-eyebrow">{copy.eyebrow}</div>
        <h1 className="rs-display rs-h1">{copy.title}</h1>
        <p className="rs-sidebar-copy">{copy.copy}</p>
        <UsernameField
          initialUsername={user.username}
          submitLabel={copy.submit}
        />
      </div>
    </section>
  );
}
