# Reelshelf Migrations

This project owns database migrations for the Reelshelf services.

Migrations use Evolve and live in `db/migrations` with Evolve names such as `V16__baseline_schema.sql`.
The runner is intentionally separate from the APIs so deployments can migrate the database as an explicit step before starting new application code.

The infrastructure repository's `services/reelshelf.yml` compose file runs this project as a one-shot `reelshelf-migrations` container and makes the Reelshelf API service depend on successful completion. Keep a single migration owner per database.

The consolidated baseline starts at `V16` because the legacy combined backend already used Evolve migrations `V1` through `V15`. That avoids checksum conflicts on databases that already have the legacy `changelog` table.

## Safety

- `erase` is not exposed and Evolve erase is disabled.
- A normal migration run refuses a non-empty database that has no Evolve `changelog` table.
- Use `--adopt-existing` only once for an existing production database that already matches `V16`.
- Adoption records the current database as starting at `V16`; it does not run the baseline SQL.
- A database that already has the legacy Evolve `changelog` is continued from `V16` so the old `V1`-`V15` scripts do not need to remain in this split repo.

## Commands

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." dotnet run --project migrations
```

For first-time production adoption:

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." dotnet run --project migrations -- --adopt-existing
```
