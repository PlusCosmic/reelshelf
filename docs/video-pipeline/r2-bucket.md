# The R2 bucket and media domain

How our own pipeline's storage is laid out and set up (#212, October 2026). The code is in `api/Video/R2/`.

## Configuration

One bucket per environment (dev and prod never share one), set with:

| Key                  | What it is                                                                                                                                            |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `R2:AccountId`       | The Cloudflare account id; the S3 endpoint is `https://{AccountId}.r2.cloudflarestorage.com`, region `auto`                                           |
| `R2:AccessKeyId`     | An R2 API token's access key, scoped to the one bucket (object read and write)                                                                        |
| `R2:SecretAccessKey` | Its secret                                                                                                                                            |
| `R2:Bucket`          | The bucket name                                                                                                                                       |
| `R2:MediaBaseUrl`    | The media domain's origin, scheme included, no trailing slash (`https://media-dev.reelshelf.app`). Never `r2.dev`, which is uncached and rate-limited |

The `r2` host is registered only when all five are set; setting only some of them, or a media URL that isn't an
https origin, fails startup. `VideoHost:NewUploads=r2` without R2 configured fails startup too. With R2 configured,
every new upload goes to R2 once `VideoHost:NewUploads=r2`; existing clips stay where they are.

The API uses AWSSDK.S3 with S3-compatible calls only (put, multipart, presign, list, delete), so a later move to S3
is a copy job and a new endpoint. Payload signing and the SDK's default checksums are off, since R2 accepts neither
the streaming signature nor every checksum the SDK would send.

## Object layout

An R2 clip's video id is its clip id (`R2ObjectKeys`):

| Key                                                                        | What                                                                                 |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `uploads/{clipId}`                                                         | The source as uploaded. Deleted once its transcode succeeds; originals aren't kept   |
| `clips/{clipId}/v{n}/av1.mp4`                                              | Version `n`'s AV1 MP4. Its size is the clip's `storage_size`, which the quota counts |
| `clips/{clipId}/v{n}/thumbnail.jpg`, `thumbnail_1.jpg` … `thumbnail_5.jpg` | The thumbnail and the five more detection frames                                     |
| `clips/{clipId}/v{n}/preview.mp4`                                          | The animated preview (no consumer yet)                                               |

The clip records its current version in `clip.video_version` (V42), with that version's codecs and height in
`video_codecs` and `video_height` for its playback source (`video/mp4; codecs="..."`). A trim writes version
`n + 1`; when it succeeds the clip moves to it and version `n`'s objects are deleted. Outputs are written once and
never change within a version, so they are uploaded with `Cache-Control: public, max-age=31536000, immutable`, and
a new version is a new URL the CDN has never cached.

Deleting a clip, or the account, deletes `uploads/{clipId}` and everything under `clips/{clipId}/`. There are no
containers, so the account deletion's container step does nothing for R2.

## Lifecycle rules (set in the dashboard)

- **Abort incomplete multipart uploads after 1 day.** Server-side uploads abort their own on failure, and deleting
  a clip aborts its open browser upload; this catches a process that died mid-upload and a browser upload whose
  clip was never deleted. The browser forgets a saved upload after 23 hours for the same reason.
- **`clips/` to Infrequent Access after about 7 days.** Most views happen in a clip's first few days.

Nothing expires `uploads/` on its own: a source whose transcode never succeeds stays until the clip is deleted or
purged. A rule expiring `uploads/` after a week or two would also be safe, since a transcode never runs that late.

## CORS (set on the bucket)

`PUT`, `GET` and `HEAD` from the site's origins, allowing the `content-type` header and exposing `ETag`, which the
browser's part PUTs must read to complete the upload (without it the uploader fails with "Storage sent no ETag").
Players load the MP4 as a plain `<video src>`, which needs no CORS; hls.js-style fetch players would.

## Browser uploads (#213)

The browser uploads straight to the bucket with a presigned S3 multipart upload; the API never sees the bytes.

1. **Prepare:** `POST /api/clips/categories/{id}/videos` reserves the clip as for Bunny (storage quota, 403; MD5
   duplicate, 409), and for an R2 clip starts a multipart upload on `uploads/{clipId}`. Its `UploadTarget` is
   protocol `s3-multipart`, endpoint `/api/clips/videos/{clipId}/upload`, and `multipart`: `upload_id`, `part_size`
   (8 MiB, or the smallest whole MiB that fits the file in 10,000 parts) and `part_count`. No upload id is stored
   server-side.
2. **Parts:** `POST {endpoint}/parts` with `upload_id` and up to 100 `part_numbers` (1 to `part_count`) returns a
   presigned PUT per part, good for an hour and signing only the host. The browser
   (`frontend/src/utils/s3MultipartUpload.ts`) sends 4 parts at once, asks for URLs 10 at a time just before it
   needs them, retries each part with backoff (1, 3, 5, 10, 20 s), and asks for a fresh URL after a 403.
3. **Resume:** the clip id, upload id and finished parts' ETags are kept in localStorage under the book, the file's
   MD5 and size. Dropping the same file on the same book after a reload resumes into that clip rather than
   preparing a new one (which the MD5 check would refuse): `GET {endpoint}/parts?uploadId=` lists what R2 holds,
   and only the rest is sent. A 404 (the upload is gone) deletes the waiting clip and prepares afresh; a 409 (the
   clip moved on) forgets it.
4. **Complete:** `POST {endpoint}/complete` with every part and its ETag joins them, then checks the object before
   anything is queued:
   - its size must be exactly the reserved `file_size`, so the quota holds;
   - a cheap ffprobe (30 s timeout) over a 10-minute presigned GET reads only the headers, and refuses a file it
     can't read, with no video stream, no picture size or no duration.

   A refused file is deleted, the clip marked `upload_failed`, and the reason comes back as a 422, which the
   uploader shows before deleting the clip. A probe that times out or can't read from storage answers 503 and the
   browser completes again, which finds the joined object. A passing file is marked `uploaded` with its `transcode`
   queued in one transaction, as the Twitch import's server-side upload is. HDR and variable frame rate aren't
   checked here: the worker probes the whole source again, tone-maps HDR and encodes at a constant frame rate (the source's, capped at 60).

5. **Cancel:** deleting the clip, as for Bunny. Deleting an R2 clip aborts any multipart upload still open on its
   key (listed by prefix), so the clip delete, the abandoned-upload purge and account deletion all free the stored
   parts at once.

All three endpoints are owner-only (`clips.create`, 404 for anyone else's clip or a host without multipart
uploads), and completion shares clip preparation's rate limit.

## Caching (Cloudflare cache rule)

A cache rule on the media hostname caches `/clips/` and respects the origin's `Cache-Control`. `uploads/` is never
served from the media domain: workers read it with a presigned URL on the S3 endpoint.

**404s.** R2 answers a missing object with a 404 and no `Cache-Control`. With the rule's edge TTL set to "use
cache-control header if present, use default Cloudflare caching behavior if not", Cloudflare then applies its
default TTL by status code, which for a 404 (and 410) is **3 minutes**
([Cloudflare: status code TTLs](https://developers.cloudflare.com/cache/how-to/configure-cache-status-code/)).
("Bypass cache if not present" wouldn't cache it.) So the API never hands out a URL before its file exists (#229):

- an R2 clip's `thumbnail_url` and detection frames are null (`[]`) until its worker reports `thumbnails_ready`
  for the clip's current version (`transcode_run.thumbnails_ready_at`) or the clip is `ready`
  (`ClipVideoColumns.ThumbnailsReadySql`, read on every query that hands out media);
- every clip's `sources` are empty until it is `ready`, so no player asks for an `av1.mp4` that isn't there.

The first request for a thumbnail is then after it was uploaded, and gets a 200. Before this, the browser asked for
the thumbnail as soon as the clip was listed, never retried the `<img>` that failed, and could be served the cached
404 for minutes after the file existed.

**Optional, for the owner to set:** a status-code TTL on the same rule, so a 404 is never cached even if something
asks too early (a client holding an old response, someone pasting a URL). In the dashboard: Caching → Cache Rules →
the `/clips/` rule → Edge TTL → "Cache TTL by status code", add status 404, "No store". Through the API it is an
`edge_ttl.status_code_ttl` entry `{ "status_code": 404, "value": -1 }` on the rule's `set_cache_settings` action.
Nothing depends on it; the API's gating above is what keeps 404s from being requested.

## Presigned URLs for workers

At claim time (`POST /api/worker/claim`) the API presigns, for `Transcoding:PresignLifetime` (2 hours, longer than
the worst-case job, and never less than `Transcoding:StaleAfter` plus an hour):

- `source_url`: a GET of `uploads/{clipId}` for a `transcode`, or of the current `av1.mp4` for `thumbnails`
  (the job's own version) and `trim` (the clip's current version);
- `upload_urls`: a PUT per output under `clips/{clipId}/v{video_version}/`, keyed by file name: everything for a
  `transcode`, the six jpgs for `thumbnails`, and `av1.mp4` plus the six jpgs for a `trim`.

Each PUT signs `Content-Type` (`video/mp4` for `.mp4`, `image/jpeg` for `.jpg`) and the immutable `Cache-Control`
above, so the worker must send exactly those. See [workers-and-jobs.md](workers-and-jobs.md).

## The Discord Activity

Discord's CSP lets the Activity load only through URL Mappings on the Discord application. Besides `/bunny-cdn`,
it needs **`/media` → the media domain** (e.g. `media.reelshelf.app` for prod). The API hands the Activity each
clip's host, folder (`clips/{clipId}/v{n}` for R2), stream type and whether it has a thumbnail yet
(`ActivityMedia`; the type is null until the clip is ready), and `frontend/src/shared/utils/clipMediaUrls.ts`
builds `/media/clips/{clipId}/v{n}/av1.mp4` and `thumbnail.jpg` from them, only once each exists.
