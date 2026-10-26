# ADR-0008: Roles and granted tiers in the database

**Status:** Accepted (2026-10-26). Replaces `whitelist.json` from ADR-0002 and ADR-0007; their other decisions stand.

## Context

An account's access is decided in four places today:

- **`app_user.role`** (Viewer, Editor or Admin) is the role the permission filter checks. New accounts get Editor.
- **`whitelist.json`**, shipped inside the API image, gives every listed identity the Unlimited tier. Its optional `Role` overrides the stored role: `AuthenticatedUserMiddleware` writes it to `app_user.role` on the next request, with `role_from_whitelist = true`, and sets the role back to Editor once the entry is gone.
- **`user_additional_permission`** adds permission strings on top of the role. It is read on every request, but nothing in the app writes it (the unused writers went in #271).
- **`billing_subscription`** gives Plus or Pro through `AccountTierService`.

Because of this:

- Making someone Unlimited or an Admin means a commit, a PR, an image build and a deploy.
- The file holds identities, not accounts, so every request resolves the account's identities and matches them against it.
- The middleware writes to the database whenever a pinned role and the stored one disagree.
- Nothing records who granted what, or when.
- Unlimited is the only tier that can be given away, so a friend or tester can't be put on Plus or Pro without paying.

## Decision

### Two columns on the account decide access

- **`app_user.role` is the only source of an account's role.** Nothing overrides it. The values and their default permissions (`Permissions.cs`) are unchanged, and new accounts still get Editor.
- **`app_user.granted_tier`** (nullable: `plus`, `pro` or `unlimited`, with a check constraint) replaces the whitelist's Unlimited. `AccountTierService` returns the better of the granted tier and the best open subscription, else Free. A granted tier is never stored as a subscription and never touches Stripe.
- **Roles and tiers stay independent**, as ADR-0007 says. An Admin has no tier unless one is granted, and a granted tier gives no permissions.
- **`user_additional_permission` goes.** Two pieces of data, one role and one tier, cover everything the app grants. If a narrower grant is ever needed, the right fix is a new permission on a role, not per-account strings. The migration turns any existing `*` or `admin.users` row into the Admin role before the table is dropped, so nobody loses access.

### One service changes them, and every change is recorded

- **`AccountAccessService` (`api/Auth/`) is the only code that writes `role` or `granted_tier`.** It writes the new value and an `account_access_change` row in the same transaction. The row holds the account, who changed it (`changed_by`, null for a migration), which field changed, its old and new values, a free-text reason, and when.
- **The log cascades with the account** when an account is deleted, since it describes that account. `changed_by` becomes null when the admin's own account is deleted.
- **Admins manage access in the app.** Endpoints under `/api/admin/accounts` require `admin.users`:
  - find an account by username or by Discord or Twitch id
  - read its role, granted tier, current tier and change log
  - set its role
  - set or clear its granted tier, with a reason

  A frontend page, `/admin/accounts`, uses them and sits next to the other admin pages in the profile menu.

- **Admins can't lock the app out.** An admin can't change their own role, and demoting the last Admin is refused (409). Either rule can be worked around only by hand in SQL.
- **Losing a granted tier works like losing a subscription.** The over-limit sweep already reads `AccountTierService`, so clearing a granted tier starts the 14-day grace (ADR-0007), the same as removing a whitelist entry does today.

### Moving the whitelist across

- **One migration (`V50`)** does the following:
  - adds `granted_tier` and `account_access_change`
  - seeds both from the current `whitelist.json`: every listed Discord or Twitch id is joined to `user_identity`, and the matching account gets `granted_tier = 'unlimited'` and any pinned role, each with a change row whose reason is `whitelist.json`
  - turns `user_additional_permission` rows into roles, as above

  The ids are already committed in `api/whitelist.json`, so writing them into the migration exposes nothing new.

- **Whitelist entries with no account yet aren't carried across.** An admin grants them from `/admin/accounts` after they sign in. There are no pending grants keyed by identity, because they would need the matching on every sign-in that this ADR removes. The implementation PR lists any entry that matched nobody on dev.
- **Nothing is dropped in `V50`.** Migrations run before the new image does, and the old image still reads `role_from_whitelist`, `user_additional_permission` and its own copy of `whitelist.json`. While both images run, the old one may write a pinned role again, but it is the same role the migration seeded. A later migration (`V51`), shipped once the new image is out, drops `role_from_whitelist` and `user_additional_permission`.
- **`whitelist.json`, `WhitelistService` and `WhitelistEntry` are removed** in the same PR as `V50`, along with the middleware's role sync.

### Reading access

- **`AuthenticatedUserMiddleware` reads, and never writes.** It reads the user row, which now includes `role` and `granted_tier`, and the identities, and puts both on `AuthenticatedUser`. The extra-permission query goes, so a signed-in request makes two queries instead of three.
- **`AccountTierService.GetTier(user)` uses the granted tier it is handed.** `GetTier(userId)` and `FilterAtLeast` become SQL over `granted_tier` and `billing_subscription` together, instead of loading identities to match against the file.

## Consequences

- Granting or revoking Unlimited or Admin takes effect on the account's next request, with no deploy, and leaves a record.
- Plus and Pro can be given away (testers, friends, refunds made good) without Stripe.
- **Paying on top of a grant.** An account with a granted tier can still subscribe. Settings hides plan cards at or below the granted tier, and `GET /api/me/plan` says which tier was granted so the page can say "Unlimited (granted)" instead of offering upgrades. The Checkout endpoint doesn't refuse a lower plan, since paying for one does no harm and is the owner's choice.
- **Dev keeps its own admins.** Dev's database is a copy of prod (`scripts/dev-db/README.md`), so prod's grants come with it. A dev-only admin is set with one documented SQL statement, which also writes an `account_access_change` row with a reason.
- **Bootstrapping a fresh database** (tests, a new environment) takes the same SQL statement for the first Admin, because no account starts with permission to grant anything.
- **Privacy.** Nothing new is collected from users and nothing goes to a new third party. The change log is internal and is deleted with the account. The implementation PR checks the privacy policy for any mention of `whitelist.json` or how access is granted and updates it, with its "last updated" date, if needed.
- **`CONTEXT.md` changes with the implementation.** **Tier** describes Unlimited as granted rather than listed in `whitelist.json`, and a **Granted Tier** term is added. CLAUDE.md's whitelist bullet is rewritten to match.

## Alternatives considered

- **Keep the file and copy it into the database at startup.** This is still a deploy per change and gives two sources of truth. Rejected.
- **A generic grants table** (`account_grant(user_id, grant)` holding `unlimited`, `admin` and permission strings). It mixes tiers with permissions, which ADR-0007 keeps apart, and turns one role per account into a set the code would have to reconcile. Rejected while there are only two things to grant.
- **Grants keyed by identity, so someone can be granted before they sign up.** This keeps the per-request identity matching the change exists to remove, for a case an admin can handle a minute after the person signs in. Rejected.
