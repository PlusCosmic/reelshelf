-- Google sign-in (ADR-0003) has no username, so an account whose primary identity is Google picks its own, and
-- anyone who has picked one keeps it: set when the account owner chooses a username, after which sign-ins no
-- longer copy the provider's username onto the account. Display name and avatar still follow the primary identity.
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS username_chosen_at timestamptz;
