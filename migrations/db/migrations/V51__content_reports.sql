-- Reports of shared clips and the takedowns they lead to (api/Moderation/). Anyone watching a share link can report
-- it; admins review open reports at /admin/reports and either dismiss them or take the clip down, which revokes its
-- share links and stops it being shared again.

-- Set when an admin takes the clip down. The clip stays in its owner's library, but can't be shared.
ALTER TABLE clip ADD COLUMN IF NOT EXISTS sharing_blocked_at timestamptz;

CREATE TABLE IF NOT EXISTS content_report (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Null once the clip is deleted; the report and what it said about the clip are kept.
    clip_id uuid REFERENCES clip(id) ON DELETE SET NULL,
    clip_owner_id uuid REFERENCES app_user(id) ON DELETE SET NULL,
    clip_title text,
    share_token text NOT NULL,
    reason text NOT NULL CONSTRAINT content_report_reason_check CHECK (reason IN (
        'child_safety', 'terrorism', 'sexual', 'violence', 'hate_or_harassment', 'copyright', 'other')),
    details text NOT NULL DEFAULT '' CONSTRAINT content_report_details_check CHECK (length(details) <= 2000),
    -- Optional, so the reporter can be told the outcome or asked for more.
    reporter_email text,
    status text NOT NULL DEFAULT 'open'
        CONSTRAINT content_report_status_check CHECK (status IN ('open', 'taken_down', 'dismissed')),
    resolved_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
    resolution_note text,
    resolved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS content_report_open_idx ON content_report (created_at) WHERE status = 'open';
CREATE INDEX IF NOT EXISTS content_report_resolved_idx ON content_report (resolved_at DESC) WHERE status <> 'open';
CREATE INDEX IF NOT EXISTS content_report_clip_idx ON content_report (clip_id);
