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
- `api/whitelist.json` does not gate access. Any Discord or Twitch account can sign in; entries match a linked identity by `DiscordId` or `TwitchId`, and listed users get unlimited clip storage and may pin a role. Everyone else gets the default tier from `Storage:DefaultLimitBytes` (25 GiB). See `docs/adr/0002-open-signup-with-storage-tiers.md`.
- Sign-in providers are Discord and Twitch (`DiscordClientId`/`DiscordClientSecret`, `TwitchClientId`/`TwitchClientSecret`). Accounts live in `app_user`; each provider login is a `user_identity` row, and one account may link both. All four credentials are required: the API throws on startup when any is missing or blank, so a misconfigured deployment fails immediately instead of returning a 500 from `/auth/{provider}/login`. The `OpenApi` environment is exempt because document generation builds the app without secrets. See `docs/adr/0003-linked-identities.md`.
- Twitch clip import (`api/Twitch/`) lists and copies a user's own Twitch clips server-side. It needs the `channel:manage:clips` scope, which is in the default `Twitch:Scopes`; Twitch tokens are stored encrypted on `user_identity` and refreshed on demand. See `docs/adr/0004-twitch-clip-import.md`.
- Account mail (linked sign-in notices, storage nearly full) goes through Resend via `ResendApiKey` and `Email__From`; with no key the API logs what it would have sent. See `api/Email/`.
- Apex legend detection (`api/ApexLegends/LegendDetection/`) sends the embedded prompt, schema and reference sheet in `Resources/` to a vision model via Microsoft.Extensions.AI, with each of the clip's six Bunny thumbnails as a half-resolution overview plus an enlarged close-up of the bottom-left HUD panel, both made with ffmpeg (`LegendHudCropper`; CI installs ffmpeg for its tests). Runs are queued in `legend_detection_run` and processed by a background service; nothing runs until `LegendDetection:Model` and `LegendDetection:Providers:<provider>:ApiKey` are set, and `LegendDetection:AutoDetect` queues a run when an Apex clip finishes encoding. Each run records its reasoning effort (`LegendDetection:ReasoningEffort` by default, or chosen on `/legend-detection`), and each API instance processes `LegendDetection:Concurrency` runs at once (default 4; dev and prod share the queue, so both count against the provider rate limit). `LegendDetection:EscalationModel` (off when empty) queues an `escalation` run with a stronger model on the same provider when an automatic run sees the owner's HUD but returns no legend or one below `EscalateBelow` (default 0.9); manual and escalation runs are claimed before automatic ones. Each instance escalates by its own config, so set it on dev and prod alike. New providers are a case in `LegendRecognizerFactory`.
- Game categories carry a `cloth_color` (#rrggbb) for their book on the library shelf, taken from the IGDB cover by `ClothColor` (ffmpeg downsample, dominant hue, muted). It is worked out when a game is added and backfilled by `GameCategoryClothColorService`; only `https://images.igdb.com` covers are fetched, so custom categories stay null and the frontend falls back.
- Clip transcription (`api/ClipTranscription/`) sends a clip's audio, extracted with ffmpeg as mono 32 kbps AAC from the Bunny HLS stream, to OpenRouter's `audio/transcriptions` endpoint, where the model id picks the provider. The default is `google/gemini-3.5-transcribe` with the game name (and Apex legend names) as `custom_vocabulary`; when it hears nothing in a clip with audio, `ClipTranscription:FallbackModel` (default `deepgram/nova-3`) is tried before the clip is recorded as having no speech. Provider-specific settings go under `provider.options` in `OpenRouterClipTranscriber.ProviderOptions`. On a sample of clips, `gpt-transcribe` often returned nothing, `gpt-4o-transcribe` invented foreign words and `whisper-1` invents text for silence. It only covers clips whose owner is whitelisted, and transcripts are search data reviewed at `/clip-transcription`, never shown to owners. Runs are queued in `clip_transcription_run` and processed by a background service; nothing runs until `ClipTranscription:OpenRouterApiKey` is set, and `ClipTranscription:AutoQueue` queues a run when a whitelisted owner's clip finishes encoding. "Retry no-speech clips" on the review page re-runs clips whose latest empty result came from another model. Each instance processes `ClipTranscription:Concurrency` runs at once (default 2). Dev and prod share the queue, and an instance without the key never claims a run, so only prod (the one with the key) processes it; dev can still show the review page. See `docs/adr/0005-clip-transcription-and-search.md`.
- The Discord Activity (`api/DiscordActivity/`, frontend `components/Activity/`) is the watch room from ADR-0006. When the app is served from `*.discordsays.com`, `main.tsx` renders it instead of the router. `POST /api/activity/token` trades the Embedded App SDK's code for a short-lived room token, but only after Discord's activity-instances API confirms the user is in that instance, which needs `DiscordActivity:BotToken` (the Activity reports itself off until it is set). Room endpoints require the `Activity` policy (bearer room token), never the site cookie. Room token claims never use `NameIdentifier`, which the rest of the API reads as an account id. The room itself is a SignalR hub at `/api/activity/hub` (`WatchRoomHub`, camelCase JSON, hand-written client types in `shared/services/watchRoom.ts`). Rooms live in memory on the one prod instance (`WatchRoomRegistry`), so a deploy drops them and clients reconnect into a fresh room; running more than one `clips` instance would break rooms. The first member to connect hosts. Any member can queue clips from their own shelf, in round-robin order by who queued them. Only the host plays, pauses, seeks or skips, and reorders, removes or locks the queue; the host's player reaching the end moves the room on. A queued clip is shown only while its owner is in the room: an absent owner's clips are hidden and dropped when their turn comes. Queue items carry no video id until they play. Anyone in the room, guests included, can react to the playing clip with one of a fixed set of emoji (`WatchRoom.ReactionEmoji`, mirrored in `reactionEmoji`), limited per person; reactions go out once on `Reaction` and are never kept. Followers correct drift by nudging `playbackRate` and seek when more than a second out. The hub accepts the room token from the `access_token` query parameter (WebSockets can't send headers); nowhere else does. Room tokens are renewed through `POST /api/activity/token/refresh`, which checks the instance again. Members pick from their own clips (`GET /api/activity/clips`) and the Activity plays the Bunny HLS stream with hls.js (light build, loaded on first play), not the embed player. The Discord application's URL Mappings need `/` → the prod site, `/discord-cdn` → `cdn.discordapp.com` and `/bunny-cdn` → `vz-cd8f9809-39a.b-cdn.net`. The Activity is built and tested on prod only.
- The terms of service and privacy policy are `frontend/src/routes/terms.tsx` and `privacy.tsx` (shared layout in `components/Reelshelf/LegalPage.tsx`). When a change collects new user data, sends it to a new third party, or changes retention or deletion, update the privacy policy and its "last updated" date in the same PR.
- Every endpoint must carry its own `RequireAuthorization()`; there is no global auth middleware after the whitelist gate was removed.
- OpenAPI is only mapped in Development, `OpenApi`, or when `OpenApi:Public` is true; background services are disabled in `OpenApi` environment.
- Production image builds the frontend first, publishes the API, installs `ffmpeg`, and serves `frontend/dist` from API `wwwroot`.

## CI / Release

- `.github/workflows/ci.yml` runs on every PR and on pushes to `main`: `Frontend` (`bun run check`), `API` (`dotnet build` + `dotnet test` on `Reelshelf.sln`), and `API client drift` (`bun run check:api-client-drift`). All three must pass locally before opening a PR.
- `main` is protected by the "Protect main" repository ruleset: no direct pushes, force pushes, or deletions; changes land only via PRs with the three CI checks green. Repository admins can bypass only from the PR merge box, and GitHub records every bypass.
- `.github/workflows/publish-app-images.yml` builds GHCR images on `main` changes touching `api/`, `frontend/`, `migrations/`, `Dockerfile`, `package.json`, or `bun.lock`.
- The workflow also opens an infrastructure PR bumping image tags in `PlusCosmic/infrastructure`, closing any older open bump PR. It decides what to build by diffing against the commits of the images currently deployed in that repo's `services/clips.yml`, not the previous push, so a superseded bump PR can't drop a migration; migration changes build the migrations image, and app-impacting changes build the app image. Runs are serialized.

## Agent skills

### Issue tracker

Issues and PRDs are tracked in GitHub Issues for `PlusCosmic/reelshelf` using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Triage uses the default five-label vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

This repo uses a single-context domain documentation layout with root `CONTEXT.md` and root `docs/adr/` when present. See `docs/agents/domain.md`.
