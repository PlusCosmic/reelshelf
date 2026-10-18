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
- It reuses the existing ffmpeg patterns (`LegendHudCropper`, `ClipAudioExtractor`) and an S3 client pointed at R2.
- It talks to the API only: authenticated claim, progress and complete endpoints over HTTPS. The API keeps the
  `transcode_run` table and applies results through `ClipVideoUpdateHandler`.

## Job types

One queue (`transcode_run`) with a job type per row, claimed in priority order like the other run tables:

| Type         | Input                                                  | Output                                                                                            |
| ------------ | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `transcode`  | The uploaded source in R2, plus any trim set at upload | A new video version: AV1 MP4, thumbnail, five detection frames, animated preview; length and size |
| `thumbnails` | An existing AV1 output                                 | Regenerated thumbnail and detection frames, without re-encoding                                   |
| `trim`       | The current AV1 output and the new trim                | A new, shorter video version (stream copy, or re-encode if frame-accurate)                        |
| `fallback`   | An existing output (later)                             | The on-demand H.264 file (see [playback-and-codecs.md](playback-and-codecs.md))                   |

**Thumbnails are a step of `transcode`, not a separate job.** The worker already has the source on disk, and
grabbing frames takes seconds; a separate job would fetch the source again. Two refinements:

- **Report thumbnails early,** before the encode finishes. Clip cards then show a real image while the clip is
  processing, and legend detection, which needs only the frames, can be queued on "thumbnails ready" instead of
  waiting for `ready`.
- **Keep `thumbnails` as its own job type** for regenerating frames without re-encoding: clips migrated from Bunny,
  or a change to the detection-frame timing.

## The queue and the worker API (built in #211)

The queue is `transcode_run` (migration V40) and the API side is in `api/Video/Transcoding/`. Nothing enqueues
into it yet: the R2 host will call `TranscodeQueue.EnqueueAsync(clipId, type, input)` when an upload completes.

- **A row per job:** `type`, `status` (`pending` → `running` → `succeeded` | `failed`), `priority` (lower first;
  `transcode` 0, `trim` and `fallback` 1, `thumbnails` 2), `attempts`, `input` (jsonb: `source_key`, trim points),
  `output` (jsonb: `outputs`, `length_seconds`, `size_bytes`), `video_version`, `error` and timestamps. A partial
  unique index allows one pending or running `transcode` per clip; enqueueing another returns null.
- **Claim:** `POST /api/worker/claim` returns 204 when there is nothing to do, or the job: `run_id`, `lease_id`,
  `clip_id`, `type`, `video_version`, `attempt`, `input`, and `source_url` / `upload_urls`, which stay null until the
  R2 host presigns a GET for the source and a PUT per output. The claim is the same `FOR UPDATE SKIP LOCKED` update
  as the other run tables. It takes a pending job whose retry delay has passed (`Transcoding:RetryAfter`, 2 minutes),
  or reclaims a running one whose worker has sent neither a claim nor a progress report for `Transcoding:StaleAfter`
  (1 hour, longer than the worst-case encode). Before that, it fails the jobs that have used
  `Transcoding:MaxAttempts` (3), and a failed `transcode` sets its clip to `encode_failed`. Workers pull, so the API
  runs no background service for this.
- **Leases:** each claim issues a new `lease_id`, and every report must carry it. A worker whose job was reclaimed
  after it went quiet gets 409 and should drop the job, so it can never report over the worker that holds it now.
  Repeating a completion or failure under the same lease (after a lost response) is accepted and changes nothing.
- **Reports:** `POST /api/worker/{runId}/progress` (`percent`, `thumbnails_ready`; also the heartbeat),
  `/complete` (`outputs`, `length_seconds`, `size_bytes`) and `/fail` (`error`, `retryable`). A retryable failure
  with attempts left goes back to pending; anything else fails the job.
- **Applying results:** only `transcode` jobs change the clip, through `ClipVideoUpdateHandler`, in the same
  transaction as the run update. Progress sets `processing` and `encode_progress`; completion sets `ready`, `length`
  and `storage_size` (which the quota counts), then queues legend detection and transcription as Bunny's webhook
  does. A clip that is already `ready` (a re-encode of a migrated clip) stays ready and playable throughout. The
  other job types record their output on the run until the trim, thumbnails and fallback work decides what they
  change. The legacy `clip.video_status` column holds Bunny's status number for the previous image; it is never
  written for these clips and stays NULL until the column is dropped.
- **Early detection:** with `Transcoding:QueueDetectionOnThumbnails` (off by default), the first progress report
  with `thumbnails_ready` queues legend detection instead of waiting for the encode.
- **Credential:** the endpoints require the `Worker` policy: `Authorization: Bearer <Worker:Secret>`, compared in
  constant time. A secret that is unset or shorter than 32 characters refuses every request. The site cookie never
  satisfies the policy, and the secret authorises nothing else. Requests are rate limited per address
  (`Worker:RequestsPerMinute`, 600) against a worker stuck in a loop; the limiter runs after authorization, so it
  doesn't slow guessing, which a 32-character random secret makes pointless anyway. The endpoints are left out of
  the OpenAPI document, since the worker isn't a client of the generated frontend API.

## Downloads

The download endpoint (`api/FFmpeg/`) remuxes Bunny's HLS into an MP4 on the API server. With progressive MP4 in R2
there is nothing to compute: a download becomes a redirect to a presigned R2 URL with
`response-content-disposition=attachment`. The ffmpeg remux, temp files and download concurrency gates in
`FFmpegService` go away, and so does the load on the main server.

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
