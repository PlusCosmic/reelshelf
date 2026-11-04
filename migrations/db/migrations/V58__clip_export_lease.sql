-- Fences each export's maker (#320). Claiming an export gives it a new lease_id; heartbeats, completion and failure
-- are accepted only from the processor holding that lease while the export is running, so one that was reclaimed
-- after going quiet can no longer mark it ready, failed or pending again. A run writes its zips under
-- exports/{user_id}/{id}/{lease_id}/, so it never overwrites or deletes another run's parts; a ready export keeps
-- the lease_id of the run that made it, which names its parts. Exports made before this have none, and their parts
-- stay directly under exports/{user_id}/{id}/.
ALTER TABLE clip_export
    ADD COLUMN IF NOT EXISTS lease_id uuid;

-- One export at a time per account and reason. Older duplicates still waiting or running give way to the oldest.
UPDATE clip_export e
SET status       = 'failed',
    error        = 'Superseded by another export of the same kind',
    heartbeat_at = NULL,
    completed_at = now()
WHERE e.status IN ('pending', 'running')
  AND EXISTS (SELECT 1
              FROM clip_export older
              WHERE older.user_id = e.user_id
                AND older.reason = e.reason
                AND older.status IN ('pending', 'running')
                AND (older.created_at, older.id) < (e.created_at, e.id));

CREATE UNIQUE INDEX IF NOT EXISTS clip_export_active_idx
    ON clip_export (user_id, reason)
    WHERE status IN ('pending', 'running');
