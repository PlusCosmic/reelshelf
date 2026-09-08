-- Providers now report an email address; keep it per identity and mirror the primary identity's on the account.
ALTER TABLE user_identity ADD COLUMN IF NOT EXISTS email text;
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS email text;
