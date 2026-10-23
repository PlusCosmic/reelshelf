-- Going over the limit (ADR-0007): when an account's tier drops, the clips over its new limit are kept for 14 days,
-- then exported to zip files and deleted.

-- When the account last went over its tier's limit; cleared once it is back under. The grace counts from it.
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS over_limit_since timestamptz;

-- When the "a few days left" notice went out for the current grace, so it goes once.
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS over_limit_warned_at timestamptz;

-- The owner picked this clip to keep when over the limit. Picked clips are kept before the newest ones.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS kept_at timestamptz;

-- A zip of an account's clips: asked for by the owner, or made when a grace ends, before those clips are deleted.
-- Its parts live under exports/{user_id}/{id}/ in R2 until expires_at; the bucket's lifecycle rule deletes them too.
CREATE TABLE IF NOT EXISTS clip_export
(
    id           uuid PRIMARY KEY     DEFAULT gen_random_uuid(),
    user_id      uuid        NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    reason       text        NOT NULL CHECK (reason IN ('requested', 'grace_ended')),
    status       text        NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'running', 'ready', 'failed', 'expired')),
    clip_ids     uuid[]      NOT NULL,
    -- [{ "index": 1, "size_bytes": 123, "clip_count": 40 }, ...] once ready.
    parts        jsonb       NOT NULL DEFAULT '[]',
    attempts     integer     NOT NULL DEFAULT 0,
    error        text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    heartbeat_at timestamptz,
    completed_at timestamptz,
    expires_at   timestamptz
);

CREATE INDEX IF NOT EXISTS clip_export_user_idx
    ON clip_export (user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS clip_export_queue_idx
    ON clip_export (available_at)
    WHERE status IN ('pending', 'running');
