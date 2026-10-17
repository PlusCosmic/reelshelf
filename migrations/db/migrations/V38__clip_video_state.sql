-- The clip's video status in the app's own terms (ClipVideoStatus), instead of the video host's numbers in
-- video_status, which use Bunny's video object numbering (0 created, 1 uploaded, 2 processing, 3 transcoding,
-- 4 finished, 5 error, 6 upload failed, 7 JIT segmenting, 8 JIT playlists created).
--
-- video_status is left in place, and still written alongside video_state, so the previous app image keeps working
-- against this schema while a deploy rolls out; a later migration drops it. Rows that image writes in the meantime
-- get the default here and are corrected by the status refresh, which re-reads every clip that is not ready.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS video_state text NOT NULL DEFAULT 'awaiting_upload';

-- A NULL status is a row reserved before its video was created. A number Bunny may add later is treated as
-- processing, as the app treated unknown numbers before: not playable, not abandoned, and refreshed.
UPDATE clip
SET video_state = CASE
        WHEN video_status IS NULL OR video_status = 0 THEN 'awaiting_upload'
        WHEN video_status = 1 THEN 'uploaded'
        WHEN video_status = 4 THEN 'ready'
        WHEN video_status = 5 THEN 'encode_failed'
        WHEN video_status = 6 THEN 'upload_failed'
        ELSE 'processing'
    END;

ALTER TABLE clip
    DROP CONSTRAINT IF EXISTS clip_video_state_check;
ALTER TABLE clip
    ADD CONSTRAINT clip_video_state_check CHECK (
        video_state IN ('awaiting_upload', 'uploaded', 'processing', 'ready', 'encode_failed', 'upload_failed'));
