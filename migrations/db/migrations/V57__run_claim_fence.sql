-- Fences the legend detection, transcription and summary queues' results to the claim that produced them (#319).
-- Each claim sets a fresh claim_id, and a processor's complete or fail writes only while the run is still running
-- under its claim, so a slow processor whose run was reclaimed after StaleAfter can't overwrite the reclaimer's
-- result, and a late failure can't move a finished run back to pending. NULL until a run is first claimed.
ALTER TABLE legend_detection_run ADD COLUMN IF NOT EXISTS claim_id uuid;
ALTER TABLE clip_transcription_run ADD COLUMN IF NOT EXISTS claim_id uuid;
ALTER TABLE clip_summary_run ADD COLUMN IF NOT EXISTS claim_id uuid;

-- pending -> running -> succeeded | failed, and nothing else. No code has ever written another status; anything
-- else is taken as failed rather than left to block the constraint.
UPDATE legend_detection_run SET status = 'failed', completed_at = COALESCE(completed_at, now())
WHERE status NOT IN ('pending', 'running', 'succeeded', 'failed');
UPDATE clip_transcription_run SET status = 'failed', completed_at = COALESCE(completed_at, now())
WHERE status NOT IN ('pending', 'running', 'succeeded', 'failed');
UPDATE clip_summary_run SET status = 'failed', completed_at = COALESCE(completed_at, now())
WHERE status NOT IN ('pending', 'running', 'succeeded', 'failed');

ALTER TABLE legend_detection_run DROP CONSTRAINT IF EXISTS legend_detection_run_status_check;
ALTER TABLE legend_detection_run ADD CONSTRAINT legend_detection_run_status_check
    CHECK (status IN ('pending', 'running', 'succeeded', 'failed'));
ALTER TABLE clip_transcription_run DROP CONSTRAINT IF EXISTS clip_transcription_run_status_check;
ALTER TABLE clip_transcription_run ADD CONSTRAINT clip_transcription_run_status_check
    CHECK (status IN ('pending', 'running', 'succeeded', 'failed'));
ALTER TABLE clip_summary_run DROP CONSTRAINT IF EXISTS clip_summary_run_status_check;
ALTER TABLE clip_summary_run ADD CONSTRAINT clip_summary_run_status_check
    CHECK (status IN ('pending', 'running', 'succeeded', 'failed'));
