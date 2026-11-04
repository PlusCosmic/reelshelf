-- Ties status columns to the fields that go with them (#331), so combinations no statement produces can't be stored.
-- Each block first normalises any row that would break its constraint, so this never fails on existing data, and
-- every constraint is dropped and added again, so the file can run twice.
--
-- billing_subscription.status gets no constraint: Stripe may add a status, and the webhook must still record it.
-- BillingService logs a status SubscriptionStatuses doesn't know instead.

-- content_report: a report is resolved exactly when it isn't open. resolved_by may still be NULL on a resolved report
-- (the admin's account was deleted: ON DELETE SET NULL). A removed report's evidence_id is NULL when the clip was
-- already gone when it was removed, or once its evidence passed EvidenceRetention (ON DELETE SET NULL); both are
-- expected, so nothing ties evidence_id to the status.
UPDATE content_report
SET resolved_at = NULL,
    resolved_by = NULL
WHERE status = 'open'
  AND (resolved_at IS NOT NULL OR resolved_by IS NOT NULL);
UPDATE content_report
SET resolved_at = created_at
WHERE status <> 'open'
  AND resolved_at IS NULL;

ALTER TABLE content_report DROP CONSTRAINT IF EXISTS content_report_resolution_check;
ALTER TABLE content_report ADD CONSTRAINT content_report_resolution_check
    CHECK ((status = 'open') = (resolved_at IS NULL) AND (status <> 'open' OR resolved_by IS NULL));

-- clip_export: pending and running exports have no completion or expiry; a finished one has a completion; a ready or
-- expired one has an expiry and no error; a failed one has an error and no expiry. A pending or running export may
-- carry the error of an earlier attempt.
UPDATE clip_export
SET completed_at = NULL,
    expires_at   = NULL
WHERE status IN ('pending', 'running')
  AND (completed_at IS NOT NULL OR expires_at IS NOT NULL);
UPDATE clip_export
SET completed_at = COALESCE(completed_at, created_at),
    expires_at   = COALESCE(expires_at, COALESCE(completed_at, created_at) + interval '28 days'),
    error        = NULL
WHERE status IN ('ready', 'expired')
  AND (completed_at IS NULL OR expires_at IS NULL OR error IS NOT NULL);
UPDATE clip_export
SET completed_at = COALESCE(completed_at, created_at),
    expires_at   = NULL,
    error        = COALESCE(error, 'The export failed')
WHERE status = 'failed'
  AND (completed_at IS NULL OR expires_at IS NOT NULL OR error IS NULL);

ALTER TABLE clip_export DROP CONSTRAINT IF EXISTS clip_export_status_fields_check;
ALTER TABLE clip_export ADD CONSTRAINT clip_export_status_fields_check
    CHECK (CASE status
               WHEN 'pending' THEN completed_at IS NULL AND expires_at IS NULL
               WHEN 'running' THEN completed_at IS NULL AND expires_at IS NULL
               WHEN 'ready' THEN completed_at IS NOT NULL AND expires_at IS NOT NULL AND error IS NULL
               WHEN 'expired' THEN completed_at IS NOT NULL AND expires_at IS NOT NULL AND error IS NULL
               WHEN 'failed' THEN completed_at IS NOT NULL AND expires_at IS NULL AND error IS NOT NULL
               ELSE false
        END);

-- clip: a binned clip isn't picked to keep (binning clears kept_at, and picking a binned clip changes nothing), since
-- OverLimit.Split lets binned clips go first whatever the pick says.
UPDATE clip
SET kept_at = NULL
WHERE binned_at IS NOT NULL
  AND kept_at IS NOT NULL;

ALTER TABLE clip DROP CONSTRAINT IF EXISTS clip_kept_not_binned_check;
ALTER TABLE clip ADD CONSTRAINT clip_kept_not_binned_check
    CHECK (binned_at IS NULL OR kept_at IS NULL);

-- clip: a clip with a Bunny copy left behind has moved (SetMigratedToR2 sets both; the delete sweep clears only
-- bunny_video_id). A copy with no migrated_at could only come from a hand edit. Giving it a migrated_at here would
-- schedule the delete sweep to remove that Bunny video, so this migration never does: the constraint is left out
-- (with a notice) until a person sorts those clips out and this block is run again.
ALTER TABLE clip DROP CONSTRAINT IF EXISTS clip_bunny_copy_migrated_check;
DO
$$
    DECLARE
        stray integer;
    BEGIN
        SELECT count(*) INTO stray FROM clip WHERE bunny_video_id IS NOT NULL AND migrated_at IS NULL;
        IF stray = 0 THEN
            ALTER TABLE clip ADD CONSTRAINT clip_bunny_copy_migrated_check
                CHECK (bunny_video_id IS NULL OR migrated_at IS NOT NULL);
        ELSE
            RAISE NOTICE 'clip_bunny_copy_migrated_check left out: % clips have a Bunny copy but no migrated_at', stray;
        END IF;
    END
$$;

-- clip: a clip playing from Bunny has no separate Bunny copy (switching back, as bunny-migration.md does it, clears
-- bunny_video_id). A copy that is the video it plays from is just cleared. A clip on Bunny with a different copy left
-- could only come from a hand edit; clearing it would leak that Bunny video, so then the constraint is left out
-- (with a notice) until a person sorts those clips out and this block is run again.
UPDATE clip
SET bunny_video_id = NULL
WHERE video_host = 'bunny'
  AND bunny_video_id = video_id;

ALTER TABLE clip DROP CONSTRAINT IF EXISTS clip_bunny_host_no_copy_check;
DO
$$
    DECLARE
        stray integer;
    BEGIN
        SELECT count(*) INTO stray FROM clip WHERE video_host = 'bunny' AND bunny_video_id IS NOT NULL;
        IF stray = 0 THEN
            ALTER TABLE clip ADD CONSTRAINT clip_bunny_host_no_copy_check
                CHECK (video_host <> 'bunny' OR bunny_video_id IS NULL);
        ELSE
            RAISE NOTICE 'clip_bunny_host_no_copy_check left out: % clips on Bunny still have another Bunny copy', stray;
        END IF;
    END
$$;
