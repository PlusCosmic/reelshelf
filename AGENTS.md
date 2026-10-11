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
- `clips-dev` has its own database, a one-off copy of prod with its own Bunny videos and `Development-` collections (`scripts/dev-db/README.md`), and migrates it to the checkout's migrations when it starts. A migration edited after dev applied it fails Evolve's checksum check there; fix the dev database rather than the check.
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
- Apex legend detection (`api/ApexLegends/LegendDetection/`) sends the embedded prompt, schema and reference sheet in `Resources/` to a vision model via Microsoft.Extensions.AI, with each of the clip's six Bunny thumbnails as a half-resolution overview plus an enlarged close-up of the bottom-left HUD panel, both made with ffmpeg (`LegendHudCropper`; CI installs ffmpeg for its tests). Runs are queued in `legend_detection_run` and processed by a background service; nothing runs until `LegendDetection:Model` and `LegendDetection:Providers:<provider>:ApiKey` are set, and `LegendDetection:AutoDetect` queues a run when an Apex clip finishes encoding. Each run records its reasoning effort (`LegendDetection:ReasoningEffort` by default, or chosen on `/legend-detection`), and each API instance processes `LegendDetection:Concurrency` runs at once (default 4; dev and prod each have their own queue but share the provider's rate limit). `LegendDetection:EscalationModel` (off when empty) queues an `escalation` run with a stronger model on the same provider when an automatic run sees the owner's HUD but returns no legend or one below `EscalateBelow` (default 0.9); manual and escalation runs are claimed before automatic ones. Each instance escalates by its own config, so set it on dev and prod alike; `LegendDetection:EscalationProvider` escalates to another provider (empty keeps the run's own). New providers are a case in `LegendRecognizerFactory`. The `openai-decisions` provider is OpenAI's Decisions API (`DecisionsLegendRecognizer`, plain HTTP since the .NET SDK lacks it, in public beta): it reuses the `openai` key, defaults to `gpt-6-luna`, takes no reasoning effort, sends `Resources/decisions-prompt.md` with the same images, and asks a `hud_detected` predicate and a `legend` choice over `ApexLegendNames` plus `unidentifiable`; the legend's confidence is the probability the API gives the chosen option. Its prompt version is hashed separately (`DecisionsPromptVersion`). "Run on labelled clips" on `/legend-detection` queues a manual run on every labelled clip with any provider, model and effort, so the usage table scores it.
- Game categories carry a `cloth_color` (#rrggbb) for their book on the library shelf, taken from the IGDB cover by `ClothColor` (ffmpeg downsample, dominant hue, muted). It is worked out when a game is added and backfilled by `GameCategoryClothColorService`; only `https://images.igdb.com` covers are fetched, so custom categories stay null and the frontend falls back.
- Clip transcription (`api/ClipTranscription/`) sends a clip's audio, extracted with ffmpeg as mono 32 kbps AAC from the Bunny HLS stream, to OpenRouter's `audio/transcriptions` endpoint, where the model id picks the provider. The default is `google/gemini-3.5-transcribe` with the game name (and Apex legend names) as `custom_vocabulary`; when it hears nothing in a clip with audio, `ClipTranscription:FallbackModel` (default `deepgram/nova-3`) is tried before the clip is recorded as having no speech. Provider-specific settings go under `provider.options` in `OpenRouterClipTranscriber.ProviderOptions`. On a sample of clips, `gpt-transcribe` often returned nothing, `gpt-4o-transcribe` invented foreign words and `whisper-1` invents text for silence. It only covers clips whose owner is whitelisted, and transcripts are search data reviewed at `/clip-transcription`, never shown to owners. Runs are queued in `clip_transcription_run` and processed by a background service; nothing runs until `ClipTranscription:OpenRouterApiKey` is set, and `ClipTranscription:AutoQueue` queues a run when a whitelisted owner's clip finishes encoding. "Retry no-speech clips" on the review page re-runs clips whose latest empty result came from another model. Each instance processes `ClipTranscription:Concurrency` runs at once (default 2). An instance without the key never claims a run, and only prod has one, so dev never transcribes; it can still show the review page. See `docs/adr/0005-clip-transcription-and-search.md`.
- Clip summaries (`api/ClipSummary/`) turn a succeeded transcript, with the clip's game, title, date and, for Apex clips, the latest detected legend, into a description, mood tags (fixed list in `ClipMoodTags`), quotes and people, through a chat model set by `ClipSummary:Provider`, `Model` and `ReasoningEffort` with keys under `ClipSummary:Providers:<provider>:ApiKey`; new providers are a case in `ClipSummarizerFactory`. The prompt and schema are embedded from `Resources/` and hashed into the prompt version. Tags outside the list and quotes not found in the transcript are dropped. The description and tags are embedded with `ClipSummary:EmbeddingModel` (default `text-embedding-3-small`, always on the `openai` key) into a pgvector `vector(1536)` on the same `clip_summary_run` row, and a run succeeds only when both steps do. The migration needs the `vector` extension in Postgres; Npgsql is set up for it in `ReelshelfDataSource`. `ClipSummary:AutoQueue` queues a summary when an automatic transcription succeeds (and a `retry` summary when a transcription retry hears speech); an instance without the `openai` key never claims a run. Each instance processes `ClipSummary:Concurrency` runs at once (default 2). Summaries are reviewed, rerun and backfilled on `/clip-transcription`, never shown to owners. Tests marked `[PostgresFact]` need `REELSHELF_TEST_DATABASE` (CI runs a pgvector service) and are skipped without it.
- The Discord Activity (`api/DiscordActivity/`, frontend `components/Activity/`) is the watch room from ADR-0006. When the app is served from `*.discordsays.com`, `main.tsx` renders it instead of the router. `POST /api/activity/token` trades the Embedded App SDK's code for a short-lived room token, but only after Discord's activity-instances API confirms the user is in that instance, which needs `DiscordActivity:BotToken` (the Activity reports itself off until it is set). Room endpoints require the `Activity` policy (bearer room token), never the site cookie. Room token claims never use `NameIdentifier`, which the rest of the API reads as an account id. The room itself is a SignalR hub at `/api/activity/hub` (`WatchRoomHub`, camelCase JSON, hand-written client types in `shared/services/watchRoom.ts`). Rooms live in memory on the one prod instance (`WatchRoomRegistry`), so a deploy drops them and clients reconnect into a fresh room; running more than one `clips` instance would break rooms. The first member to connect hosts. Any member can queue clips from their own shelf, in round-robin order by who queued them. Only the host plays, pauses, seeks or skips, and reorders, removes or locks the queue; the host's player reaching the end moves the room on. A queued clip is shown only while its owner is in the room: an absent owner's clips are hidden and dropped when their turn comes. Queue items carry no video id until they play. Anyone in the room, guests included, can react to the playing clip with one of a fixed set of emoji (`WatchRoom.ReactionEmoji`, mirrored in `reactionEmoji`), limited per person; reactions go out once on `Reaction` and are never kept. Followers correct drift by nudging `playbackRate` and seek when more than a second out. The hub accepts the room token from the `access_token` query parameter (WebSockets can't send headers); nowhere else does. Room tokens are renewed through `POST /api/activity/token/refresh`, which checks the instance again. Members pick from their own clips (`GET /api/activity/clips`) and the Activity plays the Bunny HLS stream with hls.js (light build, loaded on first play), not the embed player. The Discord application's URL Mappings need `/` → the prod site, `/discord-cdn` → `cdn.discordapp.com` and `/bunny-cdn` → `vz-cd8f9809-39a.b-cdn.net`. The Activity is built and tested on prod only.
- Account deletion (`api/Users/AccountDeletionService.cs`, Settings → Delete account) marks the account and removes its identities at once; `AccountDeletionBackgroundService` then deletes its Bunny videos and collections and finally the `app_user` row, which cascades. The job runs only where `AccountDeletion:Enabled` is true (default). Dev has its own database and Bunny videos, so deletion can be tried there.
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

<!-- BEGIN AWS Agent Toolkit rules -->
# AWS Guidance for the new AWS experience

This user has signed up for the new AWS experience. This experience lets you sign into AWS using a social provider and requires the following additional context.

Where this guidance conflicts with the project's own instructions, the project's instructions take precedence.

## Context

### Terminology:

- Say "project" instead of "account" — a project contains an AWS account and settings for sharing with other collaborators
- Say "team member" instead of "IAM user" — users are invited by email, not created or federated in IAM
- Say "AWS Settings" when referring to management tasks at [settings.aws.com](https://settings.aws.com/) (project management, billing, team members, spend limits). Users view their actual AWS resources in the AWS Management Console.
- Say "selected Region" when referring to the user's Region — not "home Region"
- The user has a managed IAM experience. This includes a managed service control policies (SCP) and resource control policies (RCP) that govern the use of AWS. They will still need to use IAM to create policies to let services work with each other. If there are questions about the SCPs or RCPs, go to the documentation at https://docs.aws.amazon.com/accounts/latest/reference/scps-and-rcps-for-projects.html

### Constraints:

- All projects share a single AWS Region determined by the user's contact address. Resources cannot be created in other Regions
- When developing:
  - MUST create all Regional resources in the project's assigned Region
  - You CAN create AWS WAF and Cloudwatch Logs resources in us-east-1 when there are global resources (like a global WAF instance) that require a connection to dependencies in us-east-1. You should not use these for any other reason, because resources in the selected Region will provide lower cost (due to no cross-Region traffic), increased availability (due to no cross-Region traffic), and easier manageability (due to not needing to look in another Region). When you need to do an inventory of resources, you need to look in both the selected Region and us-east-1 for Cloudwatch Logs or WAF resources.
  - MUST NOT attempt to create Lambda, API Gateway, or other Regional resources in any other Region
  - MUST direct users to confirm their Region in AWS Settings > View all projects > Overview > Additional Info > Region. If the user cannot confirm their Region, check in ~/.aws/config
  - MUST NOT use Lambda@Edge — excluded from both Lambda and CloudFront
  - MUST NOT use CloudFormation StackSets — no multi-account or multi-Region deployments
  - MUST NOT attempt cross-Region actions — no cross-Region replication for DynamoDB/S3/RDS, no multi-Region KMS keys
  - MUST NOT use Route 53 cross-Region routing — geolocation, latency-based, and failover routing policies are not available
  - CloudFront is a global service and its actions ARE allowed in `us-east-1`. A user can create a CloudFront distribution pointing to their project-region Lambda function URL or API Gateway. However, Lambda and API Gateway themselves MUST NOT be created in `us-east-1` — they must be in the project Region.
  - Reduced availability in `eu-north-1` specifically: Amazon Rekognition, Amazon Textract, Amazon Personalize, AWS App Runner are not available in that Region.
- IAM permissions for human access are managed by AWS. Don't assign roles to team members unless absolutely necessary
- The user may have a spend limit if they are on the paid plan. The limit that pauses their project if it's exceeded. If resources suddenly become inaccessible, ask if they have a spend limit configured. Only project owners can modify a spend limit.
- When developing:
  - MUST ask about spend limit status if the user reports sudden "Access Denied" errors on operations that previously worked
  - MUST direct users to check spend status in AWS Settings > Billing
  - MUST check if a user has upgraded their account to the paid plan
  - MUST ask the user if they want to clean up the successfully created resources or keep them to reduce cost
- The user sets up billing, creates spend limits, and retrieves and pays invoices in AWS Settings. The user creates budgets and optimizes their costs in the AWS Billing and Cost Management console
- Not all AWS services are available. If a service isn't working, do the following:
  1. Run the command `aws freetier get-account-plan-state`
  2. If accountPlanType": "FREE", check the [Free Tier supported services list](https://docs.aws.amazon.com/accounts/latest/reference/supported-services-sign-up-new.html#supported-services-free-tier) next,
  3. If accountPlanType": "PAID", check the [Paid Tier supported services list](https://docs.aws.amazon.com/accounts/latest/reference/supported-services-sign-up-new.html#supported-services-paid-plan).
  4. If neither list shows the service, check the [Not supported for this experience list](https://docs.aws.amazon.com/accounts/latest/reference/supported-services-sign-up-new.html#unsupported-services). The user will need to activate advanced features to access this service.
- Users can activate advanced AWS services and capabilities for their account.
- Before starting a task, check whether a relevant AWS skill is available. Load the skill with retrieve_skill and prefer its guidance over general knowledge.

### Help level

- help_level (required): LOW, MEDIUM, or HIGH. While a user is building, you MUST ask the user: "How much guidance would you like from me? Low (I only flag security risks), medium (I ask a couple of clarifying questions if something seems off), or high (I explain what I'm doing, suggest alternatives, and flag best practices)."

You CAN update this rule file to save a user's help_level.

Constraints for each level:

**LOW:**

- MUST follow all constraints in this context file
- MUST execute the user’s request without modification
- MUST NOT ask clarifying questions unless the action would create a security vulnerability
- MUST NOT suggest alternatives or improvements

**MEDIUM:**

- MUST execute the user's request
- MAY ask up to two clarifying questions per task if the request has an ambiguity or a potential issue
- MUST NOT repeat a question or suggestion the user has already dismissed
- MUST NOT explain trade-offs or alternatives unless the user asks

**HIGH:**

- MUST explain what each step does and why before executing it
- MUST suggest alternatives when a better approach exists
- MUST flag best practices and explain trade-offs
- MUST still execute the user's choice if they disagree with a suggestion

<!-- END AWS Agent Toolkit rules -->
