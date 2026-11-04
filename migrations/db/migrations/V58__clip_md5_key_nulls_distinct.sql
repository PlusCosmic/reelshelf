-- clip's per-book duplicate key, UNIQUE NULLS NOT DISTINCT (owner_id, game_category_id, md5_hash) from the baseline,
-- treated a missing hash as a value, so an owner could hold only one clip without an MD5 in each book. Twitch imports
-- have none, so the second import into a book hit the key and was taken for a repeat of the first. A repeated import is
-- already refused by its own key (clip_owner_source_clip_key, V24), so the hash key now covers clips with a hash alone.
CREATE UNIQUE INDEX IF NOT EXISTS clip_owner_category_md5_key
    ON clip (owner_id, game_category_id, md5_hash)
    WHERE md5_hash IS NOT NULL;

-- The old key was made without a name (prod adopted the baseline without running it), so find it by its columns.
DO $$
DECLARE
    key_name name;
BEGIN
    FOR key_name IN
        SELECT con.conname
        FROM pg_constraint con
        WHERE con.conrelid = 'clip'::regclass
          AND con.contype = 'u'
          AND (SELECT array_agg(attribute.attname::text ORDER BY attribute.attname)
               FROM unnest(con.conkey) AS key_column(attnum)
               JOIN pg_attribute attribute
                 ON attribute.attrelid = con.conrelid AND attribute.attnum = key_column.attnum)
              = ARRAY['game_category_id', 'md5_hash', 'owner_id']
    LOOP
        EXECUTE format('ALTER TABLE clip DROP CONSTRAINT %I', key_name);
    END LOOP;
END
$$;
