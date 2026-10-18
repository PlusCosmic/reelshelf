-- Jobs for our own video pipeline (R2 + AV1, #208): each row is one job for a transcode worker. Workers never
-- connect to Postgres; they claim and report through the API's /api/worker/ endpoints, and the API applies the
-- results to the clip. See docs/video-pipeline/workers-and-jobs.md.
CREATE TABLE IF NOT EXISTS transcode_run (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    -- transcode: the uploaded source into a new AV1 video version; thumbnails: regenerate frames from an existing
    -- output; trim: shorten the current output into a new version; fallback: the on-demand H.264 file.
    type text NOT NULL,
    -- pending -> running -> succeeded | failed. A retryable failure returns to pending until attempts run out.
    status text NOT NULL DEFAULT 'pending',
    -- Lower is claimed first; ties go to the oldest job.
    priority int NOT NULL DEFAULT 0,
    -- The clip's video version the job reads from (or, for transcode and trim, produces), for later trims.
    video_version int NOT NULL DEFAULT 1,
    -- What the worker needs, e.g. {"source_key": "...", "trim_start_seconds": 1.5}.
    input jsonb NOT NULL DEFAULT '{}'::jsonb,
    -- What the worker reported on completion: {"outputs": {...}, "length_seconds": 31.2, "size_bytes": 1234}.
    output jsonb,
    attempts int NOT NULL DEFAULT 0,
    -- A pending job is not claimed before this; a retryable failure pushes it back by the retry delay.
    available_at timestamptz NOT NULL DEFAULT now(),
    -- Issued afresh on every claim. A worker's reports must carry it, so a worker whose job was reclaimed after
    -- going quiet cannot report over the worker that now holds it. Kept after the job ends so reports can be retried.
    lease_id uuid,
    -- The encode percentage from the latest progress report of the current attempt.
    progress int,
    -- When the worker first reported the thumbnails and detection frames uploaded.
    thumbnails_ready_at timestamptz,
    -- The latest claim or progress report; a running job quiet for longer than the stale timeout is reclaimed.
    heartbeat_at timestamptz,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    finished_at timestamptz,
    CONSTRAINT transcode_run_type_check CHECK (type IN ('transcode', 'thumbnails', 'trim', 'fallback')),
    CONSTRAINT transcode_run_status_check CHECK (status IN ('pending', 'running', 'succeeded', 'failed')),
    CONSTRAINT transcode_run_video_version_check CHECK (video_version >= 1),
    CONSTRAINT transcode_run_progress_check CHECK (progress BETWEEN 0 AND 100)
);

-- A clip has at most one transcode waiting or in progress; enqueueing another is a no-op.
CREATE UNIQUE INDEX IF NOT EXISTS transcode_run_active_transcode_key
    ON transcode_run (clip_id)
    WHERE type = 'transcode' AND status IN ('pending', 'running');

-- The claim: pending jobs by priority and age, and running jobs by their last report for the stale reclaim.
CREATE INDEX IF NOT EXISTS transcode_run_pending_idx
    ON transcode_run (priority, created_at)
    WHERE status = 'pending';

CREATE INDEX IF NOT EXISTS transcode_run_running_idx
    ON transcode_run (heartbeat_at)
    WHERE status = 'running';

CREATE INDEX IF NOT EXISTS transcode_run_clip_idx
    ON transcode_run (clip_id, created_at DESC);
