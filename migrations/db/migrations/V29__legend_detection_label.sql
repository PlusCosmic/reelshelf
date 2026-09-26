-- The legends actually in a clip, set by an admin on the review page, so every run of every model and
-- prompt can be scored against the same answers. Kept per clip rather than per run: labels outlive archiving.
-- A null player_legend means the clip owner's legend cannot be identified from the clip. Teammate legends
-- are unordered, since HUD slot order carries no meaning here.
CREATE TABLE IF NOT EXISTS legend_detection_label (
    clip_id uuid PRIMARY KEY REFERENCES clip(id) ON DELETE CASCADE,
    player_legend text,
    teammate_legends text[] NOT NULL DEFAULT '{}',
    labelled_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
    labelled_at timestamptz NOT NULL DEFAULT now()
);
