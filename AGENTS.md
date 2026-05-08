# AGENTS.md

## Repo Shape

- `api/` is the .NET 10 ASP.NET API; real startup and endpoint wiring live in `api/Program.cs`.
- `frontend/` is the Bun workspace React 19/Vite/TanStack Router app; app entry is `frontend/src/main.tsx` and routes live under `frontend/src/routes/`.
- `migrations/` is a separate .NET 10 Evolve migration runner; the API does not run migrations on startup.
- `frontend/src/api-client/` and `frontend/src/routeTree.gen.ts` are generated and committed; do not hand-edit them.

## Commands

- Install with `bun install` from the repo root; this repo pins `bun@1.3.13` in `package.json`.
- Root verification: `bun run check` runs `build -> typecheck -> lint -> test -> format:check`.
- Focused frontend checks: `bun run build`, `bun run typecheck`, `bun run lint`, `bun run test` from root delegate into `frontend/`.
- API build: `bun run build:api` or `dotnet build Reelshelf.sln --disable-build-servers -maxcpucount:1`.
- Prefer the existing dev runner in `~/dev/infrastructure/services/clips-dev.yml` for live development instead of manually running the frontend and API.
- The frontend test script is Vitest with `--passWithNoTests`; there are currently no committed test files.

## Generated Code

- After API contract changes, run `bun run generate:api-client`; it builds the API with `ASPNETCORE_ENVIRONMENT=OpenApi` and regenerates `frontend/src/api-client/`.
- Check committed client drift with `bun run check:api-client-drift`.
- TanStack Router generates `frontend/src/routeTree.gen.ts` from files in `frontend/src/routes/`; edit route files, not the generated tree.

## Local Dev Quirks

- `clips-dev` bind-mounts this repo, runs `dotnet watch` plus the Vite frontend, exposes Vite on `5173`, and routes `/api` and `/auth` to the local API process.
- The dev runner passes `--host 0.0.0.0` to Vite; do not reintroduce a hard-coded local dev hostname in `frontend/vite.config.ts`.
- Frontend API clients default to same-origin requests; set `VITE_API_BASE_URL` only for split frontend/backend deployments.
- API config uses `DatabaseConnectionString` or `ConnectionStrings__DatabaseConnectionString`; Redis defaults to `localhost:6379` if unset.

## Migrations

- Run migrations explicitly with `DatabaseConnectionString="Host=...;Database=...;Username=...;Password=..." bun run migrate`.
- First-time adoption for an existing production database is `bun run migrate:adopt-existing`; it records baseline `V16` without executing the baseline SQL.
- Normal migration runs refuse a non-empty database with no Evolve `changelog`; do not bypass this safety in code changes.
- New SQL migrations belong in `migrations/db/migrations/` using Evolve names such as `V17__game_category_igdb_assets.sql`.

## API Conventions

- JSON is configured as `snake_case` in `api/Program.cs`; generated TypeScript clients reflect that contract.
- Auth endpoints are intentionally at root `/auth` for OAuth callback compatibility; other API endpoints are grouped under `/api`.
- OpenAPI is only mapped in Development, `OpenApi`, or when `OpenApi:Public` is true; background services are disabled in `OpenApi` environment.
- Production image builds the frontend first, publishes the API, installs `ffmpeg`, and serves `frontend/dist` from API `wwwroot`.

## CI / Release

- `.github/workflows/publish-app-images.yml` builds GHCR images on `main` changes touching `api/`, `frontend/`, `migrations/`, `Dockerfile`, `package.json`, or `bun.lock`.
- The workflow also opens an infrastructure PR bumping image tags in `PlusCosmic/infrastructure`; migration-only changes build the migrations image, and app-impacting changes build the app image.
