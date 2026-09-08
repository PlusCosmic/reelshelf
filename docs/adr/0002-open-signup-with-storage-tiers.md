# ADR-0002: Open sign-up with storage tiers

**Status:** Accepted (2026-09-07)

## Context

Reelshelf gated every request behind `whitelist.json`: only listed Discord IDs could use the app at all. Opening the app to anyone with a Discord account means clip storage on Bunny Stream becomes an unbounded cost, so access needs a storage limit rather than an allow-list.

## Decision

- Remove the whitelist gate. Any Discord account can sign in; new accounts default to the Editor role so they can upload.
- Introduce a **Storage Tier** per user. The default tier is 25 GiB (`Storage:DefaultLimitBytes`). Users listed in `whitelist.json` are on the unlimited tier; the file also keeps its optional role overrides.
- Enforce the tier when a clip row is created (`POST /api/clips/categories/{id}/videos`), before anything is created at Bunny. The client declares the file size; it is stored on `clip.file_size` and counted immediately so in-flight uploads consume quota.
- **Storage Usage** is `SUM(COALESCE(file_size, storage_size, 0))` over the owner's clips. Older clips without a recorded file size fall back to Bunny's reported storage size.
- `GET /api/me/storage` exposes usage and limit so the UI can show a meter and explain refusals.

## Consequences

- Endpoints can no longer rely on a global gate for authentication; each must require authorization itself. The Apex detection endpoints, which had none, are now admin-only.
- The user suggestion endpoint no longer lists every account; it returns only people who already share a playlist with the caller. Adding a collaborator still works by exact Discord username.
- The authoritative check and the insert run under a per-owner Postgres advisory lock (`pg_advisory_xact_lock`), so concurrent uploads cannot pass the check on the same stale usage figure. The Bunny video is created before the lock is taken and deleted again if the reservation fails, so no network I/O happens while a pool connection holds the lock.
- Roles pinned by `whitelist.json` are stored with `role_from_whitelist = true`; when the entry is removed the role resets to Editor on the user's next request, so an override cannot outlive its whitelist entry.
- Adding a clip to a playlist requires the actor to already have access to that clip, since playlist membership itself grants access. Gaming-session participants are limited to people who have added the caller to a collection they created, since that is the one signal the caller cannot manufacture alone.
- FFmpeg downloads are capped process-wide (`FFmpeg:MaxConcurrentDownloads`, default 3, 503 after a 30 s queue wait), run one at a time per video, and write unique files per request.
- Custom categories get a creator-suffixed slug, so they are effectively private to the account that made them and cannot squat an IGDB slug or another user's name.
- Size is client-declared at creation, but usage counts the larger of `file_size` and Bunny's reported `storage_size`, so an under-declared size stops mattering once encoding finishes.
- A clip whose upload is abandoned still holds its reserved storage. The uploader deletes prepared clips on cancel, error, and retry, and the status refresh service purges never-uploaded clips older than 24 hours.
- Editors can delete their own clips; without that, a default-tier user who hit the limit could never free space.
- A modified client can under-declare `fileSize` for a burst of uploads. Nothing is deleted when Bunny's real size later lands; the reconciled usage simply locks the account out of further uploads. Deleting on mismatch was rejected because Bunny's encoded renditions can legitimately exceed the original file, so any threshold would risk removing honest users' clips.
- Reads by clip id (`GET /api/clips/videos/{id}`, view marking, ffmpeg download) are scoped to the owner, members of a playlist containing the clip, or clips with an active share. Previously any signed-in account could read any clip by id.
- The library and `GET /api/games/categories` return only the caller's categories (ones they added plus ones they have clips in) instead of the global list, so one account's custom categories neither appear in nor slow down other libraries.
