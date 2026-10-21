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
- Each job targets the clip's **next** video version, `video_version + 1`, recorded on the run's `video_version`
  when it is queued. A clip never moved before is on version 1 (the column's default), so it goes to v2; one that
  moved, was switched back and is moved again goes past every version it played from. Outputs are cached
  `immutable` for a year, so writing new files into keys a clip already played from would leave Cloudflare and
  browsers serving the old ones. Versions are opaque, so skipping v1 costs nothing.
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
  - `upload_urls`: the usual transcode keys under `clips/{clipId}/v{n}/`, `n` being the run's `video_version`.
- The worker reports progress as it does for a `transcode`, but the clip isn't touched.

### Worker

- A `migrate` job first remuxes the HLS input to a local MP4 (`ffmpeg -i <playlist> -c copy`, retried like any
  storage read). It then follows the `transcode` path unchanged: ffprobe, six frames, `thumbnails_ready`, the
  AV1 encode, `preview.mp4`, uploads, and completion with `codecs`, `width`, `height`, `files`, `length_seconds`
  and `size_bytes`. Like every encode it keeps the source's frame rate, capped at 60 fps, so a clip Bunny stored at
  60 fps stays at 60 ([the worker](workers-and-jobs.md#the-worker-built-in-214)).
- An HLS input that can't be read (a 404 or 403 on the playlist) fails as `retryable: false`, since retrying
  won't bring the video back.

### API: completion

In one transaction with the run:

- `bunny_video_id` gets the old `video_id` and `migrated_at` gets now();
- `video_host = 'r2'`, `video_id` = clip id, `video_version` = the run's version;
- `video_codecs`, `video_height`, `length` and `storage_size` are set (`file_size` is cleared, as for a
  transcode), and `video_state = 'ready'`;
- the clip's container membership (`clip_collection`) is left alone for the sweep.

Detection and transcription aren't queued. Once that transaction has committed, every other version under
`clips/{clipId}/` is deleted (an earlier move's files, when the clip was switched back and moved again): the
objects under the prefix are listed and everything outside `clips/{clipId}/v{n}/` goes. The clip is read again
first, and nothing is deleted unless it still plays from R2 version `n`, so the version it plays from always stays;
`uploads/` is never touched. A failure there is only logged: the completion has already succeeded, and the leftovers
stay until the clip is deleted. A repeated completion changes and deletes nothing.

If the clip isn't on Bunny any more by the time the job completes, the run succeeds, the clip isn't touched, and
the job's own `clips/{clipId}/v{n}/` objects are deleted (unless the clip itself plays from R2 version `n`). A
deleted clip takes its run with it (`transcode_run` cascades from `clip`), so the worker's completion gets 404;
deleting a Bunny clip therefore deletes everything under `clips/{clipId}/` itself, and only a PUT the worker makes
after that is left behind. Status polling, the webhook and the metadata backfill write what
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

### Reverting and re-running the migration

To put moved clips back on their Bunny copies and move them again (for instance after the encode settings
changed, as when the worker started keeping 60 fps instead of forcing 30):

1. Set `Migration:Enabled` and `Migration:DeleteBunny` to false, on every instance, and wait for the deploy. The
   backfill then queues nothing, and the sweep deletes no Bunny copy while you revert. Make sure every worker runs
   the encode you want (the re-run uses whatever the workers do), and wait until no `migrate` run is `running`, so
   no worker cut off in step 2 is still writing files.
2. In one transaction:

   ```sql
   begin;
   delete from transcode_run where type = 'migrate';
   update clip set video_host = 'bunny', video_id = bunny_video_id, bunny_video_id = null,
     video_codecs = null, video_height = null, migrated_at = null
   where bunny_video_id is not null and video_host = 'r2' and video_state = 'ready';
   commit;
   ```

   Leave `video_version` alone, so the re-run goes to the next version and never reuses keys a clip played from.
   SET reads the row as it was, so `video_id` takes the old `bunny_video_id`. Deleting the `migrate` runs lets the
   backfill take the clips again; a run still in flight then gets 404 on completion. To revert one clip, add
   `and id = '<clip id>'` to the update and delete only that clip's `migrate` runs.

3. Turn both settings back on. The re-run moves each clip to `video_version + 1` and, once it completes, deletes
   the earlier versions under `clips/{clipId}/` by itself, including any files a run cut off in step 2 had uploaded.

The reverted clips play from Bunny again at once. Status polling picks them up, and no URL is cached against an R2
version that wasn't there before (#229). `length` and `storage_size` keep the R2 encode's values until the re-run
sets them again.

- A clip whose Bunny copy the sweep has already deleted (`bunny_video_id` is null) can't be reverted: the update
  leaves it on R2, and the backfill won't move it again.
- A clip trimmed on R2 after its move loses that trim: its Bunny copy is the untrimmed original, and the re-run
  encodes that.

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
