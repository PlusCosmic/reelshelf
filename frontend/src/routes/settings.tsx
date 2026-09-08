import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useEffect } from "react";
import {
  LinkedAccounts,
  describeLinkResult,
} from "@/components/Reelshelf/LinkedAccounts";
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
        <div className="rs-form">
          <div className="rs-field">
            <label htmlFor="default-view">Default library view</label>
            <select id="default-view" defaultValue="poster">
              <option value="poster">Poster shelf</option>
              <option value="grid">Compact grid</option>
              <option value="filmstrip">Filmstrip</option>
            </select>
          </div>
          <div className="rs-field">
            <label htmlFor="accent">Accent</label>
            <select id="accent" defaultValue="sage">
              <option value="sage">Sage archive</option>
              <option value="green">Tournament green</option>
              <option value="blue">Replay blue</option>
            </select>
          </div>
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
