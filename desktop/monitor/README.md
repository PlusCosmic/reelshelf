# Reelshelf Monitor

A Mac app, drawn with Rune, that shows everything about Reelshelf in one place. It signs in to Reelshelf as the
**monitor** (a device token only Admins' monitor tokens can use on the admin and ops endpoints) and shows:

- **Overview**: a card per section with its headline numbers and, in red, what needs attention: failures in the last
  hour, running jobs whose worker went quiet, a pending job waiting over 10 minutes, no object store configured,
  CloudFront answering over 1% of the last day's requests with a 5xx, the bucket's difference from the database
  growing by more than 0.5% of it (at least 1 GiB) in a week, CloudWatch unreadable, a cost provider that couldn't be
  read, a workflow failing on main.
- **Storage**: totals, the object store in use, the bucket's size by storage class and its objects over 30 days, the
  bucket against what the database accounts for (the difference by day and its change over the week), CloudFront's
  requests, bytes downloaded and 4xx/5xx rates over 7 days, clips by tier, the top accounts and exports
  (`GET /api/admin/ops/storage`; the bucket's and CloudFront's numbers are CloudWatch's, read by the API hourly with the
  store's AWS keys, and say so when the keys lack `cloudwatch:GetMetricData` or the store isn't AWS S3).
- **Transcoding**: the queue by job type, the overflow pools, the AAC backfill and the last failures, each clip id
  copyable (`GET /api/admin/ops/transcoding`).
- **AI pipelines**: each run queue by trigger, its last failures, and spend by model over 24 hours and 30 days
  (`GET /api/admin/ops/ai`).
- **Costs**: this month's spend across providers and Stripe's net revenue, then a card per provider set up on the API
  (AWS, OpenRouter, OpenAI, Anthropic; Stripe's revenue) with this month, last month, its last 30 days as a chart and
  its breakdown by service or model, a red line when its latest read failed (the last numbers stay), and which
  providers aren't set up and where their keys go on the API (`GET /api/admin/ops/costs`). The API reads providers in
  the background, hourly and AWS every 6 hours; billing keys live only there, never in the app.
- **Admin**: the site's `/admin/accounts` and `/admin/reports`, in two tabs. Accounts finds an account by name or
  sign-in id (`GET /api/admin/accounts`) and opens its role, granted plan, suspension and recorded changes
  (`GET /api/admin/accounts/{id}`), changing each through `PUT .../role`, `.../granted-tier` and `.../suspension`.
  Reports lists the open reports first (urgent ones, child safety and terrorism, in red), or those taken down,
  dismissed or removed, filtered by reason (`GET /api/admin/reports`), and deals with one: take down, dismiss, or for
  an urgent report remove (`POST /api/admin/reports/{id}/…`). Every change asks for confirmation with the reason or note
  the API records, naming what it changes and saying what happens; the API's own words show when it refuses (409 for
  the last Admin, a report dealt with meanwhile). The app offers only what the API allows: never a change to your own
  role or suspension, never suspending an Admin, never a link to a child-safety report's clip. Overview's Admin card
  counts the open reports, the urgent ones in red. These endpoints aren't behind `ops-monitor`.
- **Dashboards**: Grafana's dashboards drawn natively (below).
- **Releases**: from GitHub ("The Releases section", below): what is deployed, what is coming, the uploader's latest tag and the open pull
  requests. Overview's Releases card says how far prod's app is behind main and turns red when a workflow on main
  failed.
- **Settings**: the account it is signed in as, Sign out, Grafana's address and token, and the GitHub token.

The section showing is read again every 30 seconds (and on Refresh); the API caches each
answer for 15 seconds. GitHub is read at most every 2 minutes (each read is a couple of dozen requests against
the token's 5,000 an hour), and at once on Refresh.

## The Releases section

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

## Releases

The app is published to this repository's GitHub Releases as `reelshelf-monitor-<version>-macos-universal.dmg` (Apple
Silicon and Intel, macOS 13 or later), tagged `monitor-v<version>` and titled "Reelshelf Monitor <version>". To install,
open the image and drag Reelshelf Monitor to Applications.

Releases are signed with the Developer ID certificate in this repository's secrets, with the hardened runtime, and the
image is notarized by Apple and stapled, so the app opens like any other. A build without those secrets (a fork, or a
pull request from one, which GitHub gives no secrets) is signed ad hoc instead, and macOS refuses the first launch of a
downloaded copy. Then either right-click (Control-click) the app in Applications and choose **Open**, then **Open**
again (or System Settings → Privacy & Security → **Open Anyway** after the first refusal), or clear the quarantine flag
once:

```sh
xattr -dr com.apple.quarantine "/Applications/Reelshelf Monitor.app"
```

`.github/workflows/publish-monitor.yml` builds it on `macos-latest`, as PlusCosmic/postmaster's `macos.yml` does:

- **Pull requests touching `desktop/**` or the workflow**: `test` (`cargo test -p reelshelf-monitor`, the scenes on
  the runner's Metal device) and `build` run side by side. `build` compiles for Apple Silicon and Intel and
  `packaging/package.sh` merges the two into one binary, wraps it in `Reelshelf Monitor.app` (`packaging/Info.plist`,
  bundle id `app.reelshelf.monitor`, an `AppIcon.icns` made from `packaging/icon.png`) and puts that in a disk image
  beside an Applications link. The job then mounts the image and checks both architectures, the signature, the bundle
  id and version, the icon, and that each architecture's binary says that version (`--version`) and renders the
  fixture overview headless. The image is the run's `reelshelf-monitor-macos` artifact and the renders
  `monitor-snapshot-macos`. Nothing is published.
- **Pushes to `main` touching the same paths, and `workflow_dispatch`**: once both pass, `release` publishes the image
  (`packaging/publish-release.sh`). A version that already has a release (a re-run, or a dispatch with no new commit
  under `desktop/`) publishes nothing.

The version is `MAJOR.MINOR` from this crate's `Cargo.toml` (its own, apart from the uploader's) and a patch that
counts the commits touching `desktop/` or the workflow (`packaging/version.sh`), so it moves only when the app could
have changed: the monitor builds from the whole Cargo workspace (`reelshelf-app`, `reelshelf-api`, `reelshelf-core`)
and reads nothing outside `desktop/`. Bump `MAJOR.MINOR` by hand for a release people should notice. The build
compiles the version in through `REELSHELF_APP_VERSION`, as the uploader's packaging does, so the bundle's version is
the one `X-Reelshelf-Client: monitor/<version>` sends.

The monitor is the only thing published to GitHub Releases, so its newest release is marked Latest. The uploader ships
through the Microsoft Store from its own `uploader-v*` tags: `publish-uploader.yml` runs only on those, and the Releases
section reads only `uploader-v*` tags, so `monitor-v*` tags reach neither.

**Never change `CFBundleIdentifier`** (`app.reelshelf.monitor`, the Keychain service too): macOS keys Keychain access
on it. `packaging/icon.png` is `packaging/icon.svg` (the site's logo mark on macOS's icon grid) rendered at 1024 × 1024;
render it again after changing the SVG.

Signing and notarizing take these repository secrets: `MACOS_CERTIFICATE_P12` (a base64 Developer ID Application
`.p12`), `MACOS_CERTIFICATE_PASSWORD`, and an App Store Connect API key as `MACOS_NOTARY_KEY` (the `.p8`'s text),
`MACOS_NOTARY_KEY_ID` and `MACOS_NOTARY_ISSUER_ID`. With them `build` imports the certificate into a throwaway
keychain, signs the app with the hardened runtime and a secure timestamp, signs the image, submits it to Apple's notary
service and staples the ticket, and the check also runs `xcrun stapler validate` on the image. Without
`MACOS_CERTIFICATE_P12` every step is skipped and the app is signed ad hoc. A build by hand is signed ad hoc unless
`CODESIGN_IDENTITY` names a Developer ID identity in your keychain.

To build one by hand, from `desktop/`, for this Mac's architecture:

```sh
version=$(monitor/packaging/version.sh)
REELSHELF_APP_VERSION=$version cargo build --release -p reelshelf-monitor
monitor/packaging/package.sh "$version" target/macos target/release/reelshelf-monitor
```

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
`REELSHELF_MONITOR_SEARCH=<query>` opens Admin's Accounts tab searched for it.
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
- `src/admin.rs`: Admin's state, its filters and the change waiting for confirmation.
- `src/ui/`: a page per section (`overview.rs`, `storage.rs`, `transcoding.rs`, `ai.rs`, `costs.rs`, `releases.rs`,
  `admin.rs`, `settings.rs`, `sign_in.rs`)
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
- `packaging/`: the app bundle, its icon, the disk image and the release ("Releases", above).
- `tests/scenes.rs` renders every scene light and dark from the fixture answers (`tests/fixtures/ops-*.json`, `admin-*.json`) and
  checks the words and controls, as the uploader's does.
