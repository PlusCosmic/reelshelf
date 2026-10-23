-- Paid plans through Stripe (ADR-0007). An account's tier is never stored: it is Unlimited when whitelist.json lists
-- it, else the best tier its subscriptions grant, else Free.

-- The Stripe customer Checkout created for the account, linked when its first checkout completes and reused for
-- every later one and for the Customer Portal.
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS stripe_customer_id text;

CREATE UNIQUE INDEX IF NOT EXISTS app_user_stripe_customer_id_idx
    ON app_user (stripe_customer_id)
    WHERE stripe_customer_id IS NOT NULL;

-- Each Stripe subscription as the webhook last read it from Stripe. Rows are kept after a subscription ends, so the
-- account's history stays; only active, trialing and past_due ones grant their tier.
CREATE TABLE IF NOT EXISTS billing_subscription
(
    id                   text PRIMARY KEY,
    user_id              uuid        NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    status               text        NOT NULL,
    lookup_key           text,
    -- NULL when the price's lookup key isn't a known plan, so it grants nothing.
    tier                 text CHECK (tier IN ('plus', 'pro')),
    current_period_end   timestamptz,
    cancel_at_period_end boolean     NOT NULL DEFAULT false,
    cancel_at            timestamptz,
    ended_at             timestamptz,
    updated_at           timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS billing_subscription_user_idx
    ON billing_subscription (user_id);
