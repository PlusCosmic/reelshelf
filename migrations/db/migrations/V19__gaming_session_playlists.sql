CREATE TABLE IF NOT EXISTS playlist_gaming_sessions (
    playlist_id uuid PRIMARY KEY REFERENCES playlists(id) ON DELETE CASCADE,
    owner_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
    session_date date NOT NULL,
    timezone text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (owner_id, game_category_id, session_date)
);

