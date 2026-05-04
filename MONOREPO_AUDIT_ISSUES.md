# Monorepo Audit: Issues And Problems

Audit date: 2026-04-27

This report records defects, broken workflows, risky anti-patterns, and questionable decisions found in the current monorepo. It intentionally excludes "nice to have" cleanup unless the current state is likely to cause operational, security, correctness, or maintenance problems.

## Verification Snapshot

- `bun run build` succeeds, but only four workspaces participate in the build.
- `bun run lint` fails immediately because `packages/ui` has a lint script but no ESLint v9 flat config.
- `services/clips/frontend bun run lint` fails with five lint errors.
- `services/minecraft/frontend bun run lint` fails because there is no ESLint flat config.
- `services/clips/frontend bun run typecheck` succeeds.
- `services/minecraft/frontend bun run typecheck` succeeds.
- `bunx turbo run typecheck` fails because `typecheck` is not declared in `turbo.json`.
- `dotnet build services/clips/api/Nucleus.Clips.csproj` and `dotnet build services/minecraft/api/Nucleus.Minecraft.csproj` both succeed when run separately.
- Running both .NET API builds in parallel can fail on a shared `Nucleus.Shared` output file lock.
- There are no first-party test files found outside ignored/generated dependency folders.

## Critical

### API compose files reference Docker paths that do not exist

Both API-level compose files use a build context of `..` and Dockerfiles named `Nucleus.Clips/Dockerfile` or `Nucleus.Minecraft/Dockerfile`:

- `services/clips/api/compose.yaml:4-5`
- `services/minecraft/api/compose.yaml:4-5`

Those paths do not exist in this repo layout. The real project files are under `services/*/api`, and the working service-level Dockerfiles are `services/clips/Dockerfile` and `services/minecraft/Dockerfile`. Anyone using the API compose files as documented deployment artifacts will get a build failure before the app starts.

### The Minecraft frontend Dockerfile is for an old repo layout

`services/minecraft/frontend/Dockerfile` copies package manifests from `apps/minecraft`, `apps/clips`, `apps/home`, and `apps/dropzone`, then copies `/app/apps/minecraft/dist` into nginx:

- `services/minecraft/frontend/Dockerfile:10-13`
- `services/minecraft/frontend/Dockerfile:27`
- `services/minecraft/frontend/Dockerfile:33`

None of those app paths match the current repo, where Minecraft lives at `services/minecraft/frontend`. This Dockerfile is stale and cannot build the current application.

### Root lint is broken

The root `bun run lint` delegates to Turbo, and Turbo includes `@repo/ui`. `packages/ui/package.json` defines `lint`, but there is no `eslint.config.js` in that package:

- `package.json:8`
- `packages/ui/package.json:5-7`

With ESLint 9, that fails before most workspaces are checked. This makes the advertised repo-level lint command unusable and creates a false sense of coverage for changes.

### The project has no first-party automated tests

No `*.test.*`, `*.spec.*`, or first-party `tests/` files were found outside ignored/generated dependency folders. This is high-risk because the repo includes authentication, authorization, Docker control, file editing, backup sync, media upload, webhooks, playlist mutation logic, and generated client/server contracts.

The impact is not theoretical: there are multiple verified broken workflows and likely security/correctness bugs that tests would have caught.

### OpenAPI generation starts real application services during build

Both .NET API projects enable OpenAPI document generation on build. During `dotnet build`, the app is instantiated enough for hosted services and infrastructure dependencies to run:

- `services/clips/api/Nucleus.Clips.csproj` enables `OpenApiGenerateDocumentsOnBuild`.
- `services/minecraft/api/Nucleus.Minecraft.csproj` enables `OpenApiGenerateDocumentsOnBuild`.
- Hosted services are registered unconditionally in `services/clips/api/Program.cs:165-168` and `services/minecraft/api/Program.cs:119`.

Observed during build:

- Static file middleware warns that `wwwroot` does not exist in API projects.
- Data protection attempts key-ring writes and logs read-only filesystem errors.
- `ClipStatusRefreshService` attempts to connect to PostgreSQL during build and logs socket permission errors.

Builds can still report success, but they are executing runtime work in a build/document-generation path. That is fragile in CI, noisy locally, and risky for environments with reachable production-like dependencies.

## High

### Cookie authentication is not backed by persistent data-protection keys

The API builds logged data-protection key-ring errors and unencrypted key warnings. The runtime containers do not configure persisted ASP.NET Data Protection keys, yet both APIs use cookie auth:

- `services/clips/api/Program.cs:209-231`
- `services/minecraft/api/Program.cs:189-209`

In production, this can invalidate auth cookies on container restart or across replicas. It can also break OAuth correlation/state validation and make rolling deployments unreliable.

### OAuth return URL allow-list is too broad

Both auth endpoint implementations allow any host equal to `pluscosmic.dev` or ending in `.pluscosmic.dev`, plus localhost:

- `services/clips/api/Auth/AuthEndpoints.cs:84-107`
- `services/minecraft/api/Auth/AuthEndpoints.cs:84-107`

That allows redirects to any current or future subdomain under the parent domain. If any subdomain is compromised, delegated, parked, or user-controlled, the login flow can be used as an open redirect target within a trusted domain family. The allow-list should be explicit per app/environment.

### Bunny webhook authentication is optional

The Bunny webhook endpoint only rejects bad secrets when `BunnyWebhookSecret` is configured:

- `services/clips/api/Bunny/BunnyWebhookEndpoints.cs:38-50`

If the secret is missing in any environment, the webhook becomes publicly callable. The endpoint fetches Bunny metadata and updates local clip metadata. The test endpoint is also unauthenticated:

- `services/clips/api/Bunny/BunnyWebhookEndpoints.cs:11-13`

Webhook auth should fail closed in production.

### Docker control endpoint design is very sensitive

The Minecraft API can create, start, stop, destroy containers and volumes through a Docker socket proxy:

- `services/minecraft/api/Services/DockerContainerService.cs:13-21`
- `services/minecraft/api/Services/DockerContainerService.cs:106-185`
- `services/minecraft/api/compose.yaml:45-76`

The proxy disables many capabilities, which is good, but the app still has container lifecycle and volume deletion power. This needs stronger guardrails than ordinary app endpoints: strict input validation, audit logging, rate limiting, explicit admin boundaries, network isolation, and operational runbooks.

### Minecraft container port binding cannot support multiple servers

Every provisioned Minecraft container binds host port `25565`:

- `services/minecraft/api/Services/DockerContainerService.cs:170-175`

This makes only one server container usable per Docker host unless all others avoid provisioning or use external routing not represented here. The model supports multiple servers, but the provisioning implementation does not.

### Minecraft file path containment check is prefix-based and can be bypassed by sibling prefixes

`FileService.GetSafePath` verifies containment with `fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)`:

- `services/minecraft/api/Services/FileService.cs:10-31`

If `basePath` is `/srv/minecraft/server`, a resolved path under `/srv/minecraft/server2` also starts with that string. The check needs a path-boundary-aware comparison, usually by ensuring the base path ends with a separator before comparing or using `Path.GetRelativePath` and rejecting `..` and rooted paths.

### File editor endpoints can write arbitrary text files under the server data root

The file service will create parent directories and write content to any safe path under `PersistenceLocation`:

- `services/minecraft/api/Services/FileService.cs:100-119`

This is powerful enough to alter server config, plugin config, scripts, or other app-owned data. It is protected by ownership and permission checks, but there is no file allow-list, extension policy, symlink policy, audit severity distinction, or concurrency strategy.

### OpenAPI and health endpoints are public in both APIs

Both APIs map OpenAPI at root and do not apply auth to it:

- `services/clips/api/Program.cs:65-77`
- `services/minecraft/api/Program.cs:59-71`

Public OpenAPI is not always wrong, but in this repo it exposes operational surfaces for Docker management, RCON console commands, file editing, backups, upload creation, user endpoints, and webhooks. That increases discovery risk.

### Shared .NET project output can lock under parallel builds

Running clips and Minecraft builds in parallel hit:

`IOException: The process cannot access the file 'packages/nucleus-shared/obj/Debug/net10.0/ref/Nucleus.Shared.dll' because it is being used by another process.`

This matters because monorepo CI commonly parallelizes build jobs. If API builds share the same checkout and configuration, they can fight over shared project outputs unless isolated output paths or solution-level build orchestration are used.

### Database schema/migrations are absent from the repo

There are many raw Dapper statements against tables such as `clip`, `clip_collection`, `discord_user`, `minecraft_server`, `playlist_clips`, `game_category`, and others, but no SQL migrations or schema files were found.

This makes onboarding, local development, CI integration tests, disaster recovery, and production drift management fragile. The README also says "Entity Framework" even though the code uses Dapper/Npgsql.

### API client package mixes package managers

The repo declares `bun@1.3.9` as the package manager, but `packages/nucleus-api-client/package.json` has:

- `prepack: pnpm run build` at `packages/nucleus-api-client/package.json:11-14`

That script will fail in environments without pnpm and is inconsistent with the rest of the workspace.

### The generated API client is built from a static snapshot, not from the current APIs

The client package includes `Nucleus.json` and generated source, but the build pipeline does not regenerate it from the current clips and Minecraft APIs. The .NET APIs generate OpenAPI JSON into their own `bin` folders during build, and the TS client package builds whatever is already in `packages/nucleus-api-client/src`.

This allows silent contract drift between API endpoints and frontend code.

## Medium

### Turbo is not orchestrating key quality gates

`turbo.json` only defines `build`, `build:api`, `lint`, and `dev`:

- `turbo.json:3-17`

There is no `typecheck`, `test`, `.NET build`, OpenAPI generation, or API client regeneration task. The two frontend typecheck scripts exist but cannot be run through Turbo. Root build also skips packages without build scripts.

### Lint scripts miss most React files

Both service frontends define lint as `eslint "src/**/*.ts"`:

- `services/clips/frontend/package.json:6-11`
- `services/minecraft/frontend/package.json:6-11`

That excludes nearly all `.tsx` components and routes. The repo's lint coverage is much lower than it appears.

### Minecraft frontend has no ESLint config

`services/minecraft/frontend` references `@repo/eslint-config`, but has no `eslint.config.js`. `bun run lint` fails under ESLint 9.

### Clips lint currently fails

`services/clips/frontend bun run lint` fails on import ordering and array type rules:

- `services/clips/frontend/src/hooks/clips/useRelatedClips.ts`
- `services/clips/frontend/src/hooks/queries.ts`

These are small issues, but they keep the package-level lint gate red.

### Shared ESLint config is legacy `.eslintrc` format

`packages/eslint-config/index.js` exports legacy config shape (`env`, `parser`, `extends`) while the repo uses ESLint 9 flat config:

- `packages/eslint-config/index.js`

This explains why workspaces that try to rely on the package still fail unless they provide their own flat config.

### Documentation describes apps and paths that do not exist

`CLAUDE.md` refers to `apps/home`, `apps/clips`, and test suites that are not present:

- `CLAUDE.md:25-32`
- `CLAUDE.md:86-94`
- `CLAUDE.md:97-122`

The real app is `apps/personal-site`, and service frontends live under `services/*/frontend`. This is likely to mislead future agents and developers.

### README misstates backend technology

README says backend is ".NET, Entity Framework":

- `README.md:24-28`

The APIs use Dapper and Npgsql directly. This is not cosmetic; it changes how maintainers should reason about migrations, tracking, transactions, and query safety.

### User-facing delete action lies in Minecraft UI

The Minecraft server card delete modal shows a success notification without deleting anything:

- `services/minecraft/frontend/src/components/servers/ServerCard.tsx:22-40`

This is a direct UX correctness bug.

### WebSocket URL generation is wrong for relative API base URLs

`apiConfig.baseUrl` defaults to `"/api"`. `getConsoleWebSocketUrl` only replaces `http` with `ws`, so the default result is a relative URL:

- `packages/shared/src/services/minecraft.ts:105-107`

`new WebSocket("/api/...")` is invalid in browsers; WebSocket constructors require an absolute `ws://` or `wss://` URL. This likely breaks the console in the default same-origin deployment mode.

### WebSocket reconnection hook has unstable callback dependencies

`useMinecraftConsole` has `connect` depending on `addEntry` and `handleMessage`, and the effect depends on `connect` and `disconnect`:

- `services/minecraft/frontend/src/hooks/useMinecraftConsole.ts:122-181`
- `services/minecraft/frontend/src/hooks/useMinecraftConsole.ts:227-236`

Because callbacks can be recreated as state/props change, the hook can reconnect more often than intended. This should be hardened before relying on it for live server console operations.

### Bunny API calls do not consistently enforce HTTP success

`BunnyService` calls `EnsureSuccessStatusCode` for update/delete, but not for create/get/list:

- `services/clips/api/Bunny/BunnyService.cs:35-37`
- `services/clips/api/Bunny/BunnyService.cs:45-48`
- `services/clips/api/Bunny/BunnyService.cs:59-61`
- `services/clips/api/Bunny/BunnyService.cs:67-69`

Failed Bunny responses can become null/deserialization errors without the original status code/body context.

### Clip list filtering and pagination are done in memory

`ClipService.GetClipsForCategory` fetches all clips for an owner/category and then applies title search, date range, viewed filtering, sorting, and pagination in memory:

- `services/clips/api/Core/ClipService.cs:306-345`

This will degrade as clip counts grow and can produce expensive request latency. It also duplicates logic that belongs in indexed SQL.

### Tag filtering uses substring matching on aggregated tag strings

`ClipsStatements.GetClipsWithTagsByOwnerAndCategory` builds `STRING_AGG(t.name, ',') LIKE @tag` conditions:

- `services/clips/api/Core/ClipsStatements.cs:59-80`

That can match partial tags (`art` matching `cart`) and is harder for the database to optimize. Tag filters should use relational joins or `EXISTS` predicates.

### Pagination accepts unsafe values

Clip list pagination uses `Skip((page - 1) * pageSize).Take(pageSize)` with no visible validation:

- `services/clips/api/Core/ClipService.cs:343-345`

Zero/negative values and extremely large values should be rejected or clamped at the API boundary.

### Query/client services swallow errors inconsistently

Several shared frontend services catch errors and return `null` or `[]`, while others rethrow:

- `packages/shared/src/services/playlists.ts:25-41`
- `packages/shared/src/services/playlists.ts:47-197`
- `packages/shared/src/services/clips.ts`
- `packages/shared/src/services/user.ts`

This makes UI behavior inconsistent and can hide auth, network, and server failures as "empty data".

### Shared frontend HTTP helpers are unused/inconsistent with generated client usage

`packages/shared/src/services/http.ts` defines custom fetch helpers, while many services instantiate generated OpenAPI clients directly. The project now has two HTTP patterns with different timeout, error, and credentials behavior.

### Packages are exposed as source `.ts` files

`@repo/shared` exports source files directly:

- `packages/shared/package.json`

This works under Vite/Bun but is brittle for Node tooling, publishing, type emission, and non-bundler consumers. `@repo/ui` also has no build script and no declarations output.

### Root workspace name is still template-derived

The root package name is `with-vite`:

- `package.json:1-4`

That is not a runtime bug, but it is a maintenance smell in repo metadata and generated logs.

### IDE metadata is tracked

Several `.idea` files are tracked despite `.gitignore` ignoring `.idea` now. This causes editor-specific churn and can leak developer-local assumptions.

### Generated API documentation is tracked inside generated client source

`packages/nucleus-api-client/src/docs/*.md` is tracked. These generated docs are noisy, include placeholder TODOs and sample `console.log` snippets, and increase review noise. If they are needed, they should be regenerated deterministically; otherwise, omit them from the client output.

## Lower Severity But Still Worth Fixing

### Dependency versions are inconsistent across sibling frontends

Clips and Minecraft use different patch/minor versions for Mantine, TanStack Router/Query, Vite, React, and related packages. This increases bundle duplication and makes shared components harder to validate.

### Dockerfiles use preview .NET base images

The Dockerfiles use `mcr.microsoft.com/dotnet/*:10.0-preview` while the installed local SDK is `10.0.101`. If .NET 10 is intended, move to stable tags; if preview is intentional, document why.

### Docker builds include more workspace source than needed

The service-level Dockerfiles copy entire `packages/` and both service frontends before install/build:

- `services/clips/Dockerfile:6-16`
- `services/minecraft/Dockerfile:5-15`

This increases build context size and invalidates cache more often than necessary.

### Root dependencies look misplaced

The root package lists runtime UI dependencies like Mantine and `@repo/nucleus-api-client`:

- `package.json:11-15`

The root is not an app. Runtime dependencies should live in the workspaces that import them.

### Local environment files are present

`services/clips/frontend/.env.development` and `.env.production` exist in the working tree. They are ignored by `.gitignore`, but their presence is still a local-secret hygiene risk and should be checked before sharing archives or screenshots.

## Recommended Fix Order

1. Repair the build/deploy artifacts: compose paths, stale frontend Dockerfile, root lint, Turbo tasks.
2. Add schema/migration management before deeper API work.
3. Fix auth/data-protection/webhook/Docker-control guardrails.
4. Add a minimal test suite around auth, ownership checks, file path safety, Docker provisioning parameters, upload creation, and playlist mutations.
5. Move OpenAPI generation out of normal runtime startup side effects and wire client regeneration into CI.
6. Clean up documentation so humans and agents stop following stale paths.
