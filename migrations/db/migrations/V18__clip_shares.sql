CREATE TABLE IF NOT EXISTS clip_share (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    token text NOT NULL UNIQUE,
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    owner_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    revoked_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_clip_share_active_clip
    ON clip_share (clip_id)
    WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_clip_share_owner
    ON clip_share (owner_id);
