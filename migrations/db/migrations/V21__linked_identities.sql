-- Accounts become provider-neutral: discord_user is renamed to app_user and the Discord
-- login moves into user_identity, so one account can hold a Discord and a Twitch identity.

ALTER TABLE discord_user RENAME TO app_user;

CREATE TABLE IF NOT EXISTS user_identity (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
    provider text NOT NULL,
    provider_user_id text NOT NULL,
    username text NOT NULL,
    display_name text,
    avatar_url text,
    linked_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT user_identity_provider_user_key UNIQUE (provider, provider_user_id),
    CONSTRAINT user_identity_user_provider_key UNIQUE (user_id, provider)
);

CREATE INDEX IF NOT EXISTS user_identity_user_id_idx ON user_identity (user_id);

-- Existing accounts keep their Discord login as their first (primary) identity.
INSERT INTO user_identity (user_id, provider, provider_user_id, username, display_name, avatar_url)
SELECT
    id,
    'discord',
    discord_id,
    username,
    global_name,
    CASE
        WHEN avatar IS NULL OR avatar = '' THEN NULL
        ELSE 'https://cdn.discordapp.com/avatars/' || discord_id || '/' || avatar
    END
FROM app_user
ON CONFLICT (provider, provider_user_id) DO NOTHING;

-- The account avatar becomes a full URL so it no longer depends on a Discord id.
UPDATE app_user
SET avatar = CASE
    WHEN avatar IS NULL OR avatar = '' THEN NULL
    ELSE 'https://cdn.discordapp.com/avatars/' || discord_id || '/' || avatar
END;

ALTER TABLE app_user RENAME COLUMN avatar TO avatar_url;
ALTER TABLE app_user DROP COLUMN discord_id;
