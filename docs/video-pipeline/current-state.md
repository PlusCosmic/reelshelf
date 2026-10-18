# Current state: what the app relies on from the video host

As of 2026-10-18, after the #188 refactor (PRs #195–#200), per-clip hosts (#209), the R2 host (#212) and R2
downloads (#216). Bunny Stream holds every existing clip; the `r2` host is registered when `R2:*` is configured and
takes new uploads only for the accounts in `VideoHost:Users`.

## The seam

- `api/Video/IClipVideoHost.cs` is everything the app asks of a host: `CreateVideoAsync`, `CreateUploadTarget`,
  `UploadAsync` (server-side, Twitch import), `GetVideoAsync`, `DeleteVideoAsync`, `DeleteContainerAsync`.
  `api/Bunny/BunnyService.cs` (Bunny) and `api/Video/R2/R2VideoHost.cs` (R2, see [r2-bucket.md](r2-bucket.md))
  implement it. `CreateVideoAsync` gets the reserved clip's id, and an R2 clip's video id is its clip id.
  `NeedsStatusPolling` is false for R2: its status reaches the clip row from the worker's reports, so the status
  refresh and the metadata backfill skip it, and its `GetVideoAsync` answers from the clip row (never null for a
  clip that exists), so the abandoned-upload purge removes an R2 clip only when the row says nothing was uploaded.
- **Per-clip host:** `clip.video_host` (V39; `bunny` or `r2`, listed in `api/Video/VideoHostNames.cs`) says which
  host holds a clip's video; existing clips are `bunny`. `api/Video/ClipVideoHosts.cs` returns the host for a clip's
  `video_host`, and every per-clip call goes through it: clip delete, Twitch import's upload, the status refresh and
  abandoned-upload purge, the metadata backfill, `ClipVideoUpdateHandler` and account deletion. Hosts are keyed
  services registered by name (`AddClipVideoHost<THost, TMediaUrls>` in `api/Video/VideoHostRegistration.cs`), so
  adding one is implementing `IClipVideoHost` and `IClipVideoMediaUrls` and registering them under its name.
- **New uploads:** `ClipService.CreateClip` asks `NewUploadVideoHostPolicy` for the host and stores it on the row.
  `VideoHost:NewUploads` (default `bunny`) applies only to the account ids in `VideoHost:Users`; everyone else, and
  everyone when the list is empty, stays on Bunny. The API refuses to start when `NewUploads` names a host that isn't
  registered, rather than failing those users' uploads or quietly sending them to Bunny.
- **Downloads (#216):** a host whose video is a file registers a keyed `IClipVideoDownloads`
  (`api/Video/IClipVideoDownloads.cs`; R2's is `R2Downloads`), and `GET /api/ffmpeg/download/{videoId}` redirects a
  `ready` clip of that host to its short-lived presigned URL. A host without one (Bunny) is remuxed by
  `FFmpegService`. See [workers-and-jobs.md](workers-and-jobs.md#downloads).
- `api/Core/ClipMediaUrls.cs` builds every media URL: `Sources`, `ServerStream` (what ffmpeg reads: Bunny's HLS, R2's
  MP4), `Thumbnail`, `DetectionFrames` and `MediaPath` (the Activity's folder), each from the clip's `ClipVideoRef`
  (`video_host`, `video_id`, and V42's `video_version`, `video_codecs`, `video_height`) and that host's
  `IClipVideoMediaUrls`. Bunny's (`api/Bunny/BunnyMediaUrls.cs`) reads `BunnyCdnBaseUrl`; R2's
  (`api/Video/R2/R2MediaUrls.cs`) reads `R2:MediaBaseUrl` and serves `clips/{clipId}/v{n}/`.
- `api/Video/ClipVideoUpdateHandler.cs` is what runs when a video changes. It re-reads the video from the host,
  writes it onto the clip, and on `ready` queues legend detection and transcription. Bunny's webhook
  (`api/Bunny/BunnyWebhookEndpoints.cs`) calls it, and `TranscodeQueue` hands it our own worker's reports.
- `ClipVideoStatus` (`api/Video/ClipVideoStatus.cs`, column `clip.video_state`) is the app's own status:
  `awaiting_upload`, `uploaded`, `processing`, `ready`, `encode_failed`, `upload_failed`. `clip.video_status`
  still holds Bunny's raw number and is due to be dropped.

## What a replacement host has to provide

| Need                                                                                          | Used by                                                                              | Today (Bunny)                                                      |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------ |
| Resumable browser upload, described by `UploadTarget` (protocol, endpoint, headers, metadata) | `frontend/src/utils/clipUpload.ts` (tus-js-client)                                   | Bunny's presigned TUS endpoint                                     |
| Server-side upload of a stream                                                                | Twitch import (`api/Twitch/`)                                                        | `PUT /videos/{id}`                                                 |
| A playable stream URL                                                                         | `ClipVideoPlayer`, `ActivityPlayer` (via `frontend/src/shared/utils/hlsStream.ts`)   | `{id}/playlist.m3u8`                                               |
| A stream ffmpeg can read                                                                      | Transcription audio (`ClipAudioExtractor`), Bunny downloads (`FFmpegService`)        | the same HLS playlist                                              |
| `thumbnail.jpg`                                                                               | Clip cards, player poster, share page                                                | Bunny-generated                                                    |
| Six detection frames: `thumbnail.jpg` + `thumbnail_1..5.jpg`, spread through the clip         | Legend detection (`api/ApexLegends/LegendDetection/`), the baseline page's HUD crops | Bunny-generated; detection is tuned on Bunny's timing              |
| Status and encode progress                                                                    | Upload tray, playability, abandoned-upload purge, status refresh                     | Webhook + polling `GET /videos/{id}`                               |
| Length, storage size, uploaded-at                                                             | Durations, storage quota (`GREATEST(file_size, storage_size)`), display              | From the video object; `storage_size` counts every rendition       |
| Delete video / container (a host without containers has none to delete)                       | Clip delete, abandoned-upload purge, account deletion                                | Bunny API; containers are Bunny collections (one per owner × game) |
| An origin the Discord Activity can reach through a URL Mapping                                | `frontend/src/shared/utils/clipMediaUrls.ts` (`/bunny-cdn`)                          | Mapping `/bunny-cdn` → `vz-cd8f9809-39a.b-cdn.net`                 |
| CORS on media (only for fetch-based players such as hls.js)                                   | Main site player                                                                     | Pull zone sends `access-control-allow-origin: *`                   |

Not host-dependent: clip metadata, tags, playlists, shares, ownership and quotas live in Postgres.

## Still Bunny-specific outside `api/Bunny/`

- The Activity's `/bunny-cdn` path in `frontend/src/shared/utils/clipMediaUrls.ts` and the Discord URL Mapping. The
  API now hands the Activity each clip's host, folder and stream type (`ActivityMedia`), and R2 clips go through a
  `/media` mapping to the media domain.
- Config keys `BunnyLibraryId`, `BunnyAccessKey`, `BunnyWebhookSecret`, `BunnyCdnBaseUrl` (`api/appsettings.json`,
  `PlusCosmic/infrastructure` `services/clips*.yml`).
- `scripts/dev-db/clone-bunny-videos.ts` (gives dev its own copies of prod's videos).
- The privacy policy names Bunny.net as the processor that stores and streams clips, and Cloudflare for clips in
  our own pipeline (`frontend/src/routes/privacy.tsx`); update it in the same PR as any further switch.

## Things we get from Bunny without asking

The HLS ladder, the six thumbnails at Bunny's chosen times, encode progress, webhook retries, `storage_size`
across renditions, resumable uploads straight from the browser (our server never sees upload bandwidth) and a
CDN. Bunny may not keep originals: the dev clone script falls back to the best HLS rendition when
`{id}/original` is missing, so a data migration may only be able to recover transcodes.
