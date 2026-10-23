import { Link, Outlet, useRouterState } from "@tanstack/react-router";
import {
  IconLogout,
  IconMoon,
  IconScan,
  IconMicrophone,
  IconSettings,
  IconSun,
} from "@tabler/icons-react";
import { useState } from "react";
import { UploadsProvider } from "@/components/AddClips/UploadsProvider";
import { UploadTray } from "@/components/AddClips/UploadTray";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { Avatar } from "@/components/Reelshelf/ReelshelfPrimitives";
import { EmailOnboarding } from "@/components/Reelshelf/EmailOnboarding";
import { PlanMeter } from "@/components/Reelshelf/PlanMeter";
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
        <Link to="/" className="rs-brand" aria-label="Reelshelf library">
          <BrandLogo />
        </Link>

        <nav className="rs-nav" aria-label="Primary">
          <Link to="/" className={active("/") ? "active" : undefined}>
            Library
          </Link>
          <Link
            to="/collections"
            className={active("/collections") ? "active" : undefined}
          >
            Collections
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
              aria-label="Open profile menu"
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
                  Settings
                </Link>
                {user.isAdmin ? (
                  <Link
                    to="/legend-detection"
                    className="rs-menu-item"
                    role="menuitem"
                    onClick={() => setProfileMenuOpen(false)}
                  >
                    <IconScan size={16} />
                    Legend detection
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
                    Transcription
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
                  {theme === "dark" ? "Light mode" : "Dark mode"}
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
                  Log out
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
