-- Roles and granted tiers live in the database (ADR-0008). app_user.role is the only source of an account's role;
-- app_user.granted_tier replaces whitelist.json's Unlimited. AccountAccessService is the only code that changes
-- either, and records each change here.
--
-- Nothing is dropped: the previous image still reads role_from_whitelist, user_additional_permission and its own
-- copy of whitelist.json while this deploys. A later migration drops them.

ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS granted_tier text
        CONSTRAINT app_user_granted_tier_check CHECK (granted_tier IN ('plus', 'pro', 'unlimited'));

CREATE TABLE IF NOT EXISTS account_access_change (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
    -- Null for a migration, or once the admin who made the change has deleted their account.
    changed_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
    field text NOT NULL CONSTRAINT account_access_change_field_check CHECK (field IN ('role', 'granted_tier')),
    old_value text,
    new_value text,
    reason text NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS account_access_change_user_idx ON account_access_change (user_id, changed_at DESC);

-- api/whitelist.json as it stands. Every entry is Unlimited; Role pins a role.
CREATE TEMPORARY TABLE whitelist_entry (provider text, provider_user_id text, role text);
INSERT INTO whitelist_entry VALUES
    ('discord', '201311542791241728', 'Admin'),
    ('discord', '620243863671537676', NULL),
    ('discord', '271671707008368650', 'Admin'),
    ('discord', '368506293914304535', NULL);

-- One row per matched account; an entry pinning a role wins over one that doesn't, as WhitelistService did.
CREATE TEMPORARY TABLE whitelisted_account AS
SELECT DISTINCT ON (identity.user_id) identity.user_id, entry.role
FROM whitelist_entry entry
JOIN user_identity identity
    ON identity.provider = entry.provider AND identity.provider_user_id = entry.provider_user_id
JOIN app_user account ON account.id = identity.user_id
ORDER BY identity.user_id, entry.role NULLS LAST;

INSERT INTO account_access_change (user_id, field, old_value, new_value, reason)
SELECT account.id, 'granted_tier', account.granted_tier, 'unlimited', 'whitelist.json'
FROM app_user account
JOIN whitelisted_account whitelisted ON whitelisted.user_id = account.id
WHERE account.granted_tier IS DISTINCT FROM 'unlimited';

UPDATE app_user account
SET granted_tier = 'unlimited'
FROM whitelisted_account whitelisted
WHERE whitelisted.user_id = account.id;

-- Pinned roles are recorded even when the middleware already wrote them, so every Admin's role has a source.
INSERT INTO account_access_change (user_id, field, old_value, new_value, reason)
SELECT account.id, 'role', account.role, whitelisted.role, 'whitelist.json'
FROM app_user account
JOIN whitelisted_account whitelisted ON whitelisted.user_id = account.id
WHERE whitelisted.role IS NOT NULL;

UPDATE app_user account
SET role = whitelisted.role
FROM whitelisted_account whitelisted
WHERE whitelisted.user_id = account.id AND whitelisted.role IS NOT NULL;

-- Extra permissions are no longer read: an account granted everything or the admin pages becomes an Admin, so
-- nobody loses access. Every other permission is already in the Editor role new accounts get.
INSERT INTO account_access_change (user_id, field, old_value, new_value, reason)
SELECT account.id, 'role', account.role, 'Admin', 'user_additional_permission'
FROM app_user account
WHERE account.role <> 'Admin'
  AND EXISTS (
      SELECT 1 FROM user_additional_permission permission
      WHERE permission.user_id = account.id AND permission.permission IN ('*', 'admin.users'));

UPDATE app_user account
SET role = 'Admin'
WHERE account.role <> 'Admin'
  AND EXISTS (
      SELECT 1 FROM user_additional_permission permission
      WHERE permission.user_id = account.id AND permission.permission IN ('*', 'admin.users'));

DROP TABLE whitelisted_account;
DROP TABLE whitelist_entry;
