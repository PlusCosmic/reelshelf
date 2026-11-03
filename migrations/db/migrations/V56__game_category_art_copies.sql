-- Our own copies of an IGDB category's images (GameArtMirror). cover_url, key_art_url and game_logo_url stay the
-- IGDB source URLs; each *_key is the R2 object holding the copy of that source, named after IGDB's image id, so a
-- changed image is a new key. NULL until copied, and a key that no longer matches its source isn't served.
-- igdb_checked_at is when the category was last read from IGDB; GameCategoryAssetRefreshService reads it again once
-- it is a day old.
ALTER TABLE game_category
    ADD COLUMN IF NOT EXISTS cover_key text,
    ADD COLUMN IF NOT EXISTS key_art_key text,
    ADD COLUMN IF NOT EXISTS game_logo_key text,
    ADD COLUMN IF NOT EXISTS igdb_checked_at timestamptz;
