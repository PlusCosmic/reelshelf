-- An eval runs one provider, model and reasoning effort over every clip in the legend baseline (the labelled clips),
-- so models are compared on the same clips for accuracy, cost and speed. Its runs are legend_detection_run rows with
-- trigger 'eval' and this eval_id; nothing outside legend detection reads them, so they never become a clip's legend.
CREATE TABLE IF NOT EXISTS legend_eval (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider text NOT NULL,
    model text NOT NULL,
    -- NULL leaves it to the model's default.
    reasoning_effort text,
    created_by uuid REFERENCES app_user(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE legend_detection_run
    ADD COLUMN IF NOT EXISTS eval_id uuid REFERENCES legend_eval(id) ON DELETE CASCADE,
    -- Input tokens written to the provider's prompt cache, a subset of input_tokens billed at the cache-write price.
    ADD COLUMN IF NOT EXISTS cache_write_input_tokens int,
    -- What the provider billed for the run, when it says so (OpenRouter does); otherwise worked out from prices.
    ADD COLUMN IF NOT EXISTS cost_usd numeric(14, 8);

CREATE INDEX IF NOT EXISTS legend_detection_run_eval_idx
    ON legend_detection_run (eval_id)
    WHERE eval_id IS NOT NULL;

-- Prices in US dollars per million tokens, set on the evals page. An eval's cost is worked out from them when it is
-- read, so correcting a price corrects every eval. A cached or cache-write price left NULL falls back to input.
CREATE TABLE IF NOT EXISTS legend_model_price (
    provider text NOT NULL,
    model text NOT NULL,
    input_per_mtok numeric(12, 6),
    cached_input_per_mtok numeric(12, 6),
    cache_write_per_mtok numeric(12, 6),
    output_per_mtok numeric(12, 6),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, model)
);
