-- Set when the "storage nearly full" notice has been sent; cleared once usage drops back under the threshold.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS storage_warned_at timestamptz;
