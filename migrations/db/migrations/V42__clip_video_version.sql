-- What our own pipeline (R2 + AV1, #212) records on a clip so its media URLs can be built. An R2 clip's outputs live
-- under clips/{clip id}/v{video_version}/; a trim writes a new version and the clip moves to it when it succeeds, so
-- cached copies of the old files are never served for the new video. video_codecs (RFC 6381, e.g.
-- "av01.0.08M.08,opus") and video_height describe that version's MP4 for the player's `sources`. Bunny clips keep
-- version 1 and leave the other two NULL; nothing reads them for Bunny.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS video_version integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS video_codecs text,
    ADD COLUMN IF NOT EXISTS video_height integer;

ALTER TABLE clip
    DROP CONSTRAINT IF EXISTS clip_video_version_check;
ALTER TABLE clip
    ADD CONSTRAINT clip_video_version_check CHECK (video_version >= 1);

ALTER TABLE clip
    DROP CONSTRAINT IF EXISTS clip_video_height_check;
ALTER TABLE clip
    ADD CONSTRAINT clip_video_height_check CHECK (video_height IS NULL OR video_height > 0);
