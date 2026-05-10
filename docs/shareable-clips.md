# Shareable Clips Plan

## Goal

Let authenticated users create a public, shareable link for a video clip. The link should open a stripped-down player page that unauthenticated viewers can access without exposing the rest of the application.

This feature authorizes Reelshelf discovery and presentation of a shared clip. It does not add stronger cryptographic protection around Bunny playback than the current Bunny iframe links already provide.

## Primary Use Case

1. A signed-in user watches a clip in Reelshelf.
2. The user clicks the existing share button on the video player.
3. Reelshelf creates or reuses one stable public share link for that clip.
4. The user copies the link and sends it to someone else.
5. The recipient opens the link and can watch the shared clip without signing in.

## Scope

### In Scope

- Add behavior to the existing share action in the authenticated video player UI.
- Persist shared-link state for a clip.
- Generate a public, hard-to-guess share URL.
- Add an unauthenticated public player route for shared clips.
- Keep the public player focused on playback only, with minimal metadata.
- Allow users to identify that a clip has already been shared.
- Include schema support for future revocation.

### Out Of Scope For Initial Release

- Discord friend or server picker integration.
- Per-recipient permissions.
- Expiring links.
- Password-protected links.
- Revocation or regeneration UI/API.
- Public discovery, feeds, search, or indexing.
- Comments, reactions, or social features on the public page.
- Custom Open Graph or social preview metadata.
- Persisted public view analytics.
- New public endpoint rate limiting.

## User Experience

### Authenticated Player

The existing video player actions row should include a clear share button. When clicked, the button opens a small modal dialog and immediately creates or retrieves the stable share link.

Expected states:

- Not shared: the user can create a share link.
- Creating link: the share action shows a loading state and prevents duplicate requests.
- Shared: the user can copy the existing link.
- Not playable: the UI explains that the clip is still processing and cannot be shared yet.
- Failed: the UI shows a recoverable error and lets the user retry.

The dialog should include both:

- A read-only text field containing the absolute share URL.
- A copy button using the Clipboard API, with the text field serving as a manual fallback.

### Shared Indicator

Once a clip has a public link, the authenticated UI should mark the clip as shared on clip cards, list rows, and the player page. Use icon plus short text where there is room, and icon-only with a tooltip/accessible label in dense surfaces.

### Public Player Page

The public share URL should route to a stripped-down page that does not require authentication. The page should include:

- The Bunny iframe video player.
- Clip title.
- Game/category display name.
- Duration.
- Uploaded date, using clip `date_uploaded` when present and falling back to `created_at`.
- A lightweight Reelshelf brand mark or link back to `/`.
- A small persisted icon-only theme toggle in the top-right corner.
- A simple loading state while resolving the token.
- A clear unavailable state for deleted, revoked, or invalid links.

The public page should bypass the authenticated app chrome and current-user gate. It should not include authenticated navigation, library controls, account controls, edit actions, tags, owner identity, storage size, viewed state, playlist membership, Discord/user IDs, raw internal clip IDs, MD5 hashes, collection IDs, or other private metadata.

The route should be implemented as a TanStack Router SPA route at:

```text
/share/$token
```

Production deep-link reloads should rely on the existing SPA fallback.

## Data Model

Add a persisted share record for each public clip link. Each clip has one stable active share link. Revocation schema support is included for future use, but no revocation UI/API ships in the first release.

Minimal table shape:

- `id`.
- `token`.
- `clip_id`.
- `owner_id`.
- `created_at`.
- `revoked_at`, nullable.

The token should be a high-entropy opaque base64url-style random string generated in C# with `RandomNumberGenerator.GetBytes(32)`. Avoid exposing sequential database identifiers or the clip UUID in the public URL.

Constraints:

- `token` is globally unique.
- `clip_id` references `clip(id)` with `ON DELETE CASCADE`.
- `owner_id` references the owning user.
- One active unrevoked share per clip, enforced with a partial unique index on `clip_id where revoked_at is null`.

Recommended URL shape:

```text
/share/:token
```

## API Plan

### Authenticated Endpoint

Add an endpoint for creating or retrieving the share link for a clip.

```text
POST /api/clips/videos/{clipId}/share
```

Behavior:

- Requires authentication.
- Verifies the current user owns/can access the clip.
- Only allows sharing playable clips, defined as Bunny `Finished` or `ResolutionFinished`.
- Creates an active share record if one does not already exist.
- Returns the complete public share path and shared state.
- Is idempotent for repeated share attempts on the same clip.
- Handles concurrent requests with application checks plus database constraints. If a race hits the active-share uniqueness constraint, re-read and return the existing active share.
- Retries bounded token generation on unique-token collision before failing.

Response:

```json
{
  "share_path": "/share/F0a4u4eTqHh7G0YIhTSO7kT3zX9fZ7LZyeAhO8Me1Ig",
  "shared": true
}
```

The frontend should turn `share_path` into an absolute URL with `window.location.origin`.

Clip list/detail responses should include nested share state without the URL:

```json
{
  "share": {
    "shared": true
  }
}
```

### Public Endpoint

Add an unauthenticated endpoint for resolving a share token into the public player data.

```text
GET /api/shared-clips/{token}
```

Behavior:

- Does not require authentication.
- Returns only the data needed by the public player.
- Returns `404` for unknown, deleted, inaccessible, or revoked shares.
- Does not leak whether a private clip exists behind an invalid token.
- Does not mark the clip viewed.
- Uses the same generic unavailable behavior for unknown, revoked, deleted, or otherwise unavailable shares.

Response:

```json
{
  "title": "Clip title",
  "game": "Apex Legends",
  "duration_seconds": 42,
  "uploaded_at": "2026-05-10T12:34:56Z",
  "embed_url": "https://player.mediadelivery.net/embed/..."
}
```

Return only `embed_url` for playback. Do not return Bunny identifiers separately.

## Frontend Plan

1. Wire the existing Share button in `PlayerActions`; leave unrelated placeholder actions unchanged.
2. Open a small modal when Share is clicked and immediately call the share endpoint.
3. Show loading, not-playable, failed, copied, and ready-to-copy states in the modal.
4. Add shared indicators to clip cards, list rows, and the player page.
5. Add a public `/share/$token` route.
6. Build a minimal shared-player screen that consumes the public share endpoint.
7. Refactor the root route so `/share/$token` can render without calling or waiting on `useCurrentUser()`.
8. Keep theme state and `localStorage` persistence available to both authenticated and public shells.
9. Add a small icon-only public theme toggle in the top-right corner.
10. Add `noindex,nofollow` metadata for shared pages where the current frontend metadata setup allows it.
11. Ensure same-origin API defaults continue to work for local and deployed environments.

## Backend Plan

1. Add a migration for the share records table.
2. Add data-access logic for creating and resolving share records.
3. Add authenticated share creation endpoint.
4. Add unauthenticated public share resolution endpoint.
5. Ensure public resolution returns only safe fields.
6. Add nested share state to authenticated clip list/detail responses.
7. Regenerate the frontend API client after API contract changes.
8. Use the generated frontend API client for the new endpoints.

## Security And Privacy

- Share tokens must be high entropy and non-sequential.
- Share tokens are bearer credentials and must not be logged in full. Log share row IDs and clip IDs; if token correlation is needed, log only a short prefix.
- Public endpoints must avoid returning private library metadata.
- Invalid, revoked, deleted, or unauthorized share tokens should return the same generic unavailable response.
- Shared pages should be accessible without cookies or session state.
- Shared pages should be marked `noindex,nofollow`.
- Public share links authorize Reelshelf discovery/presentation only. Bunny iframe playback remains protected by Bunny link obscurity using UUID-like identifiers, matching the current system.
- Public API `404` responses map to "This shared clip is unavailable." Network or server failures can show a separate temporary-load-failure message.
- Basic structured logs for share creation and public resolution are acceptable; do not add persisted analytics tables in the first release.

## Discord Stretch Goal

After the basic link-sharing flow is stable, add Discord-specific sharing.

Potential capabilities:

- Show a populated list of Discord friends, channels, or servers if the user has connected Discord.
- Let the user choose one or more destinations.
- Send the generated share link directly through Discord where API permissions allow it.
- Fall back to copying the link if Discord permissions or destination data are unavailable.

This should be treated as a separate feature because it introduces OAuth scopes, destination syncing, permission handling, third-party delivery failure states, and likely proper social preview metadata.

## Rollout Plan

1. Implement link creation and public playback behind the normal authenticated app flow.
2. Verify that unauthenticated users can open shared links while the rest of the app remains protected.
3. Add visible shared state in the authenticated player.
4. Add visible shared state in clip cards/list rows.
5. Add basic structured logs for share creation and public share resolution.
6. Regenerate and consume the generated frontend API client.
7. Verify production-style deep links under `/share/:token` are handled by the SPA fallback.
8. Revisit revocation, expiration, Discord sharing, social previews, and public analytics after validating the basic flow.

## Resolved Decisions

- Each clip has one stable active share link.
- Revocation support exists only in schema via `revoked_at`.
- Deleted clips remove share rows through `ON DELETE CASCADE`.
- Shared links remain stable across clip rename/move-style metadata changes as long as the source clip still exists and is playable.
- Public metadata is limited to title, game/category name, duration, uploaded date fallback, and `embed_url`.
- Shared pages are not indexable.
- Share creation happens when the share modal opens.
- Share UI lives in the existing player actions row; unrelated placeholder actions remain unchanged.
- Public playback uses the Bunny iframe embed.
- Public views do not write to `clip_view`.
- Public endpoint returns `embed_url` only, not Bunny IDs.
- The public page includes a small Reelshelf link to `/` and an icon-only persisted theme toggle.
- The API returns `share_path`, not an absolute URL.
- Normal clip responses include `share.shared`, but not the path.
- Use a separate unauthenticated `GET /api/shared-clips/{token}` endpoint.
- No custom Open Graph/social metadata in the first release.
- No new rate limiter in the first release.
- Use generated API clients after regenerating them.
