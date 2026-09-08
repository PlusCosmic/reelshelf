# ADR-0004: Importing Twitch clips through the API

**Status:** Accepted (2026-09-08)

## Context

Twitch is a sign-in provider (ADR-0003), and many users' best clips already exist on their Twitch channel. Getting them into Reelshelf meant downloading each clip by hand and re-uploading it. Twitch's Helix API can list a broadcaster's clips and, with the `channel:manage:clips` scope, hand out temporary download URLs for them ("Get Clips Download"). Reelshelf should be able to copy those files into a user's library directly.

Two things stood in the way. First, the OAuth handshake only kept the provider token for the ten minutes the external cookie lives; nothing was stored for later calls. Second, the clip pipeline assumed the bytes come from the browser over TUS.

## Decision

- **Tokens are kept per identity, for Twitch only.** `user_identity` gains `access_token`, `refresh_token`, `token_expires_at` and `token_scopes`. Both token columns are encrypted with the app's data-protection ring before they are written (`ProviderTokenProtector`), so a database dump does not expose usable credentials. Discord tokens are dropped at sign-in because nothing calls Discord later. A sign-in or re-link that carries tokens replaces the stored ones; one that does not leaves them alone.
- **The clips scope is part of the default Twitch sign-in.** `Twitch:Scopes` defaults to `user:read:email channel:manage:clips`, so a freshly linked Twitch account can import straight away. Identities linked before this change hold a token without the scope; the API reports them as `NeedsAuthorization` and the page offers "Reconnect Twitch", which is the ordinary link flow (`/auth/twitch/link`) and ends in `LinkOutcome.AlreadyLinked` with fresh tokens. Expired tokens are refreshed on demand; a refresh Twitch refuses clears the stored tokens and lands the user in the same `NeedsAuthorization` state.
- **The copy runs server-side, one clip per request.** `POST /api/twitch/clips/import` asks Twitch for the clip's download URL, opens it, reserves storage for the reported `Content-Length` through the existing `ClipService.CreateClip` (which also creates the Bunny video), then streams the body into that video with a `PUT`. If the copy fails the clip is deleted again, releasing the reservation. From then on the clip is indistinguishable from a browser upload: the Bunny webhook and the status refresh service pick up encoding, the abandoned-upload purge covers a lost video. Bunny's own "fetch from URL" endpoint was considered and rejected because it creates the video itself and does not return its id, which would break the reserve-then-attach ordering that keeps storage accounting honest.
- **Provenance is recorded and unique.** `clip.source_provider` / `clip.source_clip_id` say where an imported clip came from, with a unique index per owner. The listing marks clips already in the library, and importing the same clip twice returns 409 rather than a second copy. Locally uploaded clips leave both columns null; MD5 duplicate detection is unchanged.
- **Game assignment reuses IGDB ids.** Twitch reports a game per clip and Helix "Get Games" carries the IGDB id, which is also how Reelshelf keys its game categories. A clip whose game is in the user's library is pre-assigned; for games that are not, the page offers to add them through the existing add-from-IGDB endpoint. Nothing is inferred from titles.
- **The page is "Add clips".** The `/upload` route keeps its path and gains a source switch: "Upload files" (the bulk upload queue, unchanged) and "Twitch clips" (`?source=twitch`). Imported clips are filed into auto-generated gaming-session playlists (ADR-0001) using the clip's Twitch creation time, exactly as uploads are, so a batch of Twitch clips lands in the same collections a local upload of the same evening would.

## Consequences

- The Twitch application needs the `channel:manage:clips` scope allowed; users see a "manage clips" permission on the consent screen at sign-in. Deployments that do not want this can set `Twitch:Scopes` back to `user:read:email`, in which case every Twitch user sees the "Reconnect Twitch" prompt and the feature is effectively off.
- Rotating the data-protection key ring invalidates stored tokens; affected users are asked to reconnect Twitch, nothing else breaks.
- Imports consume API bandwidth (Twitch → API → Bunny) rather than the user's. Clips are capped at a minute by Twitch, so this is tens of megabytes per clip; the import endpoint shares the clip-preparation rate limit and the page runs two imports at a time.
- Only the user's own channel is importable: the download endpoint requires the caller to be the broadcaster or an editor, and the API additionally refuses clips whose `broadcaster_id` is not the linked Twitch identity.
