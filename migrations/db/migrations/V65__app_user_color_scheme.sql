-- The colour scheme the account picked for the app (Settings → Appearance), one of the names in
-- api/Users/ColorSchemes.cs. NULL is the default scheme, Stacks.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS color_scheme text;

ALTER TABLE app_user DROP CONSTRAINT IF EXISTS app_user_color_scheme_check;
ALTER TABLE app_user ADD CONSTRAINT app_user_color_scheme_check
    CHECK (color_scheme IN ('stacks', 'oxblood', 'ink', 'walnut', 'graphite'));
