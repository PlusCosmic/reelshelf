-- V16 declares UNIQUE (owner_id, game_category_id) on clip_collection, but production adopted V16 without running
-- it, so the constraint never reached that table and concurrent first uploads to a game could each insert a
-- collection for it, splitting the owner's videos across several Bunny collections. Keep the lowest id per owner and
-- category, set the others aside for ClipCollectionMergeService to move their videos into the kept collection and
-- delete them at Bunny, and add the constraint where it is missing.
CREATE TABLE IF NOT EXISTS clip_collection_merge (
    -- The extra Bunny collection whose videos move into the owner's collection for the category.
    collection_id uuid PRIMARY KEY,
    owner_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE
);

WITH duplicate AS (
    SELECT id, collection_id, owner_id, game_category_id
    FROM (
        SELECT *, row_number() OVER (PARTITION BY owner_id, game_category_id ORDER BY id) AS position
        FROM clip_collection
    ) ranked
    WHERE position > 1
), set_aside AS (
    INSERT INTO clip_collection_merge (collection_id, owner_id, game_category_id)
    SELECT collection_id, owner_id, game_category_id FROM duplicate
    ON CONFLICT DO NOTHING
)
DELETE FROM clip_collection WHERE id IN (SELECT id FROM duplicate);

-- Same name Postgres gave the V16 constraint, so databases that ran V16 already have it and skip this.
CREATE UNIQUE INDEX IF NOT EXISTS clip_collection_owner_id_game_category_id_key
    ON clip_collection (owner_id, game_category_id);
