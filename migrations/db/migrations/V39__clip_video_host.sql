-- Which video host holds the clip's video. Bunny Stream and our own pipeline run side by side while new uploads
-- move over, so every per-clip host call (fetch, delete, media URLs) goes to the clip's own host. Every existing
-- clip is on Bunny. The allowed values are the API's host names (VideoHostNames); add one here before the API
-- registers a host under it.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS video_host text NOT NULL DEFAULT 'bunny';

ALTER TABLE clip
    DROP CONSTRAINT IF EXISTS clip_video_host_check;
ALTER TABLE clip
    ADD CONSTRAINT clip_video_host_check CHECK (video_host IN ('bunny', 'r2'));
