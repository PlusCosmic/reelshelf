# Reelshelf

Single application repository for the Plus Cosmic Reelshelf app.

## Layout

- `api` - .NET API for clips, playlists, auth, game metadata, Bunny, and Apex integrations
- `frontend` - React, Vite, TanStack Router, TanStack Query, and Mantine frontend
- `frontend/src/api-client` - committed generated TypeScript client for the Reelshelf API
- `migrations` - explicit Evolve database migration runner
- `Dockerfile` - production image that builds the frontend and serves it from the API

## Commands

```sh
bun install
bun run dev
bun run build
bun run lint
bun run typecheck
bun run test
bun run build:api
```

Run the API directly with:

```sh
dotnet run --project api
```

## API Client

The frontend API client is generated from the .NET OpenAPI document and committed under `frontend/src/api-client`.

```sh
bun run generate:api-client
bun run check:api-client-drift
```

Do not hand-edit generated files; update the API and regenerate the client.

## Database Migrations

Database schema changes are applied by the explicit runner in `migrations`. The API does not run migrations during startup.

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." bun run migrate
```

For the first deployment to an existing production database that already has the current schema, run this once:

```sh
DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." bun run migrate:adopt-existing
```

Adoption records the existing database as being at migration `V16` and does not execute the baseline SQL. Normal `bun run migrate` refuses to touch a non-empty database that has no Evolve changelog so the baseline cannot be accidentally replayed against production.
