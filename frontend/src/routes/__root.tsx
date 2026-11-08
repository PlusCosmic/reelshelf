import {
  Navigate,
  Outlet,
  createRootRoute,
  useRouterState,
} from "@tanstack/react-router";
import { IconMoon, IconSun } from "@tabler/icons-react";
import { Suspense, lazy, useEffect } from "react";
import { commonCopy } from "@/copy/common";
import { shellCopy } from "@/copy/shell";
import { LandingPage } from "@/components/Reelshelf/LandingPage";
import { useCurrentUser } from "@/hooks/auth.queries";
import {
  AppearanceProvider,
  useAppearance,
} from "@/shared/theme/AppearanceProvider";
import { type Theme, defaultColorScheme } from "@/shared/theme/appearance";

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
  return (
    <AppearanceProvider>
      <RootShell />
    </AppearanceProvider>
  );
}

function RootShell() {
  const { theme, toggleTheme } = useAppearance();
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });

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
  theme: Theme;
  onToggleTheme: () => void;
}) {
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });
  const searchStr = useRouterState({
    select: (state) => state.location.searchStr,
  });
  const { data: user, isLoading, isError, refetch } = useCurrentUser();
  // Only the API saying there is no session counts as signed out (fetchMe answers null on a 401); a failed check
  // keeps the hint, the address and a session already loaded.
  const signedIn = !!user;
  const signedOut = !isLoading && !isError && !user;

  useEffect(() => {
    if (signedIn) rememberSignedIn(true);
    else if (signedOut) rememberSignedIn(false);
  }, [signedIn, signedOut]);

  // The colour scheme is the account's: take it up when the session loads (and when it changes from another
  // browser), and go back to the default once signed out, so the front page is always Stacks.
  const { setColorScheme } = useAppearance();
  const accountColorScheme = user?.colorScheme;
  useEffect(() => {
    if (accountColorScheme) setColorScheme(accountColorScheme);
    else if (signedOut) setColorScheme(defaultColorScheme);
  }, [accountColorScheme, signedOut, setColorScheme]);

  if (isLoading) {
    // Someone who has never signed in on this browser gets the front page straight away, rather
    // than a blank page while their session is checked. Anyone who has signed in before waits, so
    // they aren't shown the marketing page on the way to their library.
    if (pathname === "/" && !wasSignedIn()) {
      return <LandingPage theme={theme} onToggleTheme={onToggleTheme} />;
    }
    return <ShellPending />;
  }

  if (!signedIn && isError) {
    return <ShellUnavailable onRetry={() => void refetch()} />;
  }

  if (!signedIn) {
    // Approving a device sign-in starts from a link the uploader opened, so sign in and come back to it.
    if (pathname === "/device") {
      return (
        <Navigate
          to="/sign-in"
          search={{ next: `${pathname}${searchStr}` }}
          replace
        />
      );
    }
    // Signed out, every address is the front page; send deeper links there so the address bar
    // matches what is shown.
    if (pathname !== "/") return <Navigate to="/" replace />;
    return <LandingPage theme={theme} onToggleTheme={onToggleTheme} />;
  }

  return (
    <Suspense fallback={<ShellPending />}>
      <AppShell user={user} />
    </Suspense>
  );
}

function ShellPending() {
  return <div className="rs-landing rs-landing-pending" aria-busy="true" />;
}

function ShellUnavailable({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="rs-landing rs-landing-pending">
      <div className="rs-section rs-empty" role="alert">
        <p>{shellCopy.sessionUnavailable}</p>
        <button type="button" className="rs-primary" onClick={onRetry}>
          {commonCopy.tryAgain}
        </button>
      </div>
    </div>
  );
}

function PublicShell({
  theme,
  onToggleTheme,
}: {
  theme: Theme;
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
