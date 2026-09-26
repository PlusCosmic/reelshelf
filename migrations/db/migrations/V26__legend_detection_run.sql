-- Apex legend detection: each row is one attempt to identify the legends and player names in a clip by sending
-- its Bunny thumbnails to a vision model. Runs are kept rather than overwritten so different providers, models
-- and prompt versions can be compared on the same clips; a clip's detected legend is its latest succeeded run.
CREATE TABLE IF NOT EXISTS legend_detection_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    -- 'auto' when queued by encoding finishing or the backfill, 'manual' when an admin asked for a specific run.
    trigger text NOT NULL,
    provider text NOT NULL,
    model text NOT NULL,
    -- Hash of the prompt, response schema and reference sheet actually sent; set when the run is processed.
    prompt_version text,
    -- pending -> running -> succeeded | failed. A failed attempt returns to pending until attempts run out.
    status text NOT NULL DEFAULT 'pending',
    attempts int NOT NULL DEFAULT 0,
    frame_count int,
    hud_detected boolean,
    player_legend text,
    player_legend_confidence real,
    player_name text,
    player_name_confidence real,
    teammates jsonb,
    raw_response text,
    input_tokens int,
    output_tokens int,
    duration_ms int,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz
);

-- A clip is detected automatically at most once; repeated encoding webhooks and backfills are no-ops.
CREATE UNIQUE INDEX IF NOT EXISTS legend_detection_run_auto_clip_key
    ON legend_detection_run (clip_id)
    WHERE trigger = 'auto';

CREATE INDEX IF NOT EXISTS legend_detection_run_queue_idx
    ON legend_detection_run (created_at)
    WHERE status IN ('pending', 'running');

CREATE INDEX IF NOT EXISTS legend_detection_run_clip_idx
    ON legend_detection_run (clip_id, completed_at DESC);
