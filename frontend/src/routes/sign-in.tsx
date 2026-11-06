import {
  Link,
  Navigate,
  createFileRoute,
  useRouter,
} from "@tanstack/react-router";
import { useEffect } from "react";
import { IconChevronLeft } from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { LegalContact } from "@/components/Reelshelf/LegalPage";
import { ProviderIcon } from "@/components/Reelshelf/ProviderIcon";
import { commonCopy } from "@/copy/common";
import { signInCopy } from "@/copy/signIn";
import { useCurrentUser } from "@/hooks/queries";
import { authProviders, startLogin } from "@/shared/services/auth";

type SignInSearch = {
  auth_error?: string;
  account_deleted?: boolean;
  /** Where to go once signed in: a path on this site, such as /device?code=... */
  next?: string;
};

/** Only a path on this site, never another origin ("//evil.example" or "/\\evil.example"). */
function safeNextPath(next: unknown): string | undefined {
  return typeof next === "string" &&
    next.startsWith("/") &&
    !next.startsWith("//") &&
    !next.startsWith("/\\")
    ? next
    : undefined;
}

export const Route = createFileRoute("/sign-in")({
  component: SignInRoute,
  validateSearch: (search: Record<string, unknown>): SignInSearch => ({
    auth_error:
      typeof search.auth_error === "string" ? search.auth_error : undefined,
    account_deleted: search.account_deleted === true ? true : undefined,
    next: safeNextPath(search.next),
  }),
});

function SignInRoute() {
  const { data: user, isLoading } = useCurrentUser();
  const search = Route.useSearch();

  // A successful provider round-trip lands back here; send the signed-in user on to their shelf, or back to the
  // page that sent them to sign in.
  if (user) {
    return search.next ? (
      <ReturnTo path={search.next} />
    ) : (
      <Navigate to="/" replace />
    );
  }

  const returnUrl = `${window.location.origin}/sign-in${
    search.next ? `?next=${encodeURIComponent(search.next)}` : ""
  }`;

  return (
    <main className="rs-public-page rs-signin-page">
      <header className="rs-public-header">
        <Link to="/" className="rs-brand" aria-label={commonCopy.brand}>
          <BrandLogo />
        </Link>
      </header>

      <section className="rs-signin-card" aria-busy={isLoading}>
        <div>
          <span className="rs-eyebrow">{signInCopy.eyebrow}</span>
          <h1 className="rs-display rs-signin-title">{signInCopy.title}</h1>
          <p className="rs-signin-copy">{signInCopy.intro}</p>
        </div>

        <div className="rs-signin-providers">
          {authProviders.map((provider) => (
            <button
              key={provider.id}
              className="rs-provider-login rs-signin-provider"
              data-provider={provider.id}
              type="button"
              onClick={() => startLogin(provider.id, returnUrl)}
            >
              <ProviderIcon provider={provider.id} size={22} />
              {signInCopy.continueWith(provider.label)}
            </button>
          ))}
        </div>

        {search.account_deleted ? (
          <p className="rs-signin-copy" role="status">
            {signInCopy.accountDeleted}
          </p>
        ) : null}

        {search.auth_error === "suspended" ? (
          <p className="rs-landing-error rs-signin-error" role="alert">
            {signInCopy.suspended(<LegalContact />)}
          </p>
        ) : search.auth_error ? (
          <p className="rs-landing-error rs-signin-error" role="alert">
            {signInCopy.authFailed}
          </p>
        ) : null}

        <p className="rs-signin-legal">
          {signInCopy.agreeToTerms(
            <Link to="/terms">{signInCopy.termsLink}</Link>,
            <Link to="/privacy">{signInCopy.privacyLink}</Link>,
          )}
        </p>

        <Link to="/" className="rs-signin-back">
          <IconChevronLeft size={14} aria-hidden="true" />
          {commonCopy.backToFrontPage}
        </Link>
      </section>
    </main>
  );
}

/** Back to the page that sent the visitor to sign in, replacing this entry so back doesn't return here. */
function ReturnTo({ path }: { path: string }) {
  const router = useRouter();
  useEffect(() => {
    router.history.replace(path);
  }, [router, path]);
  return null;
}
