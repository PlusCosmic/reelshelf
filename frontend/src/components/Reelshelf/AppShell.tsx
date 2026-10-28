import { Link, Outlet, useRouterState } from "@tanstack/react-router";
import {
  IconFlag,
  IconLogout,
  IconMoon,
  IconScan,
  IconMicrophone,
  IconSettings,
  IconSun,
  IconUserShield,
} from "@tabler/icons-react";
import { useState } from "react";
import { UploadsProvider } from "@/components/AddClips/UploadsProvider";
import { UploadTray } from "@/components/AddClips/UploadTray";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { Avatar } from "@/components/Reelshelf/ReelshelfPrimitives";
import { EmailOnboarding } from "@/components/Reelshelf/EmailOnboarding";
import { PlanMeter } from "@/components/Reelshelf/PlanMeter";
import { shellCopy } from "@/copy/shell";
import { useLogout, usePlan } from "@/hooks/queries";
import type { CurrentUser } from "@/shared/services/user";

// Sticky bars further down the page sit beneath the topbar, whose height changes with the layout.
function trackTopbarHeight(topbar: HTMLElement | null) {
  if (!topbar) return;
  const observer = new ResizeObserver(() => {
    document.documentElement.style.setProperty(
      "--rs-topbar-height",
      `${topbar.offsetHeight}px`,
    );
  });
  observer.observe(topbar);
  return () => observer.disconnect();
}

/**
 * The signed-in app's chrome. It is loaded lazily from the root route so the landing page, which is
 * all a signed-out visitor sees, doesn't wait on the upload machinery and the rest of the library.
 */
export function AppShell({
  user,
  theme,
  onToggleTheme,
}: {
  user: CurrentUser;
  theme: "light" | "dark";
  onToggleTheme: () => void;
}) {
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const plan = usePlan(profileMenuOpen);
  const logout = useLogout();

  const active = (path: string) =>
    path === "/" ? pathname === "/" : pathname.startsWith(path);

  return (
    <div className="rs-app">
      <header className="rs-topbar" ref={trackTopbarHeight}>
        <Link to="/" className="rs-brand" aria-label={shellCopy.brandLink}>
          <BrandLogo />
        </Link>

        <nav className="rs-nav" aria-label={shellCopy.nav.label}>
          <Link to="/" className={active("/") ? "active" : undefined}>
            {shellCopy.nav.library}
          </Link>
          <Link
            to="/collections"
            className={active("/collections") ? "active" : undefined}
          >
            {shellCopy.nav.collections}
          </Link>
        </nav>

        <div className="rs-user">
          <div
            className="rs-profile-menu"
            onBlur={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget)) {
                setProfileMenuOpen(false);
              }
            }}
          >
            <button
              className="rs-profile-trigger"
              type="button"
              onClick={() => setProfileMenuOpen((open) => !open)}
              aria-label={shellCopy.profileMenu.open}
              aria-haspopup="menu"
              aria-expanded={profileMenuOpen}
            >
              <Avatar
                name={user.globalName ?? user.username}
                src={user.avatar}
              />
            </button>
            {profileMenuOpen ? (
              <div className="rs-profile-popover" role="menu">
                {plan.data ? (
                  <div className="rs-menu-storage">
                    <PlanMeter plan={plan.data} compact />
                  </div>
                ) : null}
                <Link
                  to="/settings"
                  className="rs-menu-item"
                  role="menuitem"
                  onClick={() => setProfileMenuOpen(false)}
                >
                  <IconSettings size={16} />
                  {shellCopy.profileMenu.settings}
                </Link>
                {user.isAdmin ? (
                  <Link
                    to="/legend-detection"
                    className="rs-menu-item"
                    role="menuitem"
                    onClick={() => setProfileMenuOpen(false)}
                  >
                    <IconScan size={16} />
                    {shellCopy.profileMenu.legendDetection}
                  </Link>
                ) : null}
                {user.isAdmin ? (
                  <Link
                    to="/clip-transcription"
                    className="rs-menu-item"
                    role="menuitem"
                    onClick={() => setProfileMenuOpen(false)}
                  >
                    <IconMicrophone size={16} />
                    {shellCopy.profileMenu.transcription}
                  </Link>
                ) : null}
                {user.isAdmin ? (
                  <Link
                    to="/admin/accounts"
                    className="rs-menu-item"
                    role="menuitem"
                    onClick={() => setProfileMenuOpen(false)}
                  >
                    <IconUserShield size={16} />
                    {shellCopy.profileMenu.accounts}
                  </Link>
                ) : null}
                {user.isAdmin ? (
                  <Link
                    to="/admin/reports"
                    className="rs-menu-item"
                    role="menuitem"
                    onClick={() => setProfileMenuOpen(false)}
                  >
                    <IconFlag size={16} />
                    {shellCopy.profileMenu.reports}
                  </Link>
                ) : null}
                <button
                  className="rs-menu-item"
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    onToggleTheme();
                    setProfileMenuOpen(false);
                  }}
                >
                  {theme === "dark" ? (
                    <IconSun size={16} />
                  ) : (
                    <IconMoon size={16} />
                  )}
                  {shellCopy.profileMenu.theme(theme)}
                </button>
                <button
                  className="rs-menu-item"
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setProfileMenuOpen(false);
                    logout.mutate();
                  }}
                >
                  <IconLogout size={16} />
                  {shellCopy.profileMenu.signOut}
                </button>
              </div>
            ) : null}
          </div>
        </div>
      </header>
      <UploadsProvider>
        <main>
          {user.needsOnboarding ? <EmailOnboarding user={user} /> : <Outlet />}
        </main>
        <UploadTray />
      </UploadsProvider>
    </div>
  );
}
