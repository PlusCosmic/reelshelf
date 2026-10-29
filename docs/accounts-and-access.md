# Accounts and access

What an account may do and keep is decided by two columns on `app_user` (ADR-0008):

- **`role`**: `Viewer`, `Editor` or `Admin`. New accounts get `Editor`. Each role's permissions are in `api/Auth/Permissions.cs`; endpoints check them with `RequirePermission`. Admins have every permission, including `admin.users`, which opens the review pages and `/admin/accounts`.
- **`granted_tier`**: `plus`, `pro`, `unlimited` or null. The account's tier is the better of this and its best open subscription (`AccountTierService`), so taking a grant away works like a subscription ending, the over-limit grace included.

Roles and tiers are independent: an Admin has no tier unless one is granted, and a granted tier gives no permissions.

## Changing access

Admins use **Accounts** in the profile menu (`/admin/accounts`): find someone by username, display name or their exact Discord, Twitch or Google id, then set their role or granted plan with a reason. The change takes effect on their next request.

`AccountAccessService` is the only code that writes either column, and each change is recorded in `account_access_change` (who, when, old and new value, reason). The change log is deleted with the account. An admin can't change their own role, and the last Admin can't be demoted.

## The first Admin of a fresh database

A new database (a new environment, or dev rebuilt from scratch) has no Admin, so nobody can open `/admin/accounts`. Sign in once, then promote that account by hand, recording why:

```sql
WITH target AS (
    SELECT user_id AS id FROM user_identity
    WHERE provider = 'discord' AND provider_user_id = '<your Discord id>'
),
change AS (
    INSERT INTO account_access_change (user_id, field, old_value, new_value, reason)
    SELECT app_user.id, 'role', app_user.role, 'Admin', 'first Admin, set by hand'
    FROM app_user JOIN target ON target.id = app_user.id
)
UPDATE app_user SET role = 'Admin' FROM target WHERE app_user.id = target.id;
```

Use `provider = 'twitch'` and the Twitch user id for a Twitch account. Dev's database is a copy of prod, so prod's Admins and grants are already there.

## History

Until V50, `api/whitelist.json` made listed identities Unlimited and could pin a role, and `user_additional_permission` could add permissions. V50 copied both across (every listed account that existed got `unlimited` and its pinned role; any account with `*` or `admin.users` became an Admin). `role_from_whitelist` and `user_additional_permission` are no longer read; a later migration drops them once no running image uses them.
