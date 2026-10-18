-- Queue metrics (api/QueueMetrics/TranscodeRunMetricsSource.cs) count transcode jobs finished in the last hour every
-- 30 seconds or so. V40's partial indexes cover pending and running jobs only, so without this the count scans every
-- job ever made. The predicate matches the metrics query's status list exactly, as V41's do for the other run tables.
CREATE INDEX IF NOT EXISTS transcode_run_finished_idx
    ON transcode_run (finished_at)
    WHERE status IN ('succeeded', 'failed');
