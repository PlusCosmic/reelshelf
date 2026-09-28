-- Clip transcription: each row is one attempt to transcribe a clip's audio with a speech-to-text model. Runs are
-- kept rather than overwritten so models and prompts can be compared on the same clips; a clip's transcript is its
-- latest succeeded run. Only clips owned by whitelisted accounts are queued (see ADR-0005).
CREATE TABLE IF NOT EXISTS clip_transcription_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    -- 'auto' when queued by encoding finishing or the backfill, 'manual' when an admin asked for a run.
    trigger text NOT NULL,
    model text NOT NULL,
    -- Hash of the prompt template and keyword rules; set when the run is processed.
    prompt_version text,
    -- The prompt and keywords actually sent, which vary by game.
    prompt text,
    keywords text[],
    -- pending -> running -> succeeded | failed. A failed attempt returns to pending until attempts run out.
    status text NOT NULL DEFAULT 'pending',
    attempts int NOT NULL DEFAULT 0,
    -- False when the video has no audio track; the run then succeeds with an empty transcript and no model call.
    has_audio boolean,
    audio_seconds real,
    audio_bytes int,
    transcript text,
    languages text[],
    raw_response text,
    input_tokens int,
    output_tokens int,
    duration_ms int,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz
);

-- A clip is transcribed automatically at most once; repeated encoding webhooks and backfills are no-ops.
CREATE UNIQUE INDEX IF NOT EXISTS clip_transcription_run_auto_clip_key
    ON clip_transcription_run (clip_id)
    WHERE trigger = 'auto';

CREATE INDEX IF NOT EXISTS clip_transcription_run_queue_idx
    ON clip_transcription_run (created_at)
    WHERE status IN ('pending', 'running');

CREATE INDEX IF NOT EXISTS clip_transcription_run_clip_idx
    ON clip_transcription_run (clip_id, created_at DESC);
