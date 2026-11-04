-- Moving a clip to another book (POST /api/clips/move) changes clip.game_category_id, but a Bunny video stays in the
-- Bunny collection it was filed in, which clip_collection keys by the owner and the category the clip was uploaded to.
-- filed_category_id remembers that category once a clip first moves (NULL means it never has, so its collection is
-- its game_category_id's), and the Bunny sweep keeps a collection while any clip filed in it is on Bunny or still has
-- a Bunny copy, so moving a clip out of a book never lets its old collection, and the video in it, be deleted.
ALTER TABLE clip ADD COLUMN IF NOT EXISTS filed_category_id uuid;
