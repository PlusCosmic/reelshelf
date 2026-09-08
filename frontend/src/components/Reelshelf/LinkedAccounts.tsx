import { useState } from "react";
import { ProviderIcon } from "@/components/Reelshelf/ProviderIcon";
import { useLinkedIdentities, useUnlinkIdentity } from "@/hooks/queries";
import {
  authProviders,
  providerLabel,
  startLink,
  type AuthProvider,
} from "@/shared/services/auth";
import { ApiError } from "@/shared/services/apiError";

export type LinkNotice =
  | { tone: "success"; text: string }
  | { tone: "error"; text: string };

const linkErrorMessages: Record<string, string> = {
  session_expired:
    "Your session changed while linking. Sign in again and retry.",
  provider_already_linked:
    "This account already has a login from that provider. Unlink it first.",
  linked_to_another_account:
    "That login already belongs to a different Reelshelf account.",
  provider_failed: "The provider didn't complete the sign-in. Nothing changed.",
};

export function describeLinkResult(search: {
  linked?: string;
  link_error?: string;
}): LinkNotice | null {
  if (search.linked) {
    return {
      tone: "success",
      text: `${providerLabel(search.linked)} is now linked to your account.`,
    };
  }
  if (search.link_error) {
    return {
      tone: "error",
      text:
        linkErrorMessages[search.link_error] ??
        "Linking didn't complete. Nothing changed.",
    };
  }
  return null;
}

export function LinkedAccounts({ notice }: { notice: LinkNotice | null }) {
  const identities = useLinkedIdentities();
  const unlink = useUnlinkIdentity();
  const [unlinkError, setUnlinkError] = useState<string | null>(null);

  const linked = identities.data ?? [];
  const unlinked = authProviders.filter(
    (provider) => !linked.some((identity) => identity.provider === provider.id),
  );
  const canUnlink = linked.length > 1;

  const handleUnlink = (provider: string) => {
    setUnlinkError(null);
    unlink.mutate(provider, {
      onError: (error) => {
        setUnlinkError(
          error instanceof ApiError
            ? error.message
            : "Couldn't unlink that account.",
        );
      },
    });
  };

  const returnUrl = `${window.location.origin}/settings`;

  return (
    <div className="rs-linked-accounts">
      {notice ? (
        <p className="rs-linked-notice" data-tone={notice.tone} role="status">
          {notice.text}
        </p>
      ) : null}

      {identities.isLoading ? (
        <p className="rs-sidebar-copy">Loading linked accounts…</p>
      ) : null}

      {identities.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          Couldn't load linked accounts.
        </p>
      ) : null}

      <ul className="rs-linked-list">
        {linked.map((identity) => (
          <li key={identity.provider} className="rs-linked-row">
            <span
              className="rs-linked-provider"
              data-provider={identity.provider}
            >
              <ProviderIcon provider={identity.provider} />
            </span>
            <span className="rs-linked-meta">
              <span className="rs-linked-name">
                {identity.displayName ?? identity.username}
                {identity.isPrimary ? (
                  <span className="rs-linked-primary">primary</span>
                ) : null}
              </span>
              <span className="rs-linked-sub">
                {providerLabel(identity.provider)} · @{identity.username}
                {identity.email ? ` · ${identity.email}` : null}
              </span>
            </span>
            <button
              className="rs-secondary"
              type="button"
              disabled={!canUnlink || unlink.isPending}
              title={
                canUnlink
                  ? undefined
                  : "Link another sign-in method before removing this one"
              }
              onClick={() => handleUnlink(identity.provider)}
            >
              Unlink
            </button>
          </li>
        ))}
      </ul>

      {unlinkError ? (
        <p className="rs-linked-notice" data-tone="error">
          {unlinkError}
        </p>
      ) : null}

      {unlinked.length > 0 ? (
        <div className="rs-linked-actions">
          {unlinked.map((provider) => (
            <button
              key={provider.id}
              className="rs-provider-login rs-provider-login-compact"
              data-provider={provider.id}
              type="button"
              onClick={() => startLink(provider.id as AuthProvider, returnUrl)}
            >
              <ProviderIcon provider={provider.id} />
              Link {provider.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
