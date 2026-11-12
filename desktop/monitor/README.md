# Reelshelf Monitor

A Mac app, drawn with Rune, that shows everything about Reelshelf in one place. It signs in to Reelshelf as the
**monitor** (a device token only Admins' monitor tokens can use on the admin and ops endpoints) and shows:

- **Overview**: a card per section with its headline numbers and, in red, what needs attention: failures in the last
  hour, running jobs whose worker went quiet, a pending job waiting over 10 minutes, no object store configured, a cost
  provider that couldn't be read, a workflow failing on main.
- **Storage**: totals, the object store in use, clips by tier, the top accounts and exports (`GET /api/admin/ops/storage`).
- **Transcoding**: the queue by job type, the overflow pools, the AAC backfill and the last failures, each clip id
  copyable (`GET /api/admin/ops/transcoding`).
- **AI pipelines**: each run queue by trigger, its last failures, and spend by model over 24 hours and 30 days
  (`GET /api/admin/ops/ai`).
- **Costs**: this month's spend across providers and Stripe's net revenue, then a card per provider set up on the API
  (AWS, OpenRouter, OpenAI, Anthropic; Stripe's revenue) with this month, last month, its last 30 days as a chart and
  its breakdown by service or model, a red line when its latest read failed (the last numbers stay), and which
  providers aren't set up and where their keys go on the API (`GET /api/admin/ops/costs`). The API reads providers in
  the background, hourly and AWS every 6 hours; billing keys live only there, never in the app.
- **Dashboards**: Grafana's dashboards drawn natively (below).
- **Releases**: from GitHub (below): what is deployed, what is coming, the uploader's latest tag and the open pull
  requests. Overview's Releases card says how far prod's app is behind main and turns red when a workflow on main
  failed.
- **Settings**: the account it is signed in as, Sign out, Grafana's address and token, and the GitHub token.

Admin is a placeholder. The section showing is read again every 30 seconds (and on Refresh); the API caches each
answer for 15 seconds. GitHub is read at most every 2 minutes (each read is a couple of dozen requests against
the token's 5,000 an hour), and at once on Refresh.

## Releases

The app calls GitHub's REST API itself, with no change to Reelshelf's API, using a fine-grained, read-only token
(Contents, Pull requests and Actions, read, on PlusCosmic/reelshelf and PlusCosmic/infrastructure) entered once in
Settings and kept in the Keychain (`github-token`); debug builds take `GITHUB_TOKEN` from the environment first. Each
part reads and fails on its own, so a token that can't read infrastructure still shows reelshelf's CI and PRs.

- **Deployed**: the `ghcr.io/pluscosmic/reelshelf*` image tags in infrastructure's `services/clips.yml` (prod),
  `clips-dev.yml` (dev) and every `services/reelshelf-worker*.yml`, plus the overflow pools' worker
  (`Overflow__<Pool>__WorkerImage=`), as `publish-app-images.yml` reads and bumps them. Each `sha-` tag is compared
  with main (`/compare/<sha>...main`): its commit's title, author and date, how many commits main is ahead, and those
  commits (prod's app listed at first; other rows open from their "N behind" button). Dev's runner image is tagged
  `main` and isn't a commit.
- **Coming**: the open `automation/app-images-*` PR in infrastructure with its age and checks, and the latest run on
  main of CI, Publish Application Images and Windlass (red when the latest finished run failed, even while a newer one
  runs).
- **Uploader**: the highest `uploader-v*` tag and `publish-uploader.yml`'s last run.
- **Open pull requests** on reelshelf, each with its checks (the latest run of each workflow on its head commit).

Checks are read from Actions runs, so the token needs no Checks permission. Every title opens on GitHub in the browser.

## Signing in

Sign in starts the device flow (`POST /api/devices/authorize` with `client: "monitor"`), shows the code and opens
`/device` on it, and polls `/api/devices/token` until the code is approved there. The `rsd_` token goes to the login
Keychain (service `app.reelshelf.monitor`, one item per site, so a debug build pointed at dev keeps its own) and nowhere
else; on Linux, where CI runs the tests, it is kept in memory. Sign out is `DELETE /api/devices/current`; removing the
device in Settings → Devices on the site signs it out too.

Every request carries `X-Reelshelf-Client: monitor/<version>`. A 401 signs the app out, a 426 asks for an update, and
the ops sections say why they can't show when the API answers 404 (the `ops-monitor` flag is off for the account) or
403 (the account isn't an Admin).

## Run it

```sh
cd desktop
cargo run -p reelshelf-monitor
```

Release builds use `https://reelshelf.app`; debug builds read `REELSHELF_API_BASE_URL` (a local API, say) first.

`REELSHELF_MONITOR_SIGN_IN=1` starts signing in at launch, as Sign in does.
`REELSHELF_MONITOR_SECTION` (`storage`, `transcoding`, `ai-pipelines`, ...) opens on that section; with Rune's
`--snap file.png --theme dark --at 6` it renders the live window once it has read its numbers.

Grafana is reachable on the tailnet only. Enter its address and a Viewer service account token once in Settings; both
are kept in the Keychain. Debug builds still take `GRAFANA_URL` and `GRAFANA_TOKEN` from the environment first.

Without Reelshelf or Grafana, `REELSHELF_MONITOR_FIXTURES=1` shows a fixture scene (`REELSHELF_MONITOR_SCENE`: one of
`fixtures::SCENES`, such as `overview`, `transcoding`, `signing-in` or `dashboard-pipeline`).

To render one real dashboard headless (for screenshots, without the window):

```sh
GRAFANA_URL=… GRAFANA_TOKEN=… cargo run -p reelshelf-monitor --example snap -- reelshelf-api 24h Latency 300 \
  -- --snap api-dark.png --theme dark --at 0.5
```

(the dashboard's uid, the range, and optionally a panel title and the pointer's x for the hover readout; everything
after the second `--` goes to Rune).

Tokens are only ever sent to the service they belong to; the app never logs or shows them.

## Layout

- `src/session.rs`: signing in and what refusals mean, over the `MonitorApi` trait (`reelshelf-api`'s `ApiClient`, or a
  fake in its tests); `src/secrets.rs`: the Keychain; `src/fetch.rs`: every call off the UI thread, and the 30 s poll.
- `src/ui/`: a page per section (`overview.rs`, `storage.rs`, `transcoding.rs`, `ai.rs`, `costs.rs`, `releases.rs`,
  `settings.rs`, `sign_in.rs`)
  from the parts in `parts.rs`; `src/alerts.rs`: what needs attention.
- `src/github/`: the client (`client.rs`), GitHub's answers (`model.rs`), the compose files' images (`deploy.rs`) and
  what Releases shows put together from them (`releases.rs`), plain Rust with unit tests on `tests/fixtures/github/`
  (trimmed copies of real answers; main's CI turned red for the scenes).
- `src/grafana/`: the client (`client.rs`), dashboards (`model.rs`), query bodies and variables (`query.rs`) and data
  frames (`frames.rs`), plain Rust with unit tests on `tests/fixtures/` (trimmed copies of the real dashboards).
- `src/ui/chart.rs`: the time series chart (several series, time axis, units, legend, hover crosshair and readout) and
  the stat, on Rune's canvas.
- `src/copy.rs`: every word the app shows.
- The theme is the uploader's (`reelshelf_app::theme`, the site's tokens); its `chart-1`..`chart-8` colour the series.
- `tests/scenes.rs` renders every scene light and dark from the fixture answers (`tests/fixtures/ops-*.json`) and
  checks the words and controls, as the uploader's does.
