# Plus Cosmic Development

A Turborepo monorepo with full-stack services using .NET APIs and React frontends.

## Services

Each service contains an `api/` (C# .NET) and `frontend/` (React + Vite).

- **clips** - Video clip management with resumable uploads, Apex Legends integration, and Bunny CDN
- **minecraft** - Minecraft server tools with Monaco editor for configuration

## Packages

### TypeScript
- `@repo/nucleus-api-client` - Auto-generated TypeScript client for Nucleus API
- `@repo/shared` - Shared services and hooks
- `@repo/ui` - Mantine-based UI components
- `@repo/eslint-config` - ESLint configurations
- `@repo/typescript-config` - TypeScript configurations

### .NET
- `Nucleus.Shared` - Shared .NET library (Auth, Discord, Games)

## Tech Stack

**Frontend**: React 19, TanStack Router, TanStack Query, Mantine, Vite
**Backend**: .NET, Dapper, Npgsql, PostgreSQL
**Tooling**: Bun, Turborepo

## Getting Started

```sh
# Install frontend dependencies
bun install

# Run all frontends in development
bun run dev

# Build all frontends
bun run build

# Lint
bun run lint

# Typecheck and tests
bun run typecheck
bun run test

# Build .NET APIs
bun run build:api

# Apply pending database migrations locally
bun run migrate
```

For .NET APIs, use `dotnet run` from the respective `services/*/api/` directories.

## Database Migrations

Database schema changes are applied by the explicit Evolve runner in `tools/Nucleus.Migrations`.
The APIs do not run migrations during startup. The clips Docker Compose stack in the infrastructure repository owns the one-shot migration service, which should be deployed before or alongside API services that depend on the shared database.

For a new empty database, run:

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." bun run migrate
```

For the first deployment to an existing production database that already has the current schema, run this once:

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." bun run migrate:adopt-existing
```

Adoption records the existing database as being at migration `V16` and does not execute the baseline SQL. The baseline starts at `V16` to avoid conflicts with the legacy backend's Evolve migrations `V1` through `V15`. Normal `bun run migrate` will refuse to touch a non-empty database that has no Evolve changelog so the baseline cannot be accidentally replayed against production.

If the database already has the legacy Evolve `changelog`, normal migration continues from `V16` without requiring the old `V1`-`V15` SQL files in this split repo.
