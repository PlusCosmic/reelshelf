-- Archiving runs lets detection be tried again from scratch while keeping their usage for comparison.
-- Archived runs are ignored by the worker, the review page and the one-automatic-run-per-clip rule.
ALTER TABLE legend_detection_run
    ADD COLUMN IF NOT EXISTS archived_at timestamptz;

DROP INDEX IF EXISTS legend_detection_run_auto_clip_key;
CREATE UNIQUE INDEX IF NOT EXISTS legend_detection_run_auto_clip_key
    ON legend_detection_run (clip_id)
    WHERE trigger = 'auto' AND archived_at IS NULL;
