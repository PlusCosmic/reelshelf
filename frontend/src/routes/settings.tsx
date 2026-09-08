import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect } from "react";
import {
  LinkedAccounts,
  describeLinkResult,
} from "@/components/Reelshelf/LinkedAccounts";
import { EmailField } from "@/components/Reelshelf/EmailField";
import { useCurrentUser } from "@/hooks/queries";

type SettingsSearch = {
  linked?: string;
  link_error?: string;
};

export const Route = createFileRoute("/settings")({
  component: SettingsRoute,
  validateSearch: (search: Record<string, unknown>): SettingsSearch => ({
    linked: typeof search.linked === "string" ? search.linked : undefined,
    link_error:
      typeof search.link_error === "string" ? search.link_error : undefined,
  }),
});

function SettingsRoute() {
  const { data: user } = useCurrentUser();
  const search = Route.useSearch();
  const navigate = useNavigate();
  const notice = describeLinkResult(search);

  useEffect(() => {
    // The link result arrives as query parameters; drop them once shown so a refresh doesn't repeat it.
    if (search.linked || search.link_error) {
      void navigate({ to: "/settings", search: {}, replace: true });
    }
  }, [navigate, search.linked, search.link_error]);

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">Settings</div>
        <h1 className="rs-display rs-h1">
          Archive preferences for{" "}
          <em>{user?.globalName ?? user?.username ?? "your account"}</em>.
        </h1>
      </section>
      <section className="rs-section rs-split">
        <div className="rs-settings-main">
          <h2 className="rs-eyebrow">Account email</h2>
          <p className="rs-sidebar-copy">
            Used for notices about newly linked sign-in methods and when your
            storage is nearly full. Leave it empty to receive nothing.
          </p>
          <EmailField initialEmail={user?.email ?? null} />
        </div>
        <aside className="rs-sidebar-panel">
          <h2 className="rs-eyebrow">Linked accounts</h2>
          <p className="rs-sidebar-copy">
            Sign in with any linked account. Your name and avatar follow the
            primary one.
          </p>
          <LinkedAccounts notice={notice} />
        </aside>
      </section>
    </>
  );
}
