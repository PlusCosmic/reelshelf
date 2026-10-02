-- Turns a fresh restore of the prod database into dev's own copy. Run it once, against the dev database only,
-- straight after the restore and before anything else connects (see README.md):
--
--   psql "$DEV_DB_URL" -v ON_ERROR_STOP=1 -f scripts/dev-db/prepare-dev-copy.sql
--
-- It drops work that prod still owes, removes stored Twitch tokens, and records every clip's prod video id so
-- clone-bunny-videos.ts can move the copy onto videos of its own. The dev_copy* tables it creates are what that
-- script checks for before it touches anything, so it can never run against prod.

BEGIN;

DO $$
BEGIN
    IF current_database() NOT LIKE '%dev%' THEN
        RAISE EXCEPTION 'Refusing to prepare database "%": this script is for the dev copy only', current_database();
    END IF;
    IF to_regclass('dev_copy') IS NOT NULL THEN
        RAISE EXCEPTION 'Database "%" has already been prepared', current_database();
    END IF;
END $$;

-- Prod's job deletes these accounts' videos and rows; here the rows go without touching Bunny.
DELETE FROM app_user WHERE deletion_requested_at IS NOT NULL;

-- Queued model calls are prod's to make. Runs that already finished stay as history.
DELETE FROM legend_detection_run WHERE status IN ('pending', 'running');
DELETE FROM clip_transcription_run WHERE status IN ('pending', 'running');

-- Encrypted with prod's Data Protection keys, which dev doesn't have; linking Twitch again on dev stores new ones.
UPDATE user_identity
SET access_token = NULL,
    refresh_token = NULL,
    token_expires_at = NULL,
    token_scopes = NULL
WHERE access_token IS NOT NULL OR refresh_token IS NOT NULL;

CREATE TABLE dev_copy (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    prepared_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO dev_copy DEFAULT VALUES;

-- No foreign keys: the record should outlive clips and collections that dev later deletes.
CREATE TABLE dev_copy_collection (
    clip_collection_id uuid PRIMARY KEY,
    prod_collection_id uuid NOT NULL,
    dev_collection_id uuid
);
INSERT INTO dev_copy_collection (clip_collection_id, prod_collection_id)
SELECT id, collection_id FROM clip_collection;

-- state: pending -> created (dev video exists, not yet filled) -> swapped (clip points at the dev video),
-- or dropped when prod's video has nothing to copy and the clip row was deleted.
CREATE TABLE dev_copy_video (
    clip_id uuid PRIMARY KEY,
    prod_video_id uuid NOT NULL,
    dev_video_id uuid,
    state text NOT NULL DEFAULT 'pending',
    source text,
    error text,
    updated_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO dev_copy_video (clip_id, prod_video_id)
SELECT id, video_id FROM clip;

COMMIT;
