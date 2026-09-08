import {
  Link,
  Outlet,
  createRootRoute,
  useRouterState,
} from "@tanstack/react-router";
import {
  IconLogout,
  IconMoon,
  IconSettings,
  IconSun,
} from "@tabler/icons-react";
import { useEffect, useState, type Dispatch, type SetStateAction } from "react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { Avatar } from "@/components/Reelshelf/ReelshelfPrimitives";
import { EmailOnboarding } from "@/components/Reelshelf/EmailOnboarding";
import { LandingPage } from "@/components/Reelshelf/LandingPage";
import { StorageMeter } from "@/components/Reelshelf/StorageMeter";
import { useCurrentUser, useLogout, useStorageUsage } from "@/hooks/queries";

type ReelshelfTheme = "light" | "dark";

function getInitialTheme(): ReelshelfTheme {
  const stored = window.localStorage.getItem("reelshelf-theme");
  if (stored === "light" || stored === "dark") return stored;
  return window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

function RootComponent() {
  const [theme, setTheme] = useState<ReelshelfTheme>(getInitialTheme);
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem("reelshelf-theme", theme);
  }, [theme]);

  if (pathname.startsWith("/share/") || pathname === "/sign-in") {
    return <PublicShell theme={theme} setTheme={setTheme} />;
  }

  return <AuthenticatedShell theme={theme} setTheme={setTheme} />;
}

function AuthenticatedShell({
  theme,
  setTheme,
}: {
  theme: ReelshelfTheme;
  setTheme: Dispatch<SetStateAction<ReelshelfTheme>>;
}) {
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const { data: user, isLoading, isError } = useCurrentUser();
  const storage = useStorageUsage(!!user && profileMenuOpen);
  const logout = useLogout();

  const active = (path: string) =>
    path === "/" ? pathname === "/" : pathname.startsWith(path);

  if (isLoading) {
    // The landing page is a full marketing page now, so don't flash it at a signed-in user
    // while their session is still being checked.
    return <div className="rs-landing rs-landing-pending" aria-busy="true" />;
  }

  if (!user || isError) {
    return (
      <LandingPage
        theme={theme}
        onToggleTheme={() =>
          setTheme((value) => (value === "dark" ? "light" : "dark"))
        }
      />
    );
  }

  return (
    <div className="rs-app">
      <header className="rs-topbar">
        <Link to="/" className="rs-brand" aria-label="Reelshelf library">
          <BrandLogo />
        </Link>

        <nav className="rs-nav" aria-label="Primary">
          <Link to="/" className={active("/") ? "active" : undefined}>
            Library
          </Link>
          <Link
            to="/playlists"
            className={active("/playlists") ? "active" : undefined}
          >
            Collections
          </Link>
          <Link
            to="/upload"
            className={active("/upload") ? "active" : undefined}
          >
            Upload
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
                {storage.data ? (
                  <div className="rs-menu-storage">
                    <StorageMeter usage={storage.data} compact />
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
                <button
                  className="rs-menu-item"
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setTheme((value) => (value === "dark" ? "light" : "dark"));
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
      <main>
        {user.needsOnboarding ? <EmailOnboarding user={user} /> : <Outlet />}
      </main>
    </div>
  );
}

function PublicShell({
  theme,
  setTheme,
}: {
  theme: ReelshelfTheme;
  setTheme: Dispatch<SetStateAction<ReelshelfTheme>>;
}) {
  const nextTheme = theme === "dark" ? "light" : "dark";

  return (
    <div className="rs-public-app">
      <button
        className="rs-icon-button rs-public-theme-toggle"
        type="button"
        onClick={() => setTheme(nextTheme)}
        aria-label={`Switch to ${nextTheme} mode`}
        title={`Switch to ${nextTheme} mode`}
      >
        {theme === "dark" ? <IconSun size={16} /> : <IconMoon size={16} />}
      </button>
      <Outlet />
    </div>
  );
}

export const Route = createRootRoute({
  component: RootComponent,
});
