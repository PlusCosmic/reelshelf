-- What each account last told PostHog about itself (ADR-0009): the person properties feature flags target, as the
-- JSON the API sent, so the sync only identifies accounts whose properties changed. Gone with the account; the account
-- deletion deletes the person at PostHog first.
CREATE TABLE IF NOT EXISTS posthog_person (
    app_user_id uuid PRIMARY KEY REFERENCES app_user(id) ON DELETE CASCADE,
    properties text NOT NULL,
    synced_at timestamptz NOT NULL DEFAULT now()
);
