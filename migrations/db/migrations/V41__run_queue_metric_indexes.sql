-- Queue metrics (api/QueueMetrics/) count each run table's runs finished in the last hour every 30 seconds or so.
-- Pending and running rows are already covered by the partial *_queue_idx indexes; these cover finished ones, which
-- otherwise take a scan of every run ever made. The predicate matches the metrics query's status list exactly.
CREATE INDEX IF NOT EXISTS legend_detection_run_finished_idx
    ON legend_detection_run (completed_at)
    WHERE status IN ('succeeded', 'failed');

CREATE INDEX IF NOT EXISTS clip_transcription_run_finished_idx
    ON clip_transcription_run (completed_at)
    WHERE status IN ('succeeded', 'failed');

CREATE INDEX IF NOT EXISTS clip_summary_run_finished_idx
    ON clip_summary_run (completed_at)
    WHERE status IN ('succeeded', 'failed');
