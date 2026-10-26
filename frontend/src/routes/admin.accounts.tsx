import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useDeferredValue, useState, type FormEvent } from "react";
import { formatDate } from "@/components/Reelshelf/reelshelf-model";
import { Badge, Button, Field, Input, Select } from "@/components/ui";
import {
  useAccountSearch,
  useAdminAccount,
  useCurrentUser,
  useSaveAccountAccess,
} from "@/hooks/queries";
import {
  accountRoles,
  grantableTiers,
  type AccountRole,
  type AdminAccessChange,
  type AdminAccount,
  type AdminAccountDetail,
  type GrantableTier,
} from "@/shared/services/adminAccounts";
import { tierNames } from "@/shared/services/plans";

type AdminAccountsSearch = {
  account?: string;
};

export const Route = createFileRoute("/admin/accounts")({
  component: AdminAccountsRoute,
  validateSearch: (search: Record<string, unknown>): AdminAccountsSearch => ({
    account: typeof search.account === "string" ? search.account : undefined,
  }),
});

const roleNames: Record<string, string> = {
  viewer: "Viewer",
  editor: "Editor",
  admin: "Admin",
};

const noGrant = "none";

function AdminAccountsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading) return <div className="rs-section rs-empty">Loading…</div>;
  if (!user?.isAdmin)
    return (
      <div className="rs-section rs-empty">
        Accounts are only available to admins.
      </div>
    );
  return <AdminAccounts currentUserId={user.id} />;
}

function AdminAccounts({ currentUserId }: { currentUserId: string }) {
  const search = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const results = useAccountSearch(deferredQuery);
  const selected = useAdminAccount(search.account);

  const select = (account: AdminAccount) =>
    void navigate({ search: { account: account.id }, replace: true });

  return (
    <div className="rs-legend-page">
      <header className="rs-legend-header">
        <div>
          <div className="rs-eyebrow">Accounts</div>
          <h1 className="rs-display rs-h2">Roles and granted plans.</h1>
        </div>
      </header>

      <div className="rs-access-layout">
        <section className="rs-access-search">
          <Field label="Find an account">
            <Input
              type="search"
              value={query}
              placeholder="Username, display name or Discord/Twitch id"
              onChange={(event) => setQuery(event.target.value)}
              autoFocus
            />
          </Field>
          <AccountResults
            query={deferredQuery}
            accounts={results.data}
            isError={results.isError}
            selectedId={search.account}
            onSelect={select}
          />
        </section>

        <section className="rs-access-detail">
          {!search.account ? (
            <p className="rs-meta">
              Pick an account to see or change its role and granted plan.
            </p>
          ) : selected.data ? (
            <AccountAccess
              key={selected.data.account.id}
              detail={selected.data}
              isSelf={selected.data.account.id === currentUserId}
            />
          ) : selected.isError ? (
            <p className="rs-legend-error">That account couldn't be loaded.</p>
          ) : (
            <p className="rs-meta">Loading…</p>
          )}
        </section>
      </div>
    </div>
  );
}

function AccountResults({
  query,
  accounts,
  isError,
  selectedId,
  onSelect,
}: {
  query: string;
  accounts: AdminAccount[] | undefined;
  isError: boolean;
  selectedId: string | undefined;
  onSelect: (account: AdminAccount) => void;
}) {
  if (query.trim().length < 2) return null;
  if (isError)
    return <p className="rs-legend-error">The search didn't work.</p>;
  if (!accounts) return <p className="rs-meta">Searching…</p>;
  if (accounts.length === 0)
    return <p className="rs-meta">No account matches.</p>;

  return (
    <ul className="rs-access-results">
      {accounts.map((account) => (
        <li key={account.id}>
          <button
            type="button"
            className="rs-access-result"
            aria-current={account.id === selectedId || undefined}
            onClick={() => onSelect(account)}
          >
            <span className="rs-access-name">
              {account.globalName ?? account.username}
            </span>
            <span className="rs-meta">
              {account.identities
                .map((identity) => `${identity.provider}: ${identity.username}`)
                .join(" · ")}
            </span>
            <AccessBadges account={account} />
          </button>
        </li>
      ))}
    </ul>
  );
}

function AccessBadges({ account }: { account: AdminAccount }) {
  return (
    <span className="rs-meta">
      <Badge tone={account.role === "admin" ? "accent" : "neutral"}>
        {roleNames[account.role] ?? account.role}
      </Badge>
      {account.grantedTier ? (
        <Badge tone="accent">
          {tierNames[account.grantedTier] ?? account.grantedTier} granted
        </Badge>
      ) : null}
    </span>
  );
}

function AccountAccess({
  detail,
  isSelf,
}: {
  detail: AdminAccountDetail;
  isSelf: boolean;
}) {
  const { account } = detail;
  const [role, setRole] = useState(account.role);
  const [grantedTier, setGrantedTierValue] = useState(
    account.grantedTier ?? noGrant,
  );
  const [reason, setReason] = useState("");
  const save = useSaveAccountAccess();

  const roleChanged = role !== account.role;
  const tierChanged = grantedTier !== (account.grantedTier ?? noGrant);
  const canSave =
    (roleChanged || tierChanged) && reason.trim().length > 0 && !save.isPending;

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!canSave) return;
    save.mutate(
      {
        userId: account.id,
        role: roleChanged ? (role as AccountRole) : undefined,
        grantedTier: tierChanged
          ? grantedTier === noGrant
            ? null
            : (grantedTier as GrantableTier)
          : undefined,
        reason: reason.trim(),
      },
      { onSuccess: () => setReason("") },
    );
  }

  return (
    <div className="rs-access-account">
      <div>
        <h2 className="rs-access-title">
          {account.globalName ?? account.username}
        </h2>
        <p className="rs-meta">
          {account.identities.map((identity) => (
            <span key={`${identity.provider}:${identity.providerUserId}`}>
              {identity.provider}: {identity.username} (
              {identity.providerUserId})
            </span>
          ))}
        </p>
        <p className="rs-meta">
          On {tierNames[detail.tier] ?? detail.tier} now
          {account.grantedTier ? ", by a grant" : ""}.
        </p>
        {/* PostHog targets a named account by this id (docs/feature-flags.md). */}
        <p className="rs-meta">
          Account ID <code>{account.id}</code>
        </p>
      </div>

      <form className="rs-access-form" onSubmit={onSubmit}>
        <Field label="Role">
          <Select
            aria-label="Role"
            value={role}
            disabled={isSelf}
            options={accountRoles.map((value) => ({
              value,
              label: roleNames[value],
            }))}
            onValueChange={setRole}
          />
        </Field>
        <Field label="Granted plan">
          <Select
            aria-label="Granted plan"
            value={grantedTier}
            options={[
              { value: noGrant, label: "None" },
              ...grantableTiers.map((value) => ({
                value,
                label: tierNames[value] ?? value,
              })),
            ]}
            onValueChange={setGrantedTierValue}
          />
        </Field>
        <Field label="Reason" className="rs-access-reason">
          <Input
            value={reason}
            maxLength={500}
            placeholder="Why, for the record"
            onChange={(event) => setReason(event.target.value)}
          />
        </Field>
        <div className="rs-meta">
          <Button variant="primary" type="submit" disabled={!canSave}>
            {save.isPending ? "Saving…" : "Save"}
          </Button>
          {isSelf ? <span>You can't change your own role.</span> : null}
          {save.isError ? (
            <span className="rs-legend-error">{save.error.message}</span>
          ) : null}
        </div>
      </form>

      <ChangeLog changes={detail.changes} />
    </div>
  );
}

function ChangeLog({ changes }: { changes: AdminAccessChange[] }) {
  if (changes.length === 0)
    return <p className="rs-meta">No changes recorded.</p>;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th>When</th>
            <th>Change</th>
            <th>By</th>
            <th>Reason</th>
          </tr>
        </thead>
        <tbody>
          {changes.map((change) => (
            <tr key={change.id}>
              <td>{formatDate(change.changedAt)}</td>
              <td>
                {change.field === "role" ? "Role" : "Granted plan"}:{" "}
                {describe(change.field, change.oldValue)} →{" "}
                {describe(change.field, change.newValue)}
              </td>
              <td>{change.changedByName ?? "—"}</td>
              <td className="rs-access-change-reason">{change.reason}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function describe(field: string, value: string | null | undefined): string {
  if (!value) return "none";
  return field === "role"
    ? (roleNames[value] ?? value)
    : (tierNames[value] ?? value);
}
