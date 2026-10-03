-- Clip summaries: each row is one attempt to turn a clip's transcript and metadata into a searchable description,
-- mood tags, quotes and names, with an embedding of the description for semantic search. Runs are kept rather than
-- overwritten so models and prompts can be compared on the same clips; a clip's summary is its latest succeeded
-- run (see ADR-0005).
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS clip_summary_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    -- The transcription run whose transcript is summarised.
    source_transcription_run_id uuid NOT NULL REFERENCES clip_transcription_run(id) ON DELETE CASCADE,
    -- 'auto' when queued by a succeeded automatic transcription or the backfill, 'retry' when a transcription
    -- retry changed the clip's transcript, 'manual' when an admin asked for a run.
    trigger text NOT NULL,
    provider text NOT NULL,
    model text NOT NULL,
    -- NULL leaves it to the model's default.
    reasoning_effort text,
    -- Hash of the prompt, response schema and request layout; set when the run is processed.
    prompt_version text,
    -- pending -> running -> succeeded | failed. A failed attempt returns to pending until attempts run out.
    status text NOT NULL DEFAULT 'pending',
    attempts int NOT NULL DEFAULT 0,
    description text,
    mood_tags text[],
    -- The quotes the model returned that appear in the transcript, as a JSON array of strings.
    quotes jsonb,
    people text[],
    -- An embedding of the description and mood tags. Exact scans are fast enough at the expected volume, so there
    -- is no ANN index yet.
    embedding vector(1536),
    embedding_model text NOT NULL,
    raw_response text,
    input_tokens int,
    cached_input_tokens int,
    output_tokens int,
    embedding_tokens int,
    duration_ms int,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz
);

-- A clip is summarised automatically at most once; repeated transcriptions and backfills are no-ops.
CREATE UNIQUE INDEX IF NOT EXISTS clip_summary_run_auto_clip_key
    ON clip_summary_run (clip_id)
    WHERE trigger = 'auto';

CREATE INDEX IF NOT EXISTS clip_summary_run_queue_idx
    ON clip_summary_run (created_at)
    WHERE status IN ('pending', 'running');

CREATE INDEX IF NOT EXISTS clip_summary_run_clip_idx
    ON clip_summary_run (clip_id, created_at DESC);
