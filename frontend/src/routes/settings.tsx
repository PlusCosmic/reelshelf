import { Link, createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import {
  LinkedAccounts,
  describeLinkResult,
} from "@/components/Reelshelf/LinkedAccounts";
import { DeleteAccount } from "@/components/Reelshelf/DeleteAccount";
import { EmailField } from "@/components/Reelshelf/EmailField";
import {
  type BillingReturn,
  PlanSettings,
} from "@/components/Reelshelf/PlanSettings";
import { useCurrentUser } from "@/hooks/queries";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy;
const sections = settingsCopy.sections;

type SettingsSearch = {
  linked?: string;
  link_error?: string;
  billing?: BillingReturn;
};

export const Route = createFileRoute("/settings")({
  component: SettingsRoute,
  validateSearch: (search: Record<string, unknown>): SettingsSearch => ({
    linked: typeof search.linked === "string" ? search.linked : undefined,
    link_error:
      typeof search.link_error === "string" ? search.link_error : undefined,
    // Where Stripe Checkout and the Customer Portal send the browser back to.
    billing:
      search.billing === "success" ||
      search.billing === "cancelled" ||
      search.billing === "portal"
        ? search.billing
        : undefined,
  }),
});

function SettingsRoute() {
  const { data: user } = useCurrentUser();
  const search = Route.useSearch();
  const navigate = useNavigate();
  // The link result arrives as query parameters. Keep it in state so it survives cleaning the URL,
  // which happens right away so a refresh doesn't repeat the message.
  const [notice] = useState(() => describeLinkResult(search));
  const [billingReturn] = useState(() => search.billing);

  useEffect(() => {
    if (search.linked || search.link_error || search.billing) {
      void navigate({ to: "/settings", search: {}, replace: true });
    }
  }, [navigate, search.linked, search.link_error, search.billing]);

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">{copy.eyebrow}</div>
        <h1 className="rs-display rs-h1">
          {copy.title(user?.globalName ?? user?.username ?? copy.yourAccount)}
        </h1>
      </section>
      <section className="rs-section rs-settings">
        <div className="rs-settings-section">
          <h2 className="rs-eyebrow">{sections.plan}</h2>
          <PlanSettings billingReturn={billingReturn} />
        </div>
        <div className="rs-settings-section">
          <h2 className="rs-eyebrow">{sections.email.title}</h2>
          <p className="rs-sidebar-copy">{sections.email.copy}</p>
          <EmailField initialEmail={user?.email ?? null} />
        </div>
        <div className="rs-settings-section">
          <h2 className="rs-eyebrow">{sections.linkedAccounts.title}</h2>
          <p className="rs-sidebar-copy">{sections.linkedAccounts.copy}</p>
          <LinkedAccounts notice={notice} />
        </div>
        <div className="rs-settings-section">
          <h2 className="rs-eyebrow">{sections.yourData.title}</h2>
          <p className="rs-sidebar-copy">
            {sections.yourData.copy(
              <Link to="/privacy">{sections.yourData.privacyLink}</Link>,
              <Link to="/terms">{sections.yourData.termsLink}</Link>,
            )}
          </p>
        </div>
        <div className="rs-settings-section">
          <h2 className="rs-eyebrow">{sections.deleteAccount.title}</h2>
          <p className="rs-sidebar-copy">{sections.deleteAccount.copy}</p>
          <DeleteAccount />
        </div>
      </section>
    </>
  );
}
