# Database Migrations

Apply migrations in filename order against the PostgreSQL database used by the APIs.

The current repo uses Dapper/Npgsql directly, so SQL files are the source of truth for schema changes. New Dapper statements that require schema changes should add a new migration instead of relying on manual database drift.

