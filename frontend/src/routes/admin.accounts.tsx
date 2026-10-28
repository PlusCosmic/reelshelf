import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useDeferredValue, useState, type FormEvent } from "react";
import { adminCopy } from "@/copy/admin";
import { commonCopy } from "@/copy/common";
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

const copy = adminCopy.accounts;
const roleNames = copy.roles;

const noGrant = "none";

const accountStates = [
  { value: "active", label: copy.states.active },
  { value: "suspended", label: copy.states.suspended },
];

function AdminAccountsRoute() {
  const { data: user, isLoading } = useCurrentUser();

  if (isLoading)
    return <div className="rs-section rs-empty">{commonCopy.loading}</div>;
  if (!user?.isAdmin)
    return <div className="rs-section rs-empty">{copy.adminsOnly}</div>;
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
          <div className="rs-eyebrow">{copy.eyebrow}</div>
          <h1 className="rs-display rs-h2">{copy.title}</h1>
        </div>
      </header>

      <div className="rs-access-layout">
        <section className="rs-access-search">
          <Field label={copy.search.label}>
            <Input
              type="search"
              value={query}
              placeholder={copy.search.placeholder}
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
            <p className="rs-meta">{copy.pickAccount}</p>
          ) : selected.data ? (
            <AccountAccess
              key={selected.data.account.id}
              detail={selected.data}
              isSelf={selected.data.account.id === currentUserId}
            />
          ) : selected.isError ? (
            <p className="rs-legend-error">{copy.loadFailed}</p>
          ) : (
            <p className="rs-meta">{commonCopy.loading}</p>
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
  if (isError) return <p className="rs-legend-error">{copy.search.failed}</p>;
  if (!accounts) return <p className="rs-meta">{copy.search.searching}</p>;
  if (accounts.length === 0)
    return <p className="rs-meta">{copy.search.noMatch}</p>;

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
          {copy.granted(tierNames[account.grantedTier] ?? account.grantedTier)}
        </Badge>
      ) : null}
      {account.suspendedAt ? (
        <Badge tone="danger">{copy.suspendedBadge}</Badge>
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
  const [state, setState] = useState(
    account.suspendedAt ? "suspended" : "active",
  );
  const [reason, setReason] = useState("");
  const save = useSaveAccountAccess();

  const roleChanged = role !== account.role;
  const tierChanged = grantedTier !== (account.grantedTier ?? noGrant);
  const stateChanged = (state === "suspended") !== !!account.suspendedAt;
  const canSave =
    (roleChanged || tierChanged || stateChanged) &&
    reason.trim().length > 0 &&
    !save.isPending;

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
        suspended: stateChanged ? state === "suspended" : undefined,
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
          {copy.onTier(
            tierNames[detail.tier] ?? detail.tier,
            !!account.grantedTier,
          )}
        </p>
        {/* PostHog targets a named account by this id (docs/feature-flags.md). */}
        <p className="rs-meta">
          {copy.accountId} <code>{account.id}</code>
        </p>
        {account.suspendedAt ? (
          <p className="rs-legend-error">
            {copy.suspendedSince(formatDate(account.suspendedAt))}
          </p>
        ) : null}
      </div>

      <form className="rs-access-form" onSubmit={onSubmit}>
        <Field label={copy.form.role}>
          <Select
            aria-label={copy.form.role}
            value={role}
            disabled={isSelf}
            options={accountRoles.map((value) => ({
              value,
              label: roleNames[value],
            }))}
            onValueChange={setRole}
          />
        </Field>
        <Field label={copy.form.grantedPlan}>
          <Select
            aria-label={copy.form.grantedPlan}
            value={grantedTier}
            options={[
              { value: noGrant, label: copy.form.noGrant },
              ...grantableTiers.map((value) => ({
                value,
                label: tierNames[value] ?? value,
              })),
            ]}
            onValueChange={setGrantedTierValue}
          />
        </Field>
        <Field label={copy.form.account}>
          <Select
            aria-label={copy.form.account}
            value={state}
            disabled={
              isSelf || (account.role === "admin" && !account.suspendedAt)
            }
            options={accountStates}
            onValueChange={setState}
          />
        </Field>
        <Field label={copy.form.reason} className="rs-access-reason">
          <Input
            value={reason}
            maxLength={500}
            placeholder={copy.form.reasonPlaceholder}
            onChange={(event) => setReason(event.target.value)}
          />
        </Field>
        <div className="rs-meta">
          <Button variant="primary" type="submit" disabled={!canSave}>
            {save.isPending ? commonCopy.saving : commonCopy.save}
          </Button>
          {isSelf ? (
            <span>{copy.form.cantChangeSelf}</span>
          ) : account.role === "admin" ? (
            <span>{copy.form.cantSuspendAdmin}</span>
          ) : null}
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
    return <p className="rs-meta">{copy.changes.empty}</p>;

  return (
    <div className="rs-legend-usage">
      <table className="rs-legend-table">
        <thead>
          <tr>
            <th>{copy.changes.when}</th>
            <th>{copy.changes.change}</th>
            <th>{copy.changes.by}</th>
            <th>{copy.changes.reason}</th>
          </tr>
        </thead>
        <tbody>
          {changes.map((change) => (
            <tr key={change.id}>
              <td>{formatDate(change.changedAt)}</td>
              <td>
                {change.field === "suspended"
                  ? change.newValue
                    ? copy.changes.suspended
                    : copy.changes.suspensionLifted
                  : copy.changes.changed(
                      change.field === "role"
                        ? copy.changes.role
                        : copy.changes.grantedPlan,
                      describe(change.field, change.oldValue),
                      describe(change.field, change.newValue),
                    )}
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
  if (!value) return copy.changes.none;
  return field === "role"
    ? (roleNames[value] ?? value)
    : (tierNames[value] ?? value);
}
