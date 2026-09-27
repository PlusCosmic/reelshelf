-- The reasoning effort a run asked the model for, such as 'low' or 'high'. NULL leaves it to the model's
-- default. Kept per run so batches at different efforts can be compared.
ALTER TABLE legend_detection_run
    ADD COLUMN IF NOT EXISTS reasoning_effort text;
