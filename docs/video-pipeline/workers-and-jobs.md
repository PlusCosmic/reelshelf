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

| Type         | Input                                  | Output                                                                                            |
| ------------ | -------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `transcode`  | The source in R2, plus the clip's trim | A new video version: AV1 MP4, thumbnail, five detection frames, animated preview; length and size |
| `thumbnails` | An existing AV1 output                 | Regenerated thumbnail and detection frames, without re-encoding                                   |
| `fallback`   | An existing output (later)             | The on-demand H.264 file (see [playback-and-codecs.md](playback-and-codecs.md))                   |

**Thumbnails are a step of `transcode`, not a separate job.** The worker already has the source on disk, and
grabbing frames takes seconds; a separate job would fetch the source again. Two refinements:

- **Report thumbnails early,** before the encode finishes. Clip cards then show a real image while the clip is
  processing, and legend detection, which needs only the frames, can be queued on "thumbnails ready" instead of
  waiting for `ready`.
- **Keep `thumbnails` as its own job type** for regenerating frames without re-encoding: clips migrated from Bunny,
  or a change to the detection-frame timing.

## Downloads

The download endpoint (`api/FFmpeg/`) remuxes Bunny's HLS into an MP4 on the API server. With progressive MP4 in R2
there is nothing to compute: a download becomes a redirect to a presigned R2 URL with
`response-content-disposition=attachment`. The ffmpeg remux, temp files and download concurrency gates in
`FFmpegService` go away, and so does the load on the main server.

Downloads only need a worker if they should be converted to H.264 (some local players and editors can't open AV1).
That would be an on-demand, cached job, the same type as the playback fallback.

## Trimming (planned feature)

The idea: upload a 2-minute clip but keep only 30 seconds from the middle. It fits on top of the transcode job.

- **Trim at encode time.** We re-encode anyway, so seeking with `-ss`/`-t` during the encode is frame-accurate and
  costs nothing extra.
- **A trim is data, not a new file.** The clip row stores `trim_start` and `trim_end`. A `transcode` job takes the
  source plus the trim and produces a new **video version** (e.g. `clips/{id}/v{n}/av1.mp4`). Immutable,
  versioned keys keep CDN caching simple, and the current version keeps playing while a re-trim encodes. The clip
  points at its current version.
- **Re-trimming needs the original.** Re-encoding a trimmed AV1 output loses quality, and cutting without
  re-encoding is limited to keyframes. This settles most of the originals question (see
  [open-questions.md](open-questions.md)). One option is to keep the original for a grace period (say 30 days),
  then delete it, after which the clip can no longer be re-trimmed.
- **Derived data follows the version.** Thumbnails, detection frames, legend detection, transcripts and summaries
  all come from the trimmed output, so a new version re-queues them. The run tables need to record which video
  version a run used, and treat runs on older versions as superseded, as "Start fresh" already does for legend
  detection.
- **Trim before the first encode.** A trim step in the upload UI means the full clip is never encoded. The full file
  is still uploaded; cutting in the browser before upload isn't worth the complexity yet.
- **Quota.** Decide whether a retained original counts toward the owner's storage, and whether trimming down frees
  space.
- **Unchanged:** the MD5 duplicate check is on the uploaded file, so it still works with trimming.
