-- Bunny Stream is gone: every clip moved to R2 (V44's migrate jobs) and the sweep deleted the Bunny copies. Clips can
-- now only be on r2, and the migrate job type goes with the code that ran it. A database that still has a clip on
-- Bunny is refused rather than left with clips nothing can play: move them first (reelshelf-docs/video-pipeline/
-- bunny-migration.md), or delete them.
--
-- The columns and tables only Bunny used (clip.video_status, bunny_video_id, migrated_at, filed_category_id,
-- clip_collection, clip_collection_merge) are dropped by a later migration, once no running image reads them.
DO $$
DECLARE
    on_bunny bigint;
BEGIN
    SELECT count(*) INTO on_bunny FROM clip WHERE video_host = 'bunny';
    IF on_bunny > 0 THEN
        RAISE EXCEPTION '% clips are still on Bunny; move or delete them before removing Bunny', on_bunny;
    END IF;
END
$$;

ALTER TABLE clip ALTER COLUMN video_host SET DEFAULT 'r2';
ALTER TABLE clip DROP CONSTRAINT IF EXISTS clip_video_host_check;
ALTER TABLE clip ADD CONSTRAINT clip_video_host_check CHECK (video_host IN ('r2'));

-- Finished migrate jobs are history only; nothing reads them.
DELETE FROM transcode_run WHERE type = 'migrate';
ALTER TABLE transcode_run DROP CONSTRAINT IF EXISTS transcode_run_type_check;
ALTER TABLE transcode_run
    ADD CONSTRAINT transcode_run_type_check CHECK (type IN ('transcode', 'thumbnails', 'trim', 'fallback'));
