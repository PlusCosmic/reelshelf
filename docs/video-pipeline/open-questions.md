# Open questions and follow-ups

## From the storage and transcoding discussion

1. **Encode benchmark:** time SVT-AV1 on real clips, including a 4K60 HDR one and a fast-paced shooter. The
   calculator assumes 10 vCPU-minutes per output minute, and that figure drives every compute cost.
2. **Bitrate tuning:** test quality with VMAF against the assumed 4 Mbps AV1, adjusting bitrate per clip by
   content.
3. **Real usage:** replace the calculator's assumptions with measured views per clip, retention and cache hit
   ratio, and watch for the 3 TB cold-storage trigger.

## Decided (2026-10-18)

- **No originals.** The uploaded source is deleted once its transcode succeeds, as with Bunny today: 4K60 sources
  are too large to keep. Anything made later (a fallback, new bitrates, a trim) works from the AV1 output, and that
  generation loss is accepted. Incomplete multipart uploads are aborted by an R2 lifecycle rule.
- **30 fps output**, the usual rate for web viewing. 60 fps would cost more storage (as a rule of thumb about 1.3–1.6×
  the bitrate at the same quality, rather than 2×; the benchmark will show the real figure).
- **Quota** counts the AV1 output only (`clip.storage_size`), plus `file_size` until the encode reports its size.

## Undecided

- **Queue: SQS or our Postgres run tables?** The storage discussion picked SQS. The app already queues work in
  Postgres run tables (`legend_detection_run`, `clip_transcription_run`, `clip_summary_run`): a background service
  claims the next row with `FOR UPDATE SKIP LOCKED`, counts attempts, retries after a delay, reclaims a run whose
  worker went quiet (`ClaimNextRunAsync` in `api/ApexLegends/LegendDetection/LegendDetectionStatements.cs`), and
  orders by priority (manual before automatic before eval).
  - **For Postgres:**
    - Enqueueing commits in the same transaction as the clip update, so no job is lost or orphaned. With SQS that
      takes an outbox, or relying on the status refresh to re-enqueue.
    - Priority is just `ORDER BY`, which the H.264 fallback job would need. Standard SQS has no priority, so it
      would take a second queue.
    - Job history is queryable for a review page, like `/legend-detection`.
    - It's one pattern the code and agents already know, with no new credentials.
  - **For SQS:**
    - Workers outside the server, such as Fargate Spot, reach it without our database being exposed.
    - `ApproximateAgeOfOldestMessage` is a CloudWatch metric that can scale Fargate directly.
    - Visibility timeouts and a dead-letter queue are built in.
  - **Middle option:** keep a `transcode_run` table as the queue, and have workers claim and report through
    authenticated API endpoints over HTTPS rather than connecting to Postgres. The API runs the same
    `SKIP LOCKED` claim and applies results through `ClipVideoUpdateHandler`. For overflow, the API publishes the
    oldest pending job's age as a custom CloudWatch metric, or starts Fargate tasks itself with `RunTask`. That keeps
    one queue pattern and leaves the database private, at the cost of building the scaling signal.
  - **Leaning (2026-10-18):** keep Postgres run tables as the queue for everything, the transcode jobs included,
    with workers going through the API as in the middle option. Moving the existing queues to SQS for
    observability was considered and set aside: SQS only shows queue-level metrics, so the run tables (and the
    review pages built on them) would stay anyway, giving two sources of truth. Get the observability instead from
    queue metrics the API publishes for every run table: pending count, oldest pending age, in progress, recent
    failures and exhausted attempts, as OpenTelemetry/Prometheus or CloudWatch custom metrics. Reconsider SQS only
    if external workers need it.
  - **Queue metrics exist (#205):** the API samples `legend_detection_run`, `clip_transcription_run` and
    `clip_summary_run` and publishes pending, retrying, oldest pending age, running, stale running, and succeeded,
    failed and exhausted in the last hour as gauges on the `Reelshelf.Queues` meter, exported over OTLP when
    `QueueMetrics:Exporter=otlp` (see `api/QueueMetrics/` and AGENTS.md). `transcode_run` is not sampled yet
    (a follow-up): its columns differ from the other run tables (`type` rather than `trigger`, `available_at` for
    the retry delay, `heartbeat_at` for staleness, `finished_at`), so it needs its own `IRunQueueMetricsSource`
    registered in `QueueMetricsSetup` rather than the shared `RunTableMetricsSource`. Its oldest pending age is the
    overflow scaling signal. Still open: where the OTLP goes (a collector forwarding to CloudWatch for Fargate
    scaling, or Prometheus/Grafana in `PlusCosmic/infrastructure`), and the alarms (oldest pending above N
    minutes, any exhausted runs).
- **Fallback:** the storage discussion lists an H.264 720p fallback, pre-generated or made on first request.
  Current call (2026-10-18): none for now, since everyone using Reelshelf can decode AV1. Build the seams (a
  `sources` list, a "can't play on this device" message, room in the queue for a fallback job) and add an
  on-demand fallback when needed. See [playback-and-codecs.md](playback-and-codecs.md).
- **Downloads:** a presigned R2 redirect, with no work on the API server (built in #216, see
  [workers-and-jobs.md](workers-and-jobs.md#downloads)). Still open: serve the AV1 file as-is, or convert to H.264
  on demand as a worker job (some local players and editors can't open AV1).
- **Clip versions:** a trim after the first encode produces a new video version, so the run tables need to record
  which version a run used and supersede older ones.
- **Migration from Bunny:** Bunny may hold only transcodes, so migration probably re-encodes Bunny's best rendition
  into AV1 through the same queue. The per-clip host column lets both hosts run side by side during the move.
- **Access control:** stay with unguessable unsigned URLs as today, or add signed URLs or a Worker check. Either
  must keep share links and the Discord Activity working.
- **Worker → API reporting:** settled as authenticated calls to the API (#211): workers claim and report through
  `/api/worker/` with a shared secret; see [workers-and-jobs.md](workers-and-jobs.md#the-queue-and-the-worker-api-built-in-211).

## Follow-ups from #188 (not done)

- Drop `clip.video_status`, `HostedVideoInfo.HostStatus` and the double write once the V38 deploy has settled.
- Clips that reach `ready` through the status refresh (a missed webhook) don't queue legend detection or
  transcription. This was already true before #188, and matters more once our own worker reports status.
- `Clip.video_id` and `PlaylistPreviewClip.video_id` duplicate `video.id`.
- The `clip_collection_merge` table and the `thumbnail_file_name` column are unused.
