-- Each clip an account created in the last 30 days, for its plan's new-clips limit (ADR-0007). Deleting the clip
-- keeps its row, since every upload costs an encode; rows older than 30 days are deleted when the owner next creates
-- a clip. A reservation given back before any video existed deletes its own row.
CREATE TABLE IF NOT EXISTS clip_creation
(
    owner_id   uuid        NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    clip_id    uuid        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS clip_creation_owner_idx
    ON clip_creation (owner_id, created_at);

CREATE INDEX IF NOT EXISTS clip_creation_clip_idx
    ON clip_creation (clip_id);
