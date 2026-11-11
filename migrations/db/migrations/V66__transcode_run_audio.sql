-- The audio job: a new video version made from a clip's current output with its video copied and its audio
-- re-encoded as AAC-LC. Outputs encoded before this carry Opus audio, which Safari (every browser on iOS) doesn't play
-- in MP4, so those clips couldn't play on iPhones whatever their AV1 support. The API's AacBackfillService queues one
-- per such clip.
ALTER TABLE transcode_run DROP CONSTRAINT IF EXISTS transcode_run_type_check;
ALTER TABLE transcode_run
    ADD CONSTRAINT transcode_run_type_check CHECK (type IN ('transcode', 'thumbnails', 'trim', 'audio', 'fallback'));

-- A clip has at most one audio job waiting or in progress, so two API instances topping up the backfill at once
-- queue it once.
CREATE UNIQUE INDEX IF NOT EXISTS transcode_run_active_audio_key
    ON transcode_run (clip_id)
    WHERE type = 'audio' AND status IN ('pending', 'running');
