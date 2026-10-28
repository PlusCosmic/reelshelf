import {
  Navigate,
  Outlet,
  createRootRoute,
  useRouterState,
} from "@tanstack/react-router";
import { IconMoon, IconSun } from "@tabler/icons-react";
import { Suspense, lazy, useEffect, useState } from "react";
import { commonCopy } from "@/copy/common";
import { LandingPage } from "@/components/Reelshelf/LandingPage";
import { useCurrentUser } from "@/hooks/auth.queries";

type ReelshelfTheme = "light" | "dark";

// Only an explicit pick from a theme toggle is stored; until then the OS setting is followed.
// index.html reads the same key to apply the theme before first paint.
const themeStorageKey = "reelshelf-theme-preference";
const darkSchemeQuery = "(prefers-color-scheme: dark)";

function getStoredTheme(): ReelshelfTheme | null {
  const stored = window.localStorage.getItem(themeStorageKey);
  return stored === "light" || stored === "dark" ? stored : null;
}

function getSystemTheme(): ReelshelfTheme {
  return window.matchMedia(darkSchemeQuery).matches ? "dark" : "light";
}

// Set once this browser has seen a signed-in session, so a first visit can show the front page
// without waiting for the session check.
const signedInStorageKey = "reelshelf-signed-in";

function wasSignedIn() {
  try {
    return window.localStorage.getItem(signedInStorageKey) === "1";
  } catch {
    return false;
  }
}

function rememberSignedIn(signedIn: boolean) {
  try {
    if (signedIn) window.localStorage.setItem(signedInStorageKey, "1");
    else window.localStorage.removeItem(signedInStorageKey);
  } catch {
    // Storage may be unavailable (private mode); the session check still decides what is shown.
  }
}

// The signed-in app is its own chunk. Someone who was signed in last time starts fetching it now,
// alongside the session check, rather than once the check returns.
const loadAppShell = () => import("@/components/Reelshelf/AppShell");
const AppShell = lazy(() =>
  loadAppShell().then((module) => ({ default: module.AppShell })),
);
if (wasSignedIn()) void loadAppShell();

function RootComponent() {
  const [theme, setTheme] = useState<ReelshelfTheme>(
    () => getStoredTheme() ?? getSystemTheme(),
  );
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  useEffect(() => {
    const query = window.matchMedia(darkSchemeQuery);
    const followSystem = () => {
      if (!getStoredTheme()) setTheme(getSystemTheme());
    };
    query.addEventListener("change", followSystem);
    return () => query.removeEventListener("change", followSystem);
  }, []);

  const toggleTheme = () => {
    const next = theme === "dark" ? "light" : "dark";
    window.localStorage.setItem(themeStorageKey, next);
    setTheme(next);
  };

  if (
    pathname.startsWith("/share/") ||
    pathname === "/sign-in" ||
    pathname === "/terms" ||
    pathname === "/privacy"
  ) {
    return <PublicShell theme={theme} onToggleTheme={toggleTheme} />;
  }

  return <AuthenticatedShell theme={theme} onToggleTheme={toggleTheme} />;
}

function AuthenticatedShell({
  theme,
  onToggleTheme,
}: {
  theme: ReelshelfTheme;
  onToggleTheme: () => void;
}) {
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const { data: user, isLoading, isError } = useCurrentUser();
  const signedIn = !!user && !isError;

  useEffect(() => {
    if (!isLoading) rememberSignedIn(signedIn);
  }, [isLoading, signedIn]);

  if (isLoading) {
    // Someone who has never signed in on this browser gets the front page straight away, rather
    // than a blank page while their session is checked. Anyone who has signed in before waits, so
    // they aren't shown the marketing page on the way to their library.
    if (pathname === "/" && !wasSignedIn()) {
      return <LandingPage theme={theme} onToggleTheme={onToggleTheme} />;
    }
    return <ShellPending />;
  }

  if (!signedIn) {
    // Signed out, every address is the front page; send deeper links there so the address bar
    // matches what is shown.
    if (pathname !== "/") return <Navigate to="/" replace />;
    return <LandingPage theme={theme} onToggleTheme={onToggleTheme} />;
  }

  return (
    <Suspense fallback={<ShellPending />}>
      <AppShell user={user} theme={theme} onToggleTheme={onToggleTheme} />
    </Suspense>
  );
}

function ShellPending() {
  return <div className="rs-landing rs-landing-pending" aria-busy="true" />;
}

function PublicShell({
  theme,
  onToggleTheme,
}: {
  theme: ReelshelfTheme;
  onToggleTheme: () => void;
}) {
  const nextTheme = theme === "dark" ? "light" : "dark";

  return (
    <div className="rs-public-app">
      <button
        className="rs-icon-button rs-public-theme-toggle"
        type="button"
        onClick={onToggleTheme}
        aria-label={commonCopy.switchTheme(nextTheme)}
        title={commonCopy.switchTheme(nextTheme)}
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
