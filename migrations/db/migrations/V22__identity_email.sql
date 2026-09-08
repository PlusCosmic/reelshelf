-- Providers report an email address; it is kept per identity so the first-sign-in step can suggest it.
ALTER TABLE user_identity ADD COLUMN IF NOT EXISTS email text;

-- The account email is chosen by the user (prefilled from a provider) and is where account mail goes.
-- onboarding_completed_at records that the user has been asked, whether or not they gave an address.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS email text;
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS onboarding_completed_at timestamptz;
