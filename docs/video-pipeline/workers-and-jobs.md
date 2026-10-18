# Workers and jobs

Direction as of 2026-10-18. Builds on the queue lean in [open-questions.md](open-questions.md) (Postgres run tables,
workers going through the API) and the pipeline in [storage-transcoding-queue.md](storage-transcoding-queue.md).

## The worker is its own service

The transcode worker is a separate application and container, not part of the API:

- It handles untrusted input, so it runs in the sandbox described in the storage notes.
- It needs its own CPU limits so it stays off the game servers' cores.
- It scales separately, and the same image runs on the dedicated server and on Fargate Spot.

**Build a thin custom worker.** Encoding is a solved problem (ffmpeg with SVT-AV1; FFMpegCore is already a
dependency). The glue isn't covered by an off-the-shelf product: claim a job from our API, fetch the source from
R2, encode, upload the outputs, report back.

- FileFlows and Tdarr automate media libraries rather than per-upload jobs.
- Jellyfin's transcoding is for live playback.
- PeerTube's remote runners use the same claim-over-HTTP pattern, but are tied to PeerTube.
- Workflow engines such as Temporal or Hatchet are far more than this needs.

The glue is a few hundred lines. Suggested shape:

- A .NET console app in this repo (e.g. `workers/`), published as its own image.
- It runs ffmpeg directly and moves files through presigned R2 URLs that come with each job, so it holds no
  storage keys (as built: [The worker](#the-worker-built-in-214)).
- It talks to the API only: authenticated claim, progress and complete endpoints over HTTPS. The API keeps the
  `transcode_run` table and applies results through `ClipVideoUpdateHandler`.

## Job types

One queue (`transcode_run`) with a job type per row, claimed in priority order like the other run tables:

| Type         | Input                                                  | Output                                                                                                        |
| ------------ | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `transcode`  | The uploaded source in R2, plus any trim set at upload | A new video version: AV1 MP4, thumbnail, five detection frames, animated preview; length and size             |
| `thumbnails` | An existing AV1 output                                 | Regenerated thumbnail and detection frames, without re-encoding                                               |
| `trim`       | The current AV1 output and the new trim                | A new, shorter video version (stream copy, or re-encode if frame-accurate)                                    |
| `fallback`   | An existing output (later)                             | The on-demand H.264 file (see [playback-and-codecs.md](playback-and-codecs.md))                               |
| `migrate`    | A Bunny clip's HLS master playlist (#239, #240)        | The same as `transcode`, after remuxing the HLS to a local MP4 (see [bunny-migration.md](bunny-migration.md)) |

**Thumbnails are a step of `transcode`, not a separate job.** The worker already has the source on disk, and
grabbing frames takes seconds; a separate job would fetch the source again. Two refinements:

- **Report thumbnails early,** before the encode finishes. Clip cards then show a real image while the clip is
  processing, and legend detection, which needs only the frames, can be queued on "thumbnails ready" instead of
  waiting for `ready`. The API hands out an R2 clip's thumbnail and detection frames only from that report (or
  once the clip is `ready`), read from `transcode_run.thumbnails_ready_at` for the clip's current version, so
  nothing requests them before they exist and caches the 404 (#229, see
  [r2-bucket.md](r2-bucket.md#caching-cloudflare-cache-rule)).
- **Keep `thumbnails` as its own job type** for regenerating frames without re-encoding: clips migrated from Bunny,
  or a change to the detection-frame timing.

## The queue and the worker API (built in #211)

The queue is `transcode_run` (migration V40) and the API side is in `api/Video/Transcoding/`. The R2 host
(`api/Video/R2/R2VideoHost.cs`, #212) calls `TranscodeQueue.EnqueueAsync(clipId, "transcode", input)` in the same
transaction that marks the clip `uploaded`, once a server-side upload (Twitch import) completes, or a browser
upload completes and passes its checks (#213, see [r2-bucket.md](r2-bucket.md#browser-uploads-213)).

- **A row per job:** `type`, `status` (`pending` → `running` → `succeeded` | `failed`), `priority` (lower first;
  `transcode` 0, `trim` and `fallback` 1, `thumbnails` 2), `attempts`, `input` (jsonb: `source_key`, trim points),
  `output` (jsonb: `outputs`, `length_seconds`, `size_bytes`), `video_version`, `error` and timestamps. A partial
  unique index allows one pending or running `transcode` per clip; enqueueing another returns null.
- **Claim:** `POST /api/worker/claim` returns 204 when there is nothing to do, or the job: `run_id`, `lease_id`,
  `clip_id`, `type`, `video_version`, `attempt`, `input`, and `source_url` / `upload_urls`: a presigned GET for the
  job's input and a presigned PUT per output, by file name, which sign `Content-Type` and `Cache-Control` (see
  [r2-bucket.md](r2-bucket.md#presigned-urls-for-workers)); both are null while R2 isn't configured. The claim is
  the same `FOR UPDATE SKIP LOCKED` update as the other run tables. It takes a pending job whose retry delay has passed (`Transcoding:RetryAfter`, 2 minutes),
  or reclaims a running one whose worker has sent neither a claim nor a progress report for `Transcoding:StaleAfter`
  (1 hour, longer than the worst-case encode). Before that, it fails the jobs that have used
  `Transcoding:MaxAttempts` (3), and a failed `transcode` sets its clip to `encode_failed`. Workers pull, so the API
  runs no background service for this.
- **Leases:** each claim issues a new `lease_id`, and every report must carry it. A worker whose job was reclaimed
  after it went quiet gets 409 and should drop the job, so it can never report over the worker that holds it now.
  Repeating a completion or failure under the same lease (after a lost response) is accepted and changes nothing.
- **Reports:** `POST /api/worker/{runId}/progress` (`percent`, `thumbnails_ready`; also the heartbeat),
  `/complete` (`outputs`, `length_seconds`, `size_bytes`), `/fail` (`error`, `retryable`) and `/release` (the
  lease alone). A retryable failure with attempts left goes back to pending; anything else fails the job.
- **Release** is how a worker that is shutting down (a deploy, a restart) hands its job back without it counting as
  a failure. The job is `pending` again and claimable at once; its lease, `heartbeat_at` and `progress` are cleared,
  and the attempt the claim counted is refunded (never below 0). A `transcode`'s clip goes from `processing` back to
  `uploaded` in the same transaction, through `ClipVideoUpdateHandler`. Repeating it under the same lease is accepted,
  and any other lease gets 409. So that a worker restarted for ever (a crash loop that happens to stop it cleanly)
  can't bounce a job without end, only the first `Transcoding:MaxReleases` (10) releases of a job refund; after that
  a release still requeues the job but keeps the attempt, and `Transcoding:MaxAttempts` ends it as usual. The count
  and the lease it was released under live in the run's `output` (`release_count`, `released_lease_id`), which a
  pending job doesn't otherwise use; a completion keeps `release_count` next to what the worker reported. Workers
  never release a job that failed: crashes and errors go through `/fail`, so a source that always crashes ffmpeg
  still ends `encode_failed`.
- **Applying results:** `transcode` and `trim` jobs change the clip, through `ClipVideoUpdateHandler`, in the same
  transaction as the run update. A transcode's progress sets `processing` and `encode_progress`; completion of
  either sets `ready` on the job's `video_version`, with `length`, `storage_size` (the AV1 size, which alone the
  quota counts from then on: `file_size` is cleared), and the `codecs` and `height` from `outputs` for the clip's
  playback source. After the commit the uploaded source is deleted (a transcode) and so is the version the clip moved
  off (a trim), and a transcode queues legend detection and transcription as Bunny's webhook does. A clip that is
  already `ready` (a re-encode of a migrated clip, or a trim) stays ready and playable throughout. `thumbnails` and
  `fallback` record their output on the run until the work that uses them decides what they change. The legacy `clip.video_status` column holds Bunny's status number for the previous image; it is never
  written for these clips and stays NULL until the column is dropped.
- **Early detection:** with `Transcoding:QueueDetectionOnThumbnails` (off by default), the first progress report
  with `thumbnails_ready` queues legend detection instead of waiting for the encode.
- **Moving clips off Bunny (`migrate`, #239, V44):** see [bunny-migration.md](bunny-migration.md) for the plan.
  `BunnyMigrationBackfillService` (`api/Video/Migration/`) queues them where `Migration:Enabled` is true (default
  false) and R2 is configured, keeping up to `Migration:BatchSize` (20) pending or running, from the `ready` clips on
  `bunny`, oldest first. It skips a clip with any job pending or running, and any clip that has ever had a `migrate`:
  a failed one is for a person to look at, and a succeeded one whose clip is on Bunny again was switched back on
  purpose. Deleting the run lets the backfill take the clip again. They are queued at priority 10, so every other job
  goes first. The claim never hands one out without R2 (unlike the other types, which go out with null URLs), and
  fails one whose clip is no longer on Bunny instead of handing it out. A claimed `migrate` has `video_version` 1, an
  empty `input`, a `source_url` that is the clip's public, unsigned Bunny HLS master playlist
  (`https://<pull zone>/{videoId}/playlist.m3u8`, from `BunnyMediaUrls`), and the `transcode` keys under
  `clips/{clipId}/v1/` in `upload_urls`. Progress and completion bodies are those of a `transcode`, and
  `retryable: false` means the Bunny source is gone or unreadable. Progress, release and failure never touch the
  clip, which plays from Bunny throughout; a final failure leaves it there. Completion moves a clip still on
  `bunny`, in the run's transaction: `bunny_video_id` takes its Bunny `video_id`, `migrated_at` is set, and it is
  `ready` on `r2` with `video_id` its clip id, `video_version` 1, the codecs, height, length and AV1 size reported,
  and `file_size` and the legacy `video_status` cleared. Nothing is queued after it, since detection, transcripts and
  summaries are keyed by clip. If the clip left Bunny meanwhile, the run still succeeds, the clip is left alone, and
  `clips/{clipId}/v1/` is deleted, unless the clip itself plays from R2 version 1. A clip deleted meanwhile takes its
  job with it (`transcode_run` cascades from `clip`, so the worker's completion gets 404), so deleting a Bunny clip
  also deletes `clips/{clipId}/v1/`; a PUT the worker makes after that is left in the bucket.
- **Credential:** the endpoints require the `Worker` policy: `Authorization: Bearer <Worker:Secret>`, compared in
  constant time. A secret that is unset or shorter than 32 characters refuses every request. The site cookie never
  satisfies the policy, and the secret authorises nothing else. Requests are rate limited per address
  (`Worker:RequestsPerMinute`, 600) against a worker stuck in a loop; the limiter runs after authorization, so it
  doesn't slow guessing, which a 32-character random secret makes pointless anyway. The endpoints are left out of
  the OpenAPI document, since the worker isn't a client of the generated frontend API.

## The worker (built in #214)

`workers/Reelshelf.Worker/` is a .NET console app (generic host) with its own image, `ghcr.io/pluscosmic/reelshelf-worker`
(`workers/Reelshelf.Worker/Dockerfile`: `debian:trixie-slim` with the worker published self-contained and Debian's ffmpeg 7.1 and SVT-AV1 2.3, running as
the image's unprivileged `app` user; the build checks that this ffmpeg has the `https` protocol and the HLS demuxer, which
`migrate` needs). It calls ffmpeg and ffprobe as processes with argument lists rather than
through FFMpegCore, so it can run them under `nice` and read `-progress`. Its tests are in `tests/Reelshelf.Worker.Test/`.

- **Configuration** (`Worker__*` environment variables, `WorkerSettings`): `ApiBaseUrl` and `Secret` (the API's
  `Worker:Secret`) are required, and startup fails without them or without libsvtav1 and libopus in ffmpeg. The rest
  have defaults: `Preset` (SVT-AV1, 8), `LogicalProcessors` (SVT-AV1 `lp`, 0 = all cores), `TargetBitrateKbps`
  (4000), `AudioBitrateKbps` (128), `Niceness` (10), `IdlePollInterval`/`MaxIdlePollInterval` (5 s doubling to 60 s),
  `ErrorBackoff`/`MaxErrorBackoff` (5 s to 5 min), `HeartbeatInterval` (30 s), `ShutdownGrace` (20 s),
  `MaxSourceBytes` (8 GiB, which also caps a remuxed HLS source), `RemuxTimeout` (20 min), `WorkDirectory` (`/work` in the image) and `FfmpegPath`/`FfprobePath`. It holds no
  storage keys: everything goes through the job's presigned URLs, and the secret is only ever sent to the API.
- **Loop:** one job at a time. Claim; on 204 wait, doubling while the queue stays empty; on an API error, back off
  the same way. Each job gets its own directory, deleted when it ends (and leftovers from a crash are deleted at
  startup). The source is streamed to disk and probed: one ffprobe can't read, with no video stream or no duration,
  or a trim that keeps nothing fails with `retryable: false`.
- **Frames first:** the six frames are taken from the kept range, uploaded, and then the first progress report says
  `thumbnails_ready`. `thumbnail.jpg` is the first frame (Bunny's `thumbnailTime`, which Reelshelf sets to 0), and
  `thumbnail_1..5.jpg` split the clip into sixths (1/6 … 5/6), never the last frame. Bunny doesn't document its own
  spread; if detection scores drop on worker-made frames, re-score the labelled clips and adjust `FrameTimings`.
- **`transcode`:** `-ss`/`-t` as input options (so a trim from the input is frame-accurate), `fps=30`, fitted in
  1920×1080 (1080×1920 for portrait sources) with `force_original_aspect_ratio=decrease:force_divisible_by=2` and
  never enlarged, `format=yuv420p`, `-fps_mode cfr`, SVT-AV1 at the target bitrate (VBR) with `-g 30`, Opus stereo,
  metadata and chapters stripped (phone GPS tags), `-movflags +faststart`. Progress comes from `-progress pipe:1` and
  is reported every 5% or 10 s, and at least every heartbeat interval through the downloads and uploads. Then a
  `preview.mp4` when the job has a URL for it: 3 silent seconds from a third of the way in, at most 640×360, 15 fps,
  AV1 at CRF 45. Then `av1.mp4` is uploaded and completion reports `length_seconds` and `size_bytes` of the
  produced file and `outputs`: `codecs`, `width`, `height` and `files`.
- **HDR is tone-mapped, not refused.** HDR captures (PQ from HDR10 recording, HLG from phones) are common enough in
  game clips that refusing them would fail real uploads, and the clip only needs to look right in SDR. A source whose
  transfer is `smpte2084` or `arib-std-b67` goes through zscale to linear light, BT.709 primaries, the Hable curve
  and back to BT.709 limited range (`EncodeArguments.ToneMapFilter`), and the output is tagged BT.709;
  `outputs.tone_mapped` is true. Frames and the preview are tone-mapped the same way. An ffmpeg without zscale fails
  such jobs as not retryable. Dolby Vision profile 5 (no HDR10 base layer) would come out with wrong colours; it
  isn't detected yet.
- **`codecs`** is read back from the produced file, not assumed: `av01.<profile>.<level><tier>.<bit depth>` with
  ffprobe's profile and the `seq_level_idx` SVT-AV1 wrote (a table of AV1 levels when ffprobe doesn't report one),
  main tier, 8-bit, then `opus` when there is audio. A 1080p30 clip is `av01.0.08M.08,opus`; 720p is
  `av01.0.05M.08,opus`.
- **`trim`:** the current output is cut with `-c copy` (the start snaps to the keyframe at or before it, within a
  second) or, with `frame_accurate`, re-encoded with the same settings; the frames are then taken from the new
  version. **`thumbnails`:** only the frames, with the existing output's length, size and codecs. `fallback` fails
  as unsupported.
- **`migrate`** (#240, [bunny-migration.md](bunny-migration.md)): `source_url` is the clip's public, unsigned Bunny
  HLS master playlist, with variant playlists and segments relative to it, and `upload_urls` are a transcode's.
  `HlsRemux` copies it into the job directory as an MP4 (`EncodeArguments.HlsRemux`: `-f hls -i <playlist> -c copy`,
  no subtitles or data). ffmpeg's default selection takes the highest-resolution variant and downloads only that one,
  and its MP4 muxer converts the ADTS AAC itself. Only network protocols are allowed (`-protocol_whitelist`), so a
  playlist can't point ffmpeg at local files, a read that stalls for 30 s (`-rw_timeout`) errors, and a segment is
  retried 3 times by ffmpeg (`-seg_max_retry`). The remux runs under `nice` like every ffmpeg run, and the whole of
  it, retries included, under `Worker:RemuxTimeout` (20 min; running out fails as retryable). From there the job is a
  `transcode`, the same code unchanged: the remuxed file is probed, framed, encoded, previewed, uploaded and
  completed, and its `input` (no trim fields) is read the same way. The remux doesn't touch how a `transcode` fetches
  its source.
  - **Failures** are read from ffmpeg's log, at warning level: a 403, 404 or 410 on the playlist or a segment (the
    video is gone), or input that isn't HLS, fails with `retryable: false`. Network errors and 5xx are retried like
    storage reads (three more tries, 2, 4, 8 s), then fail as retryable. ffmpeg skips a segment it can't fetch and
    still exits 0, so a log that shows a skipped segment or a failed playlist reload counts as a failed read, not a
    clip with a hole in it.
  - **Shutdown** during the remux is like one before the encode: the job is released at once.
- **Uploads** send exactly the headers the presigned PUTs sign: `Content-Type` `video/mp4` or `image/jpeg`, and
  `Cache-Control: public, max-age=31536000, immutable`. Storage 5xx, 429 and network errors are retried three times
  (2, 4, 8 s); after that, or on a 403, the job fails as retryable so the next claim gets fresh URLs. A missing source
  (404) or one over `MaxSourceBytes` is bad input.
- **Failures and leases:** a 409 (or 404) on any report drops the job with no report, since another worker has it.
  ffmpeg failing after the probe passed fails as retryable (the API's attempt cap ends a bad file), anything
  unexpected likewise. Completions and failures are retried on network errors and 5xx, which the API accepts as
  repeats. If a failure can't be reported at all, the API reclaims the job after `Transcoding:StaleAfter`.
- **Shutdown:** on SIGTERM a job whose encode (or trim) has started gets `ShutdownGrace` to finish and report; past
  that ffmpeg is killed and the job is released (`/release`), which puts it back in the queue at once without using
  an attempt. A job still downloading, probing or taking its frames is released at once, since nothing worth waiting
  for is under way. Only the shutdown's own stop counts: an ffmpeg crash, even during the grace, fails the job as
  retryable through `/fail` and uses an attempt. The host waits `ShutdownGrace` + 15 s, so the container's stop
  timeout should be at least that (35 s by default).
- **Resource limits** are the deployment's (#215): `cpuset`, CPU weight and memory in the container, plus `lp` and
  `nice` here. The image puts the work directory at `/work` so the root filesystem can be read-only.
- **Tests:** unit tests for the argument lists, frame timing, trim input, codecs strings and ffprobe parsing, and
  `[SvtAv1Fact]` tests that run the whole loop against `FakeWorkerApi` (the worker endpoints and a bucket on a
  loopback `HttpListener`) on `lavfi` test clips: transcode with a trim, HDR at 1440p, stream-copy and frame-accurate
  trims, thumbnails, `migrate` from a `lavfi`-made two-variant HLS ladder served by the fake's storage (the 720p
  variant is the one encoded), a 503 on the playlist (retried), a 404 playlist and one that isn't HLS (both not
  retryable), a shutdown during the remux (released at once), a lost lease, unreadable input, shutdown (released mid-encode past the grace, and at once
  before the encode), an ffmpeg crash (failed, also during the grace) and an empty queue. They skip when the local ffmpeg has
  no libsvtav1. CI's runner has Ubuntu 24.04's ffmpeg (SVT-AV1 1.7), older than the image's. The image moved off
  1.7 because it segfaulted inside `libSvtAv1Enc` on the dedicated server's VM (one CPU socket per vCPU) for an
  ordinary 1080p60 H.264 clip that 2.3 encodes there; the same clip encoded with 1.7 on a one-socket desktop.

## Downloads

Built (#216). `GET /api/ffmpeg/download/{videoId}` (`api/FFmpeg/FFmpegEndpoints.cs`) checks access first, as
before: the owner or a playlist creator or collaborator (`UserCanAccessClip`), and 404 for anyone else. Then:

- **R2 clips:** there is nothing to compute. The endpoint answers with a 302 (`Cache-Control: no-store`) to a
  presigned GET of the current `clips/{clipId}/v{n}/av1.mp4` that lives 10 minutes and signs
  `response-content-disposition`, so R2 sends `attachment; filename="<title>.mp4"; filename*=UTF-8''<title>.mp4`.
  The title is cleaned by `DownloadFileName`; the quoted name keeps ASCII only and the RFC 5987 one keeps the exact
  title. Only a `ready` clip redirects; before that it's 404. The presigning lives in `api/Video/R2/R2Downloads.cs`,
  registered as the `r2` host's keyed `IClipVideoDownloads`, so `api/FFmpeg/` never names R2. No ffmpeg, temp file
  or download permit is used, and the file comes from the bucket, not the API server.
- **Bunny clips:** still remuxed from HLS into an MP4 on the API server (`FFmpegService`, with its temp files and
  concurrency gates) until they are migrated.

The download button (`frontend/src/components/Reelshelf/primitives/Navigation.tsx`) is a plain link, so the browser
follows the redirect itself; `fetch` would need CORS on the bucket. It shows "Preparing download…" only for HLS
clips, which are the ones that take a moment.

Downloads only need a worker if they should be converted to H.264 (some local players and editors can't open AV1).
That would be an on-demand, cached job, the same type as the playback fallback.

## Trimming (planned feature)

The idea: upload a 2-minute clip but keep only 30 seconds from the middle. It fits on top of the transcode job.

Originals aren't kept (see [open-questions.md](open-questions.md#decided-2026-10-18)), so there are two kinds of
trim:

- **Trim at upload, applied in the first encode.** The uploaded source is still in R2 until its transcode
  succeeds, so a trim set in the upload flow is applied to the original: frame-accurate (`-ss`/`-t` while
  re-encoding), no extra cost, and the full clip is never encoded or stored. This should be the main way to trim.
- **Trim later, from the AV1 output.** After the source is deleted, a trim can only shorten the current output, and
  can never be undone or extended. Two ways to cut it:
  - **Stream copy** (`-c copy`): no quality loss, but cut points snap to keyframes. Encoding with a keyframe every
    second (`-g 30` at 30 fps) keeps trims accurate to about a second, for a small size cost.
  - **Re-encode** the trimmed range: frame-accurate, but a second generation of compression. One trim is barely
    visible; repeated re-trims compound.

  Default to stream copy; offer frame-accurate as an option if it's missed.

- **A trim is data plus a new version.** The clip row stores `trim_start` and `trim_end`. Each trim after the first
  encode produces a new **video version** (e.g. `clips/{id}/v{n}/av1.mp4`). Immutable, versioned keys keep CDN
  caching simple, the current version keeps playing while a trim runs, and the old version is deleted once the
  new one is ready. The clip points at its current version.
- **Derived data follows the version.** Thumbnails, detection frames, legend detection, transcripts and summaries
  all come from the trimmed output, so a new version re-queues them. The run tables need to record which video
  version a run used, and treat runs on older versions as superseded, as "Start fresh" already does for legend
  detection.
- **Upload still sends the full file.** Cutting in the browser before upload isn't worth the complexity yet.
- **Quota.** Only the current output counts, so trimming a clip down frees space once the old version is deleted.
- **Unchanged:** the MD5 duplicate check is on the uploaded file, so it still works with trimming.
