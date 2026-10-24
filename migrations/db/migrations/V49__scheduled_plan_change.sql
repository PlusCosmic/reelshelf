-- A downgrade waiting for the end of the paid period (ADR-0007): the price the subscription's schedule moves it to,
-- and when. Read from Stripe with the rest of the subscription, so it is never set by anything but Stripe's state.
ALTER TABLE billing_subscription
    ADD COLUMN IF NOT EXISTS scheduled_lookup_key text,
    ADD COLUMN IF NOT EXISTS scheduled_at timestamptz;
