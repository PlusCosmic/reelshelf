-- The account's latest Stripe Checkout Session. Before starting another, the API reads it from Stripe: a completed
-- one's subscription is recorded at once (so a second payment is refused while the webhook is still on its way),
-- and an open one is expired (so two tabs can't both pay).
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS stripe_checkout_session_id text;
