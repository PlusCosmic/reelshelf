-- Input tokens the provider served from its prompt cache. A subset of input_tokens, not in addition to it;
-- the prompt and reference sheet are identical on every run, so this shows how much caching saves.
ALTER TABLE legend_detection_run
    ADD COLUMN IF NOT EXISTS cached_input_tokens int;
