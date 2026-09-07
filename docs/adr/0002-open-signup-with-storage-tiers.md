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
- Concurrent uploads that each pass the check can overshoot the limit by at most a few clips; the check is per request, not transactional.
- Size is client-declared. A modified client could lie; Bunny's `storage_size` still lands on the row later and can be reconciled against `file_size` if that becomes a problem.
