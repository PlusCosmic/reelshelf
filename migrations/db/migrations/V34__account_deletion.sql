-- Set when the owner deletes their account in Settings. From then on the account can't be signed in to or used,
-- and AccountDeletionBackgroundService removes its Bunny videos and collections and finally the row itself, whose
-- foreign keys cascade to everything else the account owns.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS deletion_requested_at timestamptz;

CREATE INDEX IF NOT EXISTS app_user_deletion_requested_idx
    ON app_user (deletion_requested_at)
    WHERE deletion_requested_at IS NOT NULL;
