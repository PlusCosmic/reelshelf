-- Removing clips reported for child sexual abuse or terrorism, keeping a private copy as evidence, and suspending
-- their uploaders (api/Moderation/). An admin's removal snapshots the clip and its owner into clip_evidence, suspends
-- the owner and deletes the clip row in one transaction; ClipEvidenceProcessor then copies the video into the private
-- evidence bucket, deletes it from public storage and purges the CDN. The evidence is deleted after a year.

-- Set while an account is suspended: it can't sign in, and its share links stop working. Changed only through
-- AccountAccessStatements, and recorded in account_access_change like role and granted tier.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS suspended_at timestamptz;

ALTER TABLE account_access_change DROP CONSTRAINT IF EXISTS account_access_change_field_check;
ALTER TABLE account_access_change ADD CONSTRAINT account_access_change_field_check
    CHECK (field IN ('role', 'granted_tier', 'suspended'));

CREATE TABLE IF NOT EXISTS clip_evidence (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- No foreign keys to the clip or its owner: the clip row is deleted on removal, and the evidence outlives the
    -- account. What they were is in the snapshots.
    clip_id uuid NOT NULL,
    owner_id uuid,
    reason text NOT NULL,
    removed_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
    note text NOT NULL,
    removed_at timestamptz NOT NULL DEFAULT now(),
    -- The clip row (with its game's name) and the owner with every linked sign-in, as they were when removed.
    clip jsonb NOT NULL,
    owner jsonb NOT NULL,
    status text NOT NULL DEFAULT 'pending'
        CONSTRAINT clip_evidence_status_check CHECK (status IN ('pending', 'preserved', 'failed')),
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT now(),
    claimed_at timestamptz,
    last_error text,
    -- Keys in the evidence bucket, once copied.
    files jsonb,
    media_deleted_at timestamptz,
    -- 'purged', 'not_configured' (no Cloudflare token) or 'not_needed' (nothing on the media domain).
    cdn_purge text,
    -- A worker may still upload with presigned URLs it held when the clip was removed; a second sweep deletes them.
    swept_at timestamptz,
    delete_after timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS clip_evidence_pending_idx ON clip_evidence (available_at) WHERE status = 'pending';
CREATE INDEX IF NOT EXISTS clip_evidence_unswept_idx ON clip_evidence (removed_at)
    WHERE status = 'preserved' AND swept_at IS NULL;
CREATE INDEX IF NOT EXISTS clip_evidence_delete_after_idx ON clip_evidence (delete_after);

ALTER TABLE content_report DROP CONSTRAINT IF EXISTS content_report_status_check;
ALTER TABLE content_report ADD CONSTRAINT content_report_status_check
    CHECK (status IN ('open', 'taken_down', 'dismissed', 'removed'));
ALTER TABLE content_report
    ADD COLUMN IF NOT EXISTS evidence_id uuid REFERENCES clip_evidence(id) ON DELETE SET NULL;
