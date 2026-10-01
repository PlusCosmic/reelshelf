# ADR-0006: Watching clips together in a Discord Activity

**Status:** Proposed (2026-10-01)

## Context

Clips are mostly watched alone, on the library page, by their owner. The people who were in the clip are usually the owner's Discord friends, and the natural time to watch them is together in a voice channel. Discord Activities embed a web app in a voice channel through an iframe proxied at `<app-id>.discordsays.com`. The `@discord/embedded-app-sdk` handles OAuth (`authorize()` / `authenticate()`), gives every launch an `instanceId` shared by everyone in it, and lists the connected participants. Discord does not sync any state between participants; the app has to do that itself.

Things we found that shaped the decision:

- Until now only a clip's owner can see it. There are no share links. A watch room is the first place where other people see someone's clips, so it needs its own access rule.
- Playback uses Bunny's embed player iframe (`player.mediadelivery.net`). Inside an Activity, every external origin has to be listed in the app's URL Mappings and pass Discord's CSP. A third-party player iframe nested in Discord's iframe, synced through player.js postMessage, would be fragile and hard to keep in step. The HLS playlist on the Bunny pull zone (already used by `FFmpegService`) can be played directly in a `<video>` element with `hls.js`.
- The Activity's OAuth gives a Discord user id. That is the same id stored on a Discord **Linked Identity** (ADR-0003), so anyone who has linked Discord resolves to their **Account** without signing in again. People with only a Twitch identity do not resolve.
- The API has no realtime transport. Redis is already deployed and can back a SignalR hub across instances.

## Decision

- **A watch room is one Activity instance.** The room is keyed by Discord's `instanceId`. It exists while someone is connected and holds the host, participants, a queue and the playback state. Rooms are kept in memory, with a Redis backplane, and are not written to Postgres in the first version.
- **Guests need no Reelshelf account.** Anyone in the voice channel who launches the Activity can watch and react. The Activity's Discord token proves who they are and that they are in the room. No **Account** is created for them.
- **People with a Reelshelf account can queue their own clips.** A participant whose Discord id matches a **Linked Identity** is a member of the room and can add clips from their own **Library** to the queue. Clips only ever enter a room from their owner, so queuing a clip is the owner's choice to show it, and no other consent step is needed.
- **The host runs the room.** The host is the first member to connect, which is normally whoever launched the instance. The host can queue, skip, remove and reorder clips, and lock the queue so only the host can add to it. A room with only guests has no host and no clips, and shows that until a member joins. When the host leaves, the longest-connected member becomes host.
- **Access to a queued clip is scoped to the room.** A participant can fetch the stream details of a clip only while it is in their room's queue or playing, and only while they are connected. Nothing outlives the room. There are no share links, and guests never get the clip's library page, tags or other metadata beyond title, game and owner.
- **The queue alternates between people.** Clips play in round-robin by who added them, not first-come-first-served, so one person adding thirty clips does not take over the session. The host can still reorder by hand.
- **Playback is host-authoritative and relayed by the server.** The server holds `{ clipId, playing, position, serverTimestamp }`. Host actions update it and it is broadcast to the room. Clients work out the expected position, correct small drift by adjusting `playbackRate` and seek on large drift. Clips are short, so moving between clips matters more than frame accuracy.
- **Activity playback uses hls.js against the Bunny HLS stream**, not the embed player. The Bunny pull zone is added to the URL Mappings. The rest of the site keeps using the embed player.
- **Activity auth is its own scheme.** The Activity exchanges the SDK's OAuth code at the API for a short-lived token bound to the Discord user and `instanceId`. Only the room hub and room endpoints accept it. It is not a site session cookie and does not sign anyone in to the site. The Activity uses the existing Discord application, so Discord ids line up with stored identities.

## Consequences

- This is the first time a non-owner sees a clip. The guarantee is narrow: only clips the owner queued, only to people in that room, only while it lasts. Any later sharing feature should be designed separately rather than built on room access.
- Bunny token authentication is off: the pull zone serves playlists and thumbnails to unsigned requests (an unsigned request for a missing video gets 404 from storage, not 403), and the API, thumbnails and embed player all rely on that. So anyone who knows a video's id can stream it, and a guest who saw a clip's playlist URL in a room can still reach it afterwards. Room scoping limits what the API hands out, not what the CDN serves. That is acceptable for clips their owner chose to show friends. Turning token authentication on would close the gap, but every CDN read (FFmpeg downloads, transcription audio, thumbnails, the embed player and the Activity) would need signed URLs, so it is a separate change.
- Twitch-only accounts are treated as guests. The Activity shows them a hint to link Discord to queue their clips.
- The API gains a long-lived connection type (SignalR) and the Redis backplane becomes required when running more than one instance. Dev and prod share Redis, so room channels are prefixed per environment.
- The Discord application needs the Activity enabled, URL Mappings for the app and the Bunny pull zone, and `instanceId`-aware launch handling. Dev is behind Cloudflare Access and reachable only by its owner, so dev is for solo testing (one host, no guests) and multi-person testing happens on prod.
- Signing up from inside the Activity ("start your own shelf") is left out of the first version but fits, because the guest's Discord identity is already authenticated.
