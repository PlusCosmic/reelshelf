CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'minecraft_server_type') THEN
        CREATE TYPE minecraft_server_type AS ENUM ('vanilla', 'fabric', 'neoforge', 'curseforge');
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS discord_user (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    discord_id text NOT NULL UNIQUE,
    username text NOT NULL,
    global_name text,
    avatar text,
    role text NOT NULL DEFAULT 'Viewer'
);

CREATE TABLE IF NOT EXISTS user_additional_permission (
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    permission text NOT NULL,
    granted_by uuid REFERENCES discord_user(id) ON DELETE SET NULL,
    granted_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, permission)
);

CREATE TABLE IF NOT EXISTS game_category (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    igdb_id bigint UNIQUE,
    name text NOT NULL,
    slug text NOT NULL UNIQUE,
    cover_url text,
    is_custom boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS user_game_category (
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, game_category_id)
);

CREATE TABLE IF NOT EXISTS clip_collection (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    collection_id uuid NOT NULL,
    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
    UNIQUE (owner_id, game_category_id)
);

CREATE TABLE IF NOT EXISTS clip (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    video_id uuid NOT NULL UNIQUE,
    game_category_id uuid NOT NULL REFERENCES game_category(id) ON DELETE CASCADE,
    md5_hash text,
    created_at timestamptz NOT NULL DEFAULT now(),
    title text,
    length integer,
    thumbnail_file_name text,
    date_uploaded timestamptz,
    storage_size bigint,
    video_status integer,
    encode_progress integer,
    UNIQUE NULLS NOT DISTINCT (owner_id, game_category_id, md5_hash)
);

CREATE TABLE IF NOT EXISTS tag (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS clip_tag (
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    tag_id uuid NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
    PRIMARY KEY (clip_id, tag_id)
);

CREATE TABLE IF NOT EXISTS clip_view (
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    viewed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, clip_id)
);

CREATE TABLE IF NOT EXISTS apex_clip_detection (
    clip_id uuid PRIMARY KEY REFERENCES clip(id) ON DELETE CASCADE,
    status text NOT NULL,
    primary_detection jsonb,
    secondary_detection jsonb,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS playlists (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    description text,
    creator_user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS playlist_collaborators (
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    added_by_user_id uuid REFERENCES discord_user(id) ON DELETE SET NULL,
    added_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (playlist_id, user_id)
);

CREATE TABLE IF NOT EXISTS playlist_clips (
    playlist_id uuid NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
    clip_id uuid NOT NULL REFERENCES clip(id) ON DELETE CASCADE,
    position integer NOT NULL,
    added_by_user_id uuid REFERENCES discord_user(id) ON DELETE SET NULL,
    added_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (playlist_id, clip_id),
    UNIQUE (playlist_id, position)
);

CREATE TABLE IF NOT EXISTS minecraft_server (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    owner_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    persistence_location text NOT NULL,
    container_name text NOT NULL UNIQUE,
    cpu_reservation numeric NOT NULL,
    ram_reservation integer NOT NULL,
    cpu_limit numeric NOT NULL,
    ram_limit integer NOT NULL,
    server_type minecraft_server_type NOT NULL,
    minecraft_version text NOT NULL,
    modloader_version text,
    curseforge_page_url text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    rcon_password text,
    max_players integer NOT NULL DEFAULT 20,
    motd text NOT NULL DEFAULT 'A Minecraft Server'
);

CREATE TABLE IF NOT EXISTS minecraft_command_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    server_id uuid REFERENCES minecraft_server(id) ON DELETE CASCADE,
    command text NOT NULL,
    response text,
    success boolean NOT NULL,
    error text,
    executed_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS minecraft_file_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES discord_user(id) ON DELETE CASCADE,
    server_id uuid REFERENCES minecraft_server(id) ON DELETE CASCADE,
    operation text NOT NULL,
    file_path text NOT NULL,
    success boolean NOT NULL,
    error text,
    executed_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_clip_owner_category_created ON clip(owner_id, game_category_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_clip_tag_tag_id ON clip_tag(tag_id);
CREATE INDEX IF NOT EXISTS idx_clip_view_user_id ON clip_view(user_id);
CREATE INDEX IF NOT EXISTS idx_playlist_collaborators_user_id ON playlist_collaborators(user_id);
CREATE INDEX IF NOT EXISTS idx_minecraft_server_owner ON minecraft_server(owner_id);
