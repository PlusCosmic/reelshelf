# ADR-0010: A desktop uploader that watches recording folders, signed in with device tokens

**Status:** Proposed (2026-11-06). Builds on ADR-0007 (clip-count tiers), ADR-0008 (roles in the database) and ADR-0009 (feature flags).

## Context

Clips reach Reelshelf when someone drops files on a book in the library or imports their Twitch clips. Either way they have to remember to do it after a session, and by then a game recorder (NVIDIA's app, OBS, Medal) has filled a folder with clips. We want clips saved during play to land on the right book without anyone doing anything.

## Decision

Build a Windows tray app, the desktop uploader, that watches a recordings folder and uploads new clips through the API. Give the API a device sign-in so the app can act for an account without the site cookie.

### Device sign-in

Users sign in only through the site cookie today (`AuthenticationSetup`). The only bearer tokens are worker secrets and Activity room tokens, and neither stands for an account. The uploader signs in with a flow modelled on the OAuth device authorization grant (RFC 8628), the one `gh auth login` uses:

1. The app calls `POST /api/devices/authorize` (anonymous, rate-limited per address) with a device name such as "Gaming PC". It gets back:
   - a `device_code`;
   - a `user_code` (8 letters from an alphabet without lookalikes, shown as `ABCD-EFGH`);
   - `verification_uri` (`https://reelshelf.app/device`);
   - `interval` (5 s) and `expires_in` (10 minutes).

   It then opens the browser on `/device?code=ABCD-EFGH`.

2. On `/device`, the signed-in person sees the device name and the code, and approves or denies it (`POST /api/devices/approve`, cookie only, `RequireAuthorization()` and `clips.create`). A signed-out visitor signs in first and comes back to the page.
3. The app polls `POST /api/devices/token` with the `device_code` every `interval`. It gets `authorization_pending` until the request is approved, `slow_down` when it polls too fast, `access_denied` or `expired_token` to stop, and finally the token.

**Storage.** `device_authorization` holds pending requests: the hash of `device_code`, `user_code`, the device name, `expires_at` and, once approved, `app_user_id`. Rows are deleted once used or expired. `device_token` holds:

- `id`, `app_user_id` (cascades) and `name`;
- `token_hash`: the SHA-256 of a 32-byte random token, never the token itself;
- `created_at`, `last_used_at` (updated at most once an hour) and `revoked_at`.

The app receives the token once and keeps it in Windows Credential Manager.

**What a token can do.** A device token stands for its account. It carries the account id as `NameIdentifier`, so `AuthenticatedUserMiddleware`, `RequirePermission` and the plan's limits work unchanged, and it can't do more than the account's role allows. The cookie stays the default scheme. A token works only on endpoints that opt in, through a `CookieOrDevice` policy that accepts either. That policy is on:

- listing the account's books (`GET /api/games/categories`) and its plan (`GET /api/me/plan`);
- preparing a clip (`ClipService.CreateClip`'s endpoint) and the R2 multipart upload endpoints (`MultipartUploadEndpoints`: part URLs, the part list and completion);
- deleting a clip the app has just prepared, so it can cancel an upload.

Every other endpoint refuses device tokens. Tokens have no fixed lifetime, but a daily sweep revokes one unused for 90 days. `/api/devices/token` and every request carrying a token are rate-limited per token.

**Revoking.** Settings gets a Devices section (`GET /api/me/devices`, `DELETE /api/me/devices/{id}`, cookie only) listing each device's name, when it was added and when it was last used. A revoked token gets 401 at once, and the app asks the person to sign in again. Suspension refuses device tokens as it refuses the cookie, since both go through `AuthenticatedUserMiddleware`. Account deletion removes the tokens with the `app_user` row.

### The uploader

**Toolkit.** .NET 10 with Avalonia for the tray icon and its windows, sharing the API's request and response models. It lives in `desktop/Reelshelf.Uploader/`, with tests in `tests/Reelshelf.Uploader.Test/`.

**Folders and books.**

- The person picks one recordings folder, and the app watches it and its subfolders.
- A subfolder is matched to a book by name, case-insensitively and ignoring punctuation, against the account's books from `GET /api/games/categories`. "Apex Legends\" lands on the Apex Legends book.
- A file in the root, or in a subfolder with no match, goes to the default book if one is set. Otherwise the app holds it, asks in its own window which book that folder belongs to, and remembers the answer.
- Matches and the default book are kept on the device.
- The app only adds clips to existing books, and links to reelshelf.app for adding a game.

**Which files.**

- Only video files count (`.mp4`, `.mov`, `.mkv`, `.webm`).
- Files already in the folder when watching starts are recorded as seen and not uploaded.
- `FileSystemWatcher` events mark a path to look at. The app also rescans the folder every 60 s and after a buffer overflow, because events can be missed.
- A file is uploaded once its size and modified time have held for 5 s and it opens for reading without a sharing violation, since recorders write long clips over minutes.
- A local ledger (SQLite under the app's local data) keys each file by path, size and modified time, so a rescan never hashes or uploads a file twice. The API's MD5 duplicate check (409) counts as done.

**Uploads.** The app uses only the `s3-multipart` protocol to R2. If preparing a clip answers any other protocol, it stops and reports that the server isn't supported. The app uploads one clip at a time, with one part in flight and an optional bandwidth cap. An interrupted upload resumes from the parts R2 already holds (`GET .../upload/parts`). While Windows reports a full-screen game in the foreground (`SHQueryUserNotificationState` returning `QUNS_RUNNING_D3D_FULL_SCREEN` or `QUNS_BUSY`), uploads wait by default. A setting lets them carry on at the cap instead.

**Plan limits.** The app reads `GET /api/me/plan` and skips a clip longer than the plan keeps. It reads the length from MP4 and MOV headers in managed code. Other containers are left to the server's probe when an upload completes (422). Clip and 30-day limits are refused at preparation (403), and the app then pauses and says why instead of retrying.

**Titles and dates.** The created date comes from the file name when a known recorder pattern matches (NVIDIA, OBS, Medal), else from the file's creation time, as `deriveCreatedAt` does in the frontend. The title is the file name without its extension.

**Distribution.** The app ships in the Microsoft Store as a full-trust MSIX package (`runFullTrust`):

- A tagged workflow builds the package and submits it through the Partner Center submission API.
- The Store signs the package and installs updates, so the app has no updater of its own.
- Starting at login uses MSIX's startup task (`windows.startupTask`), off until the person turns it on.
- While the feature flag rolls out, the listing is reachable by direct link only.

### Frontend

- A `/device` route for the approval page.
- Settings → Devices: the device list with revoke buttons, and a link to the Store listing.
- An "Or upload automatically while you play" link to the Store listing beside the library's drop-on-a-book hint.
- Copy in `copy/device.ts` and `copy/settings.tsx`.
- `/device`, the Devices section and the Store links sit behind a `desktop-uploader` feature flag (ADR-0009), and so do the device endpoints, through `RequireFeature`. Upload endpoints aren't flagged, so an issued token keeps working if the flag goes off.

### Privacy

The privacy policy changes in the same PR as the device endpoints. It says that:

- Reelshelf keeps each device's name, when it was added and when it was last used, until the device is revoked or the account is deleted;
- the app reads only the folder it is pointed at and sends only the video files it uploads;
- folder names, matches and the ledger stay on the device.

## Consequences

- Clips saved during a session are uploaded during the session, without a browser open.
- The API has a second way to act as an account. It is limited to listing books and uploading, can't exceed the account's role, is stored only as a hash, and can be seen and revoked in Settings.
- Releases go through Store certification. Installed copies update on the Store's schedule, so the endpoints the app uses must stay backwards compatible, or the app must say it needs updating.
- Folder rules live on each device, so a second PC is set up separately.
- The client-side length check covers MP4 and MOV. Other containers that are too long are uploaded and then refused by the server's probe.

## Considered

- **A browser watcher with Chromium's `FileSystemObserver`.** It works only in desktop Chromium, only while a tab is open, and needs permission again after each reload.
- **Recorder plugins**, such as an OBS script. Each covers one recorder, and NVIDIA's app and Medal have no plugin hook.
- **A directly downloaded installer** with its own code-signing certificate and updater. It costs a certificate every year and still shows SmartScreen warnings until downloads build reputation. The Store signs and updates the app for free.
