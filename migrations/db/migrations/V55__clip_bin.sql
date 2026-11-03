-- The bin: a clip its owner deleted, kept until it is restored or emptied. While binned_at is set the clip is left
-- out of the library, its shelf, its tags and legends, collections, the Activity and its share links, but still
-- counts toward the owner's plan. ClipBinSweepService deletes it for good once it has been in the bin for
-- Bin:Retention (30 days).
ALTER TABLE clip ADD COLUMN IF NOT EXISTS binned_at timestamptz;

-- The owner's bin, newest first, and the sweep's oldest-first pass over every bin.
CREATE INDEX IF NOT EXISTS clip_binned_idx ON clip (owner_id, binned_at DESC) WHERE binned_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS clip_bin_due_idx ON clip (binned_at) WHERE binned_at IS NOT NULL;
