ALTER TABLE game_category
    ADD COLUMN IF NOT EXISTS key_art_url text,
    ADD COLUMN IF NOT EXISTS game_logo_url text;
