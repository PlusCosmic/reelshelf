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
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.linkedAccounts;

export type LinkNotice =
  { tone: "success"; text: string } | { tone: "error"; text: string };

export function describeLinkResult(search: {
  linked?: string;
  link_error?: string;
}): LinkNotice | null {
  if (search.linked) {
    return {
      tone: "success",
      text: copy.linked(providerLabel(search.linked)),
    };
  }
  if (search.link_error) {
    return {
      tone: "error",
      text: copy.linkErrors[search.link_error] ?? copy.linkFailed,
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
          error instanceof ApiError ? error.message : copy.unlinkFailed,
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
        <p className="rs-sidebar-copy">{copy.loading}</p>
      ) : null}

      {identities.isError ? (
        <p className="rs-linked-notice" data-tone="error">
          {copy.loadFailed}
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
                <span>{identity.displayName ?? identity.username}</span>
                {identity.isPrimary ? (
                  <span className="rs-linked-primary">{copy.primary}</span>
                ) : null}
              </span>
              <span className="rs-linked-sub">
                <span>
                  {copy.handle(
                    providerLabel(identity.provider),
                    identity.username,
                  )}
                </span>
                {identity.email ? <span>{identity.email}</span> : null}
              </span>
            </span>
            <button
              className="rs-secondary"
              type="button"
              disabled={!canUnlink || unlink.isPending}
              title={canUnlink ? undefined : copy.cantUnlink}
              onClick={() => handleUnlink(identity.provider)}
            >
              {copy.unlink}
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
              {copy.link(provider.label)}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
