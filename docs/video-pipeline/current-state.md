# Current state: what the app relies on from the video host

As of 2026-10-18, after the #188 refactor (PRs #195–#200). Bunny Stream is the only host.

## The seam

- `api/Video/IClipVideoHost.cs` is everything the app asks of a host: `CreateVideoAsync`, `CreateUploadTarget`,
  `UploadAsync` (server-side, Twitch import), `GetVideoAsync`, `DeleteVideoAsync`, `DeleteContainerAsync`.
  `api/Bunny/BunnyService.cs` is the only implementation.
- `api/Core/ClipMediaUrls.cs` builds every media URL: `Playlist` (HLS), `Thumbnail`, `DetectionFrames`. It reads
  `BunnyCdnBaseUrl`.
- `api/Video/ClipVideoUpdateHandler.cs` is what runs when a video changes. It re-reads the video from the host,
  writes it onto the clip, and on `ready` queues legend detection and transcription. Today Bunny's webhook
  (`api/Bunny/BunnyWebhookEndpoints.cs`) calls it; our own transcode worker would call it directly.
- `ClipVideoStatus` (`api/Video/ClipVideoStatus.cs`, column `clip.video_state`) is the app's own status:
  `awaiting_upload`, `uploaded`, `processing`, `ready`, `encode_failed`, `upload_failed`. `clip.video_status`
  still holds Bunny's raw number and is due to be dropped.

## What a replacement host has to provide

| Need                                                                                          | Used by                                                                              | Today (Bunny)                                                      |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------ |
| Resumable browser upload, described by `UploadTarget` (protocol, endpoint, headers, metadata) | `frontend/src/utils/clipUpload.ts` (tus-js-client)                                   | Bunny's presigned TUS endpoint                                     |
| Server-side upload of a stream                                                                | Twitch import (`api/Twitch/`)                                                        | `PUT /videos/{id}`                                                 |
| A playable stream URL                                                                         | `ClipVideoPlayer`, `ActivityPlayer` (via `frontend/src/shared/utils/hlsStream.ts`)   | `{id}/playlist.m3u8`                                               |
| A stream ffmpeg can read                                                                      | Transcription audio (`ClipAudioExtractor`), downloads (`FFmpegService`)              | the same HLS playlist                                              |
| `thumbnail.jpg`                                                                               | Clip cards, player poster, share page                                                | Bunny-generated                                                    |
| Six detection frames: `thumbnail.jpg` + `thumbnail_1..5.jpg`, spread through the clip         | Legend detection (`api/ApexLegends/LegendDetection/`), the baseline page's HUD crops | Bunny-generated; detection is tuned on Bunny's timing              |
| Status and encode progress                                                                    | Upload tray, playability, abandoned-upload purge, status refresh                     | Webhook + polling `GET /videos/{id}`                               |
| Length, storage size, uploaded-at                                                             | Durations, storage quota (`GREATEST(file_size, storage_size)`), display              | From the video object; `storage_size` counts every rendition       |
| Delete video / container                                                                      | Clip delete, abandoned-upload purge, account deletion                                | Bunny API; containers are Bunny collections (one per owner × game) |
| An origin the Discord Activity can reach through a URL Mapping                                | `frontend/src/shared/utils/clipMediaUrls.ts` (`/bunny-cdn`)                          | Mapping `/bunny-cdn` → `vz-cd8f9809-39a.b-cdn.net`                 |
| CORS on media (only for fetch-based players such as hls.js)                                   | Main site player                                                                     | Pull zone sends `access-control-allow-origin: *`                   |

Not host-dependent: clip metadata, tags, playlists, shares, ownership and quotas live in Postgres.

## Still Bunny-specific outside `api/Bunny/`

- The Activity's `/bunny-cdn` path in `frontend/src/shared/utils/clipMediaUrls.ts` and the Discord URL Mapping.
- Config keys `BunnyLibraryId`, `BunnyAccessKey`, `BunnyWebhookSecret`, `BunnyCdnBaseUrl` (`api/appsettings.json`,
  `PlusCosmic/infrastructure` `services/clips*.yml`).
- `scripts/dev-db/clone-bunny-videos.ts` (gives dev its own copies of prod's videos).
- The privacy policy names Bunny.net as the processor that stores and streams clips
  (`frontend/src/routes/privacy.tsx`); update it in the same PR as any switch.

## Things we get from Bunny without asking

The HLS ladder, the six thumbnails at Bunny's chosen times, encode progress, webhook retries, `storage_size`
across renditions, resumable uploads straight from the browser (our server never sees upload bandwidth) and a
CDN. Bunny may not keep originals: the dev clone script falls back to the best HLS rendition when
`{id}/original` is missing, so a data migration may only be able to recover transcodes.
