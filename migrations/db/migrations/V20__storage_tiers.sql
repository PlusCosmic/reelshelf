-- Open sign-up with storage tiers: everyone can upload, whitelist.json only grants unlimited storage.

-- Client-declared size of the original upload, recorded when the clip row is created.
-- Bunny reports storage_size only after encoding, so file_size is what the quota counts first.
ALTER TABLE clip ADD COLUMN IF NOT EXISTS file_size bigint;

CREATE INDEX IF NOT EXISTS idx_clip_owner_id ON clip(owner_id);

-- Viewer cannot create clips. Now that anyone may sign in and use their free storage tier,
-- new accounts default to Editor and existing Viewer accounts are promoted.
ALTER TABLE discord_user ALTER COLUMN role SET DEFAULT 'Editor';
UPDATE discord_user SET role = 'Editor' WHERE role = 'Viewer';
