# Moving Bunny clips to R2

Plan for moving every clip still on Bunny onto our own pipeline (R2 + AV1), then leaving Bunny. Written
2026-10-18, after the first prod clip went through R2 end to end. Built in #239 (API, V44), #240 (worker) and
#241 (the delete sweep), under #208.

## What there is to move

Prod's Bunny library on 2026-10-18: **820 clips, 25.2 hours, 87 GB** on Bunny (every rendition counted).

- **Storage after:** the encode targets 4 Mbps video plus 128 kbps Opus, so 25.2 h comes to about 47 GB at most,
  and less for clips under 1080p or simple scenes. That's well under $1 a month on R2, with no egress charge.
- **Worker time:** not benchmarked yet (#217). SVT-AV1 preset 8 on the worker's four cores runs at roughly
  real time for 1080p30, give or take a factor of two. So 820 clips take about **one day of worker time**
  (somewhere between half a day and two days), as low-priority work that new uploads always jump ahead of.
- **Input:** Bunny may hold only its own transcodes, not the files people uploaded. We re-encode Bunny's best
  HLS rendition, which costs one more generation of loss. At our bitrate that's acceptable for game clips. If
  the library keeps originals (Bunny's "Keep original files"), those are the better input. Check the setting
  before starting.

## Decisions (2026-10-18)

- **A new `migrate` job type** on the existing queue (`transcode_run`). It reuses claiming, leases, progress,
  release and retries. It's queued at priority 10 (lower is claimed first; `TranscodeRunTypes.DefaultPriority` gives the others 0 to
  2), so new uploads, trims and thumbnails always go first.
  Why: a separate mechanism would duplicate all of that.
- **The clip stays on Bunny, and playable, until the migration completes.** A running or failed `migrate`
  never changes `video_state`, `encode_progress` or what the clip shows. Only completion switches it over.
- **Only `ready` Bunny clips are migrated.** Clips that failed on Bunny (`encode_failed`, `upload_failed`) have
  nothing playable to move. They stay as they are, and the final cleanup lists them so the user can decide
  before Bunny is cancelled.
- **Bunny copies are kept for 7 days** after their clip moves (`Migration:DeleteBunnyAfter`). That leaves time
  to notice a bad encode and switch the clip back. A sweep then deletes the Bunny video. Bunny collections are
  deleted once no clip refers to them.
- **Derived data stays.** Legend detection, transcripts, summaries and labels are keyed by clip id. Completing
  a migration doesn't queue detection or transcription again.
- **Dev first.** Dev has its own database and its own Bunny videos (`scripts/dev-db/README.md`), so the whole
  run, including the delete sweep, can be tried there without touching prod's Bunny copies.

## How it works

### Schema (V44)

- `transcode_run.type` gains `migrate` (the check constraint).
- `clip.bunny_video_id uuid null`: the clip's old Bunny video, kept after the switch until the sweep deletes it.
- `clip.migrated_at timestamptz null`: when the clip moved. The grace period counts from it.

### API: queueing (`Migration:*`)

- A backfill service keeps up to `Migration:BatchSize` (default 20) `migrate` jobs pending or running, taking
  `ready` clips with `video_host = 'bunny'` oldest first. It only runs where `Migration:Enabled` is true
  (default false) and R2 is configured. It never queues a clip that already has a pending or running job of any type,
  nor one that has had a `migrate` before (failed, or succeeded and then switched back); deleting that run lets
  the backfill take the clip again.
- `Migration:BatchSize` must be larger than the total number of workers: the dedicated worker plus
  `Overflow:MaxTasks` overflow tasks ([the overflow pool](workers-and-jobs.md#the-overflow-pool-built-in-248)).
  Otherwise some workers sit idle, and overflow tasks started for the backlog exit with nothing to claim. The
  overflow pool counts `migrate` jobs by default (`Overflow:JobTypes` empty), so the migration can start tasks.
- Without R2 the claim never hands out a `migrate` job, and it fails one whose clip is no longer on Bunny instead
  of handing it out.
- Claiming a `migrate` job gives:
  - `source_url`: the clip's Bunny HLS playlist (`BunnyMediaUrls`, the same stream ffmpeg reads for downloads
    today). It's a public CDN URL, not a storage key, so the job's input carries no `SourceKey`, and the claim
    builds it from the clip's current Bunny `video_id`. ffmpeg's HLS demuxer picks the highest-resolution
    variant by default.
  - `upload_urls`: the usual transcode keys under `clips/{clipId}/v1/`.
- The worker reports progress as it does for a `transcode`, but the clip isn't touched.

### Worker

- A `migrate` job first remuxes the HLS input to a local MP4 (`ffmpeg -i <playlist> -c copy`, retried like any
  storage read). It then follows the `transcode` path unchanged: ffprobe, six frames, `thumbnails_ready`, the
  AV1 encode, `preview.mp4`, uploads, and completion with `codecs`, `width`, `height`, `files`, `length_seconds`
  and `size_bytes`.
- An HLS input that can't be read (a 404 or 403 on the playlist) fails as `retryable: false`, since retrying
  won't bring the video back.

### API: completion

In one transaction with the run:

- `bunny_video_id` gets the old `video_id` and `migrated_at` gets now();
- `video_host = 'r2'`, `video_id` = clip id, `video_version = 1`;
- `video_codecs`, `video_height`, `length` and `storage_size` are set (`file_size` is cleared, as for a
  transcode), and `video_state = 'ready'`;
- the clip's container membership (`clip_collection`) is left alone for the sweep.

Detection and transcription aren't queued. If the clip isn't on Bunny any more by the time the job completes, the
run succeeds, the clip isn't touched, and the uploaded `clips/{clipId}/v1/` objects are deleted (unless the clip
itself plays from R2 version 1). A deleted clip takes its run with it (`transcode_run` cascades from `clip`), so
the worker's completion gets 404; deleting a Bunny clip therefore deletes `clips/{clipId}/v1/` itself, and only a
PUT the worker makes after that is left behind. Status polling, the webhook and the metadata backfill write what
Bunny reports only while the clip is still on Bunny with that video, so none of them can overwrite a clip that has
just moved. A final failure leaves the clip on Bunny, and the run's error shows why. A later backfill pass doesn't
pick it up again automatically: clips with a failed `migrate` are left for a person to look at.

### API: the sweep

Where `Migration:DeleteBunny` is true (default false), a background pass (`BunnyCopySweepService`, hourly, never in
the `OpenApi` environment; the option is read on every pass):

- deletes the Bunny video of every clip whose `migrated_at` is older than `Migration:DeleteBunnyAfter`
  (default 7 days), through the Bunny host, 20 at a time with 500 ms between Bunny calls, oldest move first. Each
  delete happens in a transaction that locks the clip row and re-checks that the copy is still due and that **no
  clip plays from it** (`video_host = 'bunny'` with that `video_id`, whichever clip that is). Only then is Bunny
  called, and `bunny_video_id` cleared in the same transaction (`migrated_at` stays as a record of the move);
- then deletes each Bunny collection that no clip still on Bunny, and no undeleted `bunny_video_id`, refers to.
  `clip_collection` holds one collection per owner and game category, so a collection is in use while any of that
  owner's clips in that category is on Bunny (a reserved upload included) or still has a `bunny_video_id`. The row
  and the Bunny collection are deleted under the owner's upload lock (`clip-storage:{owner}`, the one an upload
  reserves its clip under before it looks the collection up), so an upload either keeps the collection in use or
  makes a new one; no video is filed into a collection that is being deleted.

A 404 from Bunny counts as already deleted. Any other error is logged, that copy or collection is skipped for the
rest of the pass, and the next pass retries it. While `Migration:Enabled` is on and `Migration:DeleteBunny` is off,
the sweep logs a warning, since the privacy policy says Bunny copies are deleted after 7 days: turn both on together.

A clip deleted during its grace period would leave its copy behind, since the sweep finds copies through the clip
row. So deleting a clip (`ClipService.DeleteClip`, which the uploader's cancel uses, and the abandoned-upload purge)
and account deletion delete a pending `bunny_video_id` at Bunny too (`ClipVideoDeletion`), and account deletion does
so before it deletes the account's collections.

### Switching a clip back (within the grace period)

```sql
update clip set video_host = 'bunny', video_id = bunny_video_id, bunny_video_id = null, video_version = 1,
  video_codecs = null, video_height = null, migrated_at = null
where id = '<clip id>' and bunny_video_id is not null;
```

(`video_version` is `NOT NULL`, and Bunny clips keep 1. SET reads the row as it was, so `video_id` takes the old
`bunny_video_id`.) It waits for a sweep deleting that copy right then, and then changes nothing, since
`bunny_video_id` is gone; the sweep never deletes a copy the clip plays from, even if `bunny_video_id` were left set. Then delete `clips/{clipId}/` from the bucket. The
clip's succeeded `migrate` run keeps the backfill from queueing it again; delete the run to try once more. The
clip shows Bunny's copy again. Status polling picks it up, and no URL is cached against the R2 copy that wasn't
there before (#229).

## After the last clip

1. Check that no clip has `video_host = 'bunny'`, apart from the failed ones the user has decided about. Then let
   the sweep finish.
2. Remove the Bunny host: `api/Bunny/`, its registration, the webhook, `ClipStatusRefreshService`'s Bunny
   polling, the HLS download remux in `FFmpegService`, hls.js, the Activity's `/bunny-cdn` path and URL Mapping,
   and the `video_status` column. Drop `bunny_video_id`.
3. Transcription reads audio from "the Bunny HLS stream" today. It moves to the clip's `ClipMediaUrls` stream,
   which is already host-agnostic, so check that before removing the code.
4. Update the privacy policy: Bunny no longer processes videos. Then cancel the Bunny library and delete its
   settings (`Bunny*` secrets) from both deployments.
5. Move the decisions in this folder into an ADR, and delete the folder.

## Considered

- **Copying Bunny's MP4 fallback** (`play_720p.mp4` and similar) instead of re-encoding: it's capped at 720p,
  it's H.264, and it may not be enabled. Rejected.
- **Bulk export through Bunny's API, encoding locally:** faster on a desktop, but it skips the worker's checks
  and the per-clip transactions. Rejected; the queue already does this safely.
- **Deleting each Bunny copy as soon as its clip moves:** no way back from a bad encode. Rejected for a 7-day
  grace.
