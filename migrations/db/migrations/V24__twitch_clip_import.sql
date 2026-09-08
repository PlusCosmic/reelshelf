-- Twitch clip import: the API downloads a user's own Twitch clips on their behalf, which needs the
-- provider's user access token after the sign-in round-trip has finished.

-- Token material is encrypted by the API (ASP.NET data protection) before it is stored; the columns hold
-- opaque ciphertext. token_scopes is the space-separated scope list the token was issued with, so the app
-- can tell whether an identity linked before a scope was requested needs to be re-authorized.
ALTER TABLE user_identity
    ADD COLUMN IF NOT EXISTS access_token text,
    ADD COLUMN IF NOT EXISTS refresh_token text,
    ADD COLUMN IF NOT EXISTS token_expires_at timestamptz,
    ADD COLUMN IF NOT EXISTS token_scopes text;

-- Where an imported clip came from. Locally uploaded clips leave both null. A provider clip is a single
-- video, so an owner can hold it once regardless of game assignment.
ALTER TABLE clip
    ADD COLUMN IF NOT EXISTS source_provider text,
    ADD COLUMN IF NOT EXISTS source_clip_id text;

CREATE UNIQUE INDEX IF NOT EXISTS clip_owner_source_clip_key
    ON clip (owner_id, source_provider, source_clip_id)
    WHERE source_clip_id IS NOT NULL;
