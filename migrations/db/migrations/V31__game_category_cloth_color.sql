-- The colour a game's book is bound in on the library shelf, as #rrggbb. Derived from the IGDB cover and
-- cleared when the cover changes; NULL until it has been worked out, and for covers that cannot be fetched.
ALTER TABLE game_category
    ADD COLUMN IF NOT EXISTS cloth_color text;
