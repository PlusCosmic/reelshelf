-- The model tried when a run's own model heard no words in a clip with audio. Null when no fallback was tried;
-- when set, the stored transcript is the fallback's if it heard anything, and empty if it heard nothing either.
ALTER TABLE clip_transcription_run ADD COLUMN IF NOT EXISTS fallback_model text;
