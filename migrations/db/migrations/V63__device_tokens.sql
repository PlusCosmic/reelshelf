-- Device sign-in for the desktop uploader (ADR-0010). A device asks for a sign-in (device_authorization), its owner
-- approves it on /device, and the device trades its device_code for a token (device_token). Only SHA-256 hashes of
-- the device_code and the token are kept. A request is deleted when its token is issued, and by the daily sweep once
-- it has expired; the sweep also revokes tokens unused for Devices:RevokeUnusedAfter.
CREATE TABLE IF NOT EXISTS device_authorization (
    id uuid PRIMARY KEY,
    device_code_hash bytea NOT NULL UNIQUE,
    -- 8 letters, stored without the dash.
    user_code text NOT NULL,
    device_name text NOT NULL CHECK (length(device_name) BETWEEN 1 AND 64),
    -- Set on approval.
    app_user_id uuid REFERENCES app_user(id) ON DELETE CASCADE,
    denied_at timestamptz,
    last_polled_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS device_authorization_user_code_idx ON device_authorization (user_code);

CREATE TABLE IF NOT EXISTS device_token (
    id uuid PRIMARY KEY,
    app_user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
    name text NOT NULL,
    token_hash bytea NOT NULL UNIQUE,
    -- The last X-Reelshelf-Client header seen.
    client_version text,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_used_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz
);
CREATE INDEX IF NOT EXISTS device_token_user_idx ON device_token (app_user_id) WHERE revoked_at IS NULL;
