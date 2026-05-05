import { createFileRoute } from "@tanstack/react-router";
import { useCurrentUser } from "@/hooks/queries";

export const Route = createFileRoute("/settings")({
  component: SettingsRoute,
});

function SettingsRoute() {
  const { data: user } = useCurrentUser();

  return (
    <>
      <section className="rs-hero">
        <div className="rs-eyebrow">Settings</div>
        <h1 className="rs-display rs-h1">
          Archive preferences for <em>{user?.globalName ?? user?.username ?? "your account"}</em>.
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
          <h2 className="rs-eyebrow">Preference storage</h2>
          <p style={{ color: "var(--fg-soft)", fontSize: 14, marginTop: 0 }}>
            The UI design includes theme and density preferences. The backend preference API can store simple settings, but this migration leaves
            these controls presentational until product defaults are confirmed.
          </p>
        </aside>
      </section>
    </>
  );
}
