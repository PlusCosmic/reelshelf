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
new uploads by the account ids in `VideoHost:Users` go to R2 (`VideoHost:NewUploads=r2`, `VideoHost:Users:0=<id>`);
everyone else stays on Bunny.

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

- **Abort incomplete multipart uploads after 1 day.** Server-side uploads abort their own on failure; this catches
  a process that died mid-upload, and browser uploads once #213 lands.
- **`clips/` to Infrequent Access after about 7 days.** Most views happen in a clip's first few days.

Nothing expires `uploads/` on its own: a source whose transcode never succeeds stays until the clip is deleted or
purged. A rule expiring `uploads/` after a week or two would also be safe, since a transcode never runs that late.

## CORS (set on the bucket)

`PUT`, `GET` and `HEAD` from the site's origins, exposing `ETag` (which a multipart upload's parts report). Players
load the MP4 as a plain `<video src>`, which needs no CORS; hls.js-style fetch players would.

## Caching (Cloudflare cache rule)

A cache rule on the media hostname caches `/clips/` and respects the origin's `Cache-Control`. `uploads/` is never
served from the media domain: workers read it with a presigned URL on the S3 endpoint.

## Presigned URLs for workers

At claim time (`POST /api/worker/claim`) the API presigns, for at least `Transcoding:StaleAfter` plus an hour:

- `source_url`: a GET of `uploads/{clipId}` for a `transcode`, or of the current `av1.mp4` for `thumbnails`
  (the job's own version) and `trim` (the clip's current version);
- `upload_urls`: a PUT per output under `clips/{clipId}/v{video_version}/`, keyed by file name: everything for a
  `transcode`, the six jpgs for `thumbnails`, and `av1.mp4` plus the six jpgs for a `trim`.

Each PUT signs `Content-Type` (`video/mp4` for `.mp4`, `image/jpeg` for `.jpg`) and the immutable `Cache-Control`
above, so the worker must send exactly those. See [workers-and-jobs.md](workers-and-jobs.md).

## The Discord Activity

Discord's CSP lets the Activity load only through URL Mappings on the Discord application. Besides `/bunny-cdn`,
it needs **`/media` → the media domain** (e.g. `media.reelshelf.app` for prod). The API hands the Activity each
clip's host, folder (`clips/{clipId}/v{n}` for R2) and stream type (`ActivityMedia`), and
`frontend/src/shared/utils/clipMediaUrls.ts` builds `/media/clips/{clipId}/v{n}/av1.mp4` and `thumbnail.jpg` from
them.
