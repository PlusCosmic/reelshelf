-- Moving the clips still on Bunny onto our own pipeline (R2 + AV1, #239; plan in
-- docs/video-pipeline/bunny-migration.md). A `migrate` job re-encodes a ready Bunny clip's HLS stream into version 1
-- under clips/{clip id}/v1/, and its completion switches the clip to R2. The clip keeps playing from Bunny until then.
ALTER TABLE transcode_run
    DROP CONSTRAINT IF EXISTS transcode_run_type_check;
ALTER TABLE transcode_run
    ADD CONSTRAINT transcode_run_type_check CHECK (type IN ('transcode', 'thumbnails', 'trim', 'fallback', 'migrate'));

-- The clip's old Bunny video, kept after the switch until the delete sweep (#241) removes it, so a bad encode can be
-- switched back within the grace period. NULL for every clip that never moved.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS bunny_video_id uuid;

-- When the clip moved to R2. The grace period before its Bunny copy is deleted counts from it.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS migrated_at timestamptz;
