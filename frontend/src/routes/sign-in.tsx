import { Link, Navigate, createFileRoute } from "@tanstack/react-router";
import { IconChevronLeft } from "@tabler/icons-react";
import { BrandLogo } from "@/components/Reelshelf/BrandLogo";
import { ProviderIcon } from "@/components/Reelshelf/ProviderIcon";
import { useCurrentUser } from "@/hooks/queries";
import { authProviders, startLogin } from "@/shared/services/auth";

type SignInSearch = {
  auth_error?: string;
};

export const Route = createFileRoute("/sign-in")({
  component: SignInRoute,
  validateSearch: (search: Record<string, unknown>): SignInSearch => ({
    auth_error:
      typeof search.auth_error === "string" ? search.auth_error : undefined,
  }),
});

function SignInRoute() {
  const { data: user, isLoading } = useCurrentUser();
  const search = Route.useSearch();

  // A successful provider round-trip lands back here; send the signed-in user on to their shelf.
  if (user) {
    return <Navigate to="/" replace />;
  }

  return (
    <main className="rs-public-page rs-signin-page">
      <header className="rs-public-header">
        <Link to="/" className="rs-brand" aria-label="Reelshelf">
          <BrandLogo />
        </Link>
      </header>

      <section className="rs-signin-card" aria-busy={isLoading}>
        <div>
          <span className="rs-eyebrow">Sign in or create an account</span>
          <h1 className="rs-display rs-signin-title">
            Open your <em>shelf</em>.
          </h1>
          <p className="rs-signin-copy">
            Continue with any provider below. If you're new, this creates your
            account. It's free, with 25 GB of clip storage. Link the others
            later from Settings and any of them will open the same shelf.
          </p>
        </div>

        <div className="rs-signin-providers">
          {authProviders.map((provider) => (
            <button
              key={provider.id}
              className="rs-provider-login rs-signin-provider"
              data-provider={provider.id}
              type="button"
              onClick={() =>
                startLogin(provider.id, `${window.location.origin}/sign-in`)
              }
            >
              <ProviderIcon provider={provider.id} size={22} />
              Continue with {provider.label}
            </button>
          ))}
        </div>

        {search.auth_error ? (
          <p className="rs-landing-error rs-signin-error" role="alert">
            The provider didn't complete the sign-in. Try again.
          </p>
        ) : null}

        <Link to="/" className="rs-signin-back">
          <IconChevronLeft size={14} aria-hidden="true" />
          Back to the front page
        </Link>
      </section>
    </main>
  );
}
