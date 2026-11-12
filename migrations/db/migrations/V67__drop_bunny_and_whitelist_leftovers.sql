-- The leftovers V50 and V62 kept for the image they replaced, which no running image reads or writes any more.
--
-- Bunny Stream (gone since V62): the host's own status numbers (superseded by video_state in V38), the Bunny copy a
-- clip kept after moving to R2 and when it moved (V44, with V59's checks over them), the category a moved clip's Bunny
-- collection was keyed by (V60), and the Bunny collections themselves (V16, V36).
ALTER TABLE IF EXISTS clip DROP CONSTRAINT IF EXISTS clip_bunny_copy_migrated_check;
ALTER TABLE IF EXISTS clip DROP CONSTRAINT IF EXISTS clip_bunny_host_no_copy_check;
ALTER TABLE IF EXISTS clip DROP COLUMN IF EXISTS video_status;
ALTER TABLE IF EXISTS clip DROP COLUMN IF EXISTS bunny_video_id;
ALTER TABLE IF EXISTS clip DROP COLUMN IF EXISTS migrated_at;
ALTER TABLE IF EXISTS clip DROP COLUMN IF EXISTS filed_category_id;
DROP TABLE IF EXISTS clip_collection_merge;
DROP TABLE IF EXISTS clip_collection;

-- Access (database-backed since V50): the whitelist's marker on an account's role (V20) and the extra permissions
-- V50 turned into the Admin role (V16).
ALTER TABLE IF EXISTS app_user DROP COLUMN IF EXISTS role_from_whitelist;
DROP TABLE IF EXISTS user_additional_permission;
