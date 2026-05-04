# Monorepo Audit: Improvement Opportunities

Audit date: 2026-04-27

This report lists improvements that are not necessarily defects today, but would make the project cleaner, faster, easier to operate, or easier to evolve.

## Monorepo Workflow

### Create a single quality pipeline

Add explicit root scripts and Turbo tasks for:

- `build`
- `typecheck`
- `lint`
- `test`
- `format:check`
- `.NET build`
- OpenAPI generation
- API client regeneration/check

The current repo has the pieces, but they are scattered. A new contributor should be able to run one command before pushing and trust the result.

### Add package-level build scripts to shared TS packages

`@repo/shared` and `@repo/ui` are consumed by apps but do not produce compiled output. Add build scripts that emit JS and declarations into `dist`, then export from `dist`. This makes the packages easier to test, publish, lint, and typecheck independently.

### Standardize workspace naming

The root package is still named `with-vite`, service frontends are named `clips` and `minecraft`, and docs reference old app names. Rename workspaces to stable names such as:

- `@repo/clips-frontend`
- `@repo/minecraft-frontend`
- `@repo/personal-site`
- `@repo/shared`
- `@repo/ui`

This improves Turbo logs, dependency graphs, and future package ownership.

### Use one package-manager vocabulary

The repo is Bun-based, but there is still a `pnpm run build` in the API client package. Replace all package-manager-specific scripts with Bun equivalents or neutral npm lifecycle commands.

### Add repo-level dependency/version policy

Align React, Vite, Mantine, TanStack, TypeScript, ESLint, and Tabler versions across apps where possible. This reduces lockfile churn, duplicated install artifacts, and "works in one app but not the other" behavior.

### Add CODEOWNERS or ownership notes

This repo has several domains: clips, Minecraft, shared .NET auth, shared frontend packages, generated client, and personal site. Lightweight ownership docs would help future work avoid accidental cross-domain regressions.

## Backend Architecture

### Introduce migrations as a first-class package

Use a migrations tool such as DbUp, FluentMigrator, Flyway, or plain versioned SQL scripts. Keep schema, indexes, seed data, and rollback strategy under version control.

This is the highest-leverage backend improvement after fixing hard workflow breakage.

### Extract shared API startup patterns

Clips and Minecraft duplicate CORS, cookie/OAuth configuration, JSON options, OpenAPI schema transformer, health checks, whitelist/user middleware, and static SPA hosting. A shared extension package could reduce drift while keeping app-specific services explicit.

### Move config to typed options with validation

Replace direct `configuration["Key"]` access with typed options classes and `ValidateOnStart`. This would catch missing Discord, Bunny, Backblaze, Docker, Redis, and frontend-origin config before requests hit broken code paths.

### Make background services environment-aware

Hosted services should have explicit enable/disable options and avoid doing real work during OpenAPI generation, tests, or design-time builds. This will make builds quieter and CI more deterministic.

### Add cancellation tokens consistently

Many async database, HTTP, filesystem, and Docker calls do not take request cancellation tokens. Passing tokens through endpoints and services will make shutdowns and aborted requests cleaner.

### Add structured audit events

Docker lifecycle, RCON commands, file edits/deletes, backup syncs, clip deletes, collaborator changes, and webhook updates should produce structured audit records with actor, resource, operation, result, and correlation ID.

### Use named HttpClients

Move Bunny, IGDB, Apex, Discord, and other external integrations to named/typed `HttpClient`s with base addresses, default headers, retries/timeouts where appropriate, and logging policies.

### Push clip filtering to SQL

Title search, tag filtering, date range, viewed/unviewed, ordering, and pagination should be expressed in SQL with proper indexes. That will make clips scale beyond small personal datasets.

### Add database indexes intentionally

Once migrations exist, add indexes for common access paths:

- clips by owner/category/created date
- clip views by user/clip
- tags by normalized name
- clip tags by clip and tag
- playlists by creator/collaborator
- Minecraft servers by owner/container name
- Discord users by Discord ID and username search

### Normalize timestamps

Use `DateTimeOffset.UtcNow` consistently for server-generated timestamps. There is currently a mix of `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, and `DateTimeOffset.Now`.

### Decide how public OpenAPI should be

If public docs are desired, publish a sanitized OpenAPI bundle intentionally. If not, serve OpenAPI only in development/admin contexts.

## Frontend Architecture

### Centralize generated API client creation

Each shared service creates a new generated API client instance. A small API factory would centralize `basePath`, credentials, error handling, and future middleware.

### Normalize error handling

Pick one rule for data services:

- throw and let query hooks/UI handle error states, or
- return typed result objects with explicit `ok/error`.

Avoid mixing swallowed errors with thrown errors. React Query works best when query failures throw.

### Add route-level loading and error boundaries

TanStack Router supports pending/error boundaries. Use them for expensive pages such as clip lists, playlist player, Minecraft files, backups, and console.

### Make the WebSocket layer reusable and robust

There are two WebSocket hooks. Consolidate around one tested hook with:

- absolute URL generation for same-origin deployments
- stable callback refs
- backoff with jitter
- visibility/network awareness
- explicit close reasons
- typed message parsing

### Improve optimistic updates with list-cache updates

Several mutations invalidate broad query keys after optimistic updates. For high-use pages, update list caches directly where straightforward, then invalidate narrowly as a safety net.

### Split large route bundles intentionally

The root build reports >500 kB chunks for clips and Minecraft. TanStack auto code splitting is enabled, but shared dependencies still land in large chunks. Add manual chunks for Mantine, Monaco, router/query/devtools, upload tooling, and video player libraries where useful.

### Keep devtools out of production bundles

The apps depend on TanStack/React devtools packages. Gate devtool rendering and imports behind development-only dynamic imports so production bundles stay smaller.

### Add app-level accessibility checks

The UIs are control-heavy. Add automated checks with Playwright/Axe or Testing Library for dialogs, icon buttons, menu focus, keyboard navigation, color contrast, and form labels.

### Reduce inline styles over time

There is heavy inline style usage in app components. Not wrong, but extracting repeated layout/visual patterns to Mantine theme components, CSS modules, or shared UI primitives would improve consistency and reduce component noise.

## Testing Strategy

### Start with contract and guardrail tests

The first test suite does not need to be broad. Prioritize:

- auth redirect allow-list tests
- whitelist and permission filters
- ownership checks for clips/playlists/Minecraft servers
- file path traversal and symlink behavior
- Docker provisioning parameter generation
- Bunny webhook secret handling
- OpenAPI client generation drift check

### Add frontend integration tests for the riskiest flows

Use Vitest/Testing Library for:

- unauthenticated login screens
- clip upload queue state transitions
- playlist add/reorder/remove flows
- Minecraft file save/delete UI behavior
- console reconnect behavior

### Add a small Playwright smoke suite

A smoke suite should cover:

- personal site renders
- clips unauthenticated and authenticated shell behavior with mocked API
- Minecraft login/server list behavior with mocked API
- major routes do not render blank screens

### Test generated API client compatibility

Add a CI check that regenerates the API client from current OpenAPI output and fails if generated files differ from committed files.

## Operations And Deployment

### Consolidate Dockerfile strategy

Choose one deployment shape per service:

- combined API + frontend container, or
- separate API and static frontend containers.

Then remove stale Dockerfiles and compose files. Keep Docker contexts aligned with the current monorepo layout.

### Add health/readiness distinction

The current health checks are simple. Split liveness from readiness so containers can start before dependencies are ready but only receive traffic after database/Redis/external critical dependencies are usable.

### Add `.dockerignore`

Add per-context or root `.dockerignore` to exclude `node_modules`, `dist`, `.turbo`, `.idea`, `bin`, `obj`, logs, local env files, and local certs from Docker build contexts.

### Pin image versions

Avoid mutable `latest` where practical:

- `itzg/minecraft-server:latest`
- `tecnativa/docker-socket-proxy:latest`
- `oven/bun:alpine`

Pinned images improve reproducibility and incident rollback.

### Document deployment topology

Add a short deployment doc covering:

- where each service runs
- which container image is authoritative
- expected environment variables
- network names
- volumes
- data-protection key storage
- database migrations
- backup restore procedure

## Documentation

### Rewrite README around the current repo

Update README with the actual apps, actual paths, Dapper/Npgsql backend stack, Bun commands, .NET commands, Docker commands, and local environment setup.

### Replace or remove stale agent docs

`CLAUDE.md` still documents old app names and missing test suites. Rewrite it so future AI/human contributors do not follow incorrect paths.

### Add architecture decision records

Capture decisions that will matter later:

- why Bun/Turbo
- why Dapper instead of EF
- why generated `typescript-fetch`
- why cookie auth and Discord OAuth
- why combined service containers
- how Minecraft Docker control is secured
- how Bunny upload signatures work

### Document generated-code ownership

Add a clear rule for `packages/nucleus-api-client`: what files are generated, what files are hand-maintained, how to regenerate, and how CI verifies it.

## Cleanup Opportunities

### Remove generated and local noise from tracked files

Consider untracking IDE metadata and generated docs if not needed:

- `.idea/*`
- `packages/nucleus-api-client/src/docs/*.md`

### Remove unused or stale dependencies

Audit dependencies after workflow fixes. Examples to inspect:

- root runtime dependencies
- OpenAPI generator duplicate packages
- frontend testing packages with no tests
- devtools packages in production dependencies
- `web-vitals` in Minecraft if no reporting endpoint exists

### Introduce formatting check

Root `format` writes files. Add `format:check` for CI and pre-commit use.

### Add local development bootstrap docs/scripts

Make local startup predictable:

- database setup
- Redis setup for clips
- Discord OAuth callback URLs
- local cert generation for `local.pluscosmic.dev`
- Docker socket proxy setup for Minecraft
- seed whitelist file

### Add sample env files

Keep `.env.example` files for each service/frontend. Include safe placeholder values and comments for required vs optional variables.

## Suggested Short-Term Roadmap

1. Fix broken lint/Docker/docs paths so the repo's advertised commands are trustworthy.
2. Add migrations and a local dev database bootstrap.
3. Add the first backend guardrail tests around auth, permissions, file paths, Docker provisioning, and webhooks.
4. Wire OpenAPI generation and client regeneration into a repeatable script.
5. Consolidate frontend API/error/WebSocket utilities.
6. Add deployment docs and data-protection key persistence.
