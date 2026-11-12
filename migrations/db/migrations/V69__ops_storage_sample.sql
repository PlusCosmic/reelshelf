-- The database's own total of what the object store holds (clips as the plan counts them, plus exports held), one row
-- per UTC day, recorded while the monitor app reads /api/admin/ops/storage. Set beside the bucket's daily size from
-- CloudWatch, the difference over the days shows whether objects the database no longer knows are piling up.
CREATE TABLE IF NOT EXISTS ops_storage_sample (
    day date PRIMARY KEY,
    database_bytes bigint NOT NULL,
    recorded_at timestamptz NOT NULL
);
