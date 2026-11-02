-- Where each book stands on its owner's library shelf, left to right from 0. NULL until the owner arranges the
-- shelf; books without a position (newly added ones included) follow the arranged ones, by name. A book that is
-- on the shelf only because it holds clips gets a user_game_category row when the shelf is arranged.
ALTER TABLE user_game_category ADD COLUMN IF NOT EXISTS shelf_position integer;
