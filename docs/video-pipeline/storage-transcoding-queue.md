# Storage, transcoding and queueing

From the design discussion on object storage, worker pools and queueing (October 2026). Prices are ballpark list
prices at the time; verify before committing. Cost calculator: https://claude.ai/artifact/FtZTAAyWqpHDWLGiwb1bs6

## Summary

Move off Bunny Stream to our own pipeline: **Cloudflare R2 for storage and delivery**, encoding on **the
dedicated server**, and **AWS Fargate Spot** taking only the overflow. At the calculator's assumptions (3,000
clips/month, 50 views per clip, 12-month retention) this is about **$20/month**, against about $65 for Bunny
Stream with H.264 and about $195 for Bunny with AV1. Storage and delivery are built so they can move to **AWS (S3

- CloudFront Pro)** once the library grows past roughly 3 TB of cold clips.

## Decisions

| Area            | Decision                                                                      | Notes                                                                                                                       |
| --------------- | ----------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Storage         | Cloudflare R2, lifecycle rule moving clips to Infrequent Access after ~7 days | $0.015 → $0.01/GB-month; most views happen in the first few days                                                            |
| Delivery        | Cloudflare CDN on a custom domain                                             | Free egress; Cloudflare's terms allow video stored in R2. Never serve from `r2.dev` (uncached, rate-limited)                |
| Encode baseline | The existing dedicated server                                                 | Effectively free; capacity far exceeds current volume; traffic is well under the host's 3 TB/24h throttle threshold         |
| Encode overflow | AWS Fargate Spot                                                              | Per-second billing, scale to zero; most egress covered by the 100 GB/month free allowance                                   |
| Queue           | AWS SQS                                                                       | Free at this volume; reachable from both the server and AWS                                                                 |
| Output codec    | AV1 (SVT-AV1), 1080p30                                                        | Royalty-free, good quality per bit, fast on CPU                                                                             |
| Fallback        | H.264 720p for clients without AV1 decode                                     | Not built yet; see [playback-and-codecs.md](playback-and-codecs.md#future-on-demand-h264-fallback) and the open items below |
| Packaging       | Progressive MP4 with `faststart`                                              | Simplest for clips of a couple of minutes; HLS with fMP4 segments later if adaptive bitrate is needed (AV1 works in it)     |

## Pipeline

1. **Upload:** the client uploads straight to R2 with presigned multipart URLs.
2. **Validate:** ffprobe checks duration, resolution, HDR and variable frame rate, and rejects bad or abusive
   inputs before anything is queued.
3. **Enqueue:** a transcode job goes to SQS.
4. **Encode:** workers pull jobs. The dedicated server always runs one; Fargate Spot tasks start only when the
   oldest message is more than ~2–3 minutes old (`ApproximateAgeOfOldestMessage`), not on queue depth.
5. **Write back:** the AV1 1080p MP4, a thumbnail and a short animated preview go to R2 (plus the detection frames
   and, if pre-generated, the H.264 fallback; see below).
6. **Serve:** Cloudflare CDN on a custom domain, with a small Worker (or signed URLs) for access control and for
   choosing between AV1 and H.264.

### Encoding notes

- Force constant frame rate with `fps=30` and `-fps_mode cfr`; game recorders often produce variable frame rate.
- Tone-map HDR sources (zscale or libplacebo), or reject them.
- Scale with `force_original_aspect_ratio=decrease`, keep dimensions even, and never upscale sources below 1080p.
- Jobs must be idempotent. Set the SQS visibility timeout above the worst-case encode time, and add a dead-letter
  queue.

### Running on the dedicated server

- **Protect the game servers:** pin the worker to specific cores (`--cpuset-cpus`), give it a low CPU weight or run
  ffmpeg with `chrt --idle 0`, cap SVT-AV1 threads with `--lp`, and coordinate with FileFlows.
- **Sandbox untrusted input:** no host network, read-only root filesystem, an unprivileged user, ideally gVisor. A
  scoped R2 token for one bucket and an IAM user limited to receiving and deleting SQS messages.

## How it fits the app

How each step maps onto today's code (see [current-state.md](current-state.md)). These are the parts the
discussion didn't cover.

- **A second `IClipVideoHost`** (e.g. `api/Video/R2/`), next to `BunnyService`. Record each clip's host in the
  database (a per-clip host column), which the discussion also asks for, so both hosts run side by side during
  migration and a later move to S3 is a copy job.
- **Uploads:** presigned multipart is a new `UploadTarget.protocol` (e.g. `s3-multipart`). The API creates the
  multipart upload and presigns part URLs, the browser uploads parts (tus-js-client doesn't do this, so a small
  uploader or Uppy's S3 multipart plugin), and the API completes the upload, then validates and enqueues. Resuming
  after a reload needs the upload id and finished parts kept client-side. An R2 lifecycle rule should abort
  incomplete multipart uploads, alongside the abandoned-upload purge (`ClipStatusRefreshService`).
- **Twitch import** uses `IClipVideoHost.UploadAsync` from the server: stream into R2 with the same multipart API.
- **Validation** can run in the API right after the upload completes (the prod image has ffprobe) or as the
  first step of the job. Rejections become `upload_failed` or `encode_failed`; the purge depends on telling them
  apart.
- **Status:** the worker reports progress and completion, and the API applies it through
  `ClipVideoUpdateHandler` (`processing` with `encode_progress`, then `ready` or `encode_failed`). The worker
  shouldn't write to Postgres directly; an authenticated callback or a results queue keeps the database in the
  API's hands.
- **Outputs the app needs** beyond the discussion's list:
  - the five extra **detection frames** `thumbnail_1..5.jpg` spread through the clip, at timings close to Bunny's,
    or legend detection re-scored on the labelled clips ("Run on labelled clips" on `/legend-detection`);
  - the output's length and size, for durations and `clip.storage_size` (the quota).
- **Media URLs:** `ClipMediaUrls` builds them from the custom domain. The animated preview has no consumer yet.
- **Access control:** media is unsigned today (anyone with a video id can fetch it). If signed URLs or a Worker
  check are added, they must still work for anonymous share-page viewers and through the Discord Activity's URL
  Mapping (query-string signatures survive the mapping; cookies don't).
- **Discord Activity:** add a URL Mapping for the custom domain in place of `/bunny-cdn`.
- **Privacy policy:** Cloudflare (storage, delivery) and AWS (overflow encoding) replace Bunny as processors;
  update `frontend/src/routes/privacy.tsx` in the same PR as the switch.

## Cost picture (calculator defaults)

| Setup                                                               | ~$/month |
| ------------------------------------------------------------------- | -------- |
| Bunny Stream, H.264 (free encoding)                                 | 65       |
| Bunny Stream, AV1 premium encoding                                  | 195      |
| **DIY: R2 + Infrequent Access, own server + Fargate Spot overflow** | **20**   |
| DIY on AWS: S3 Glacier Instant Retrieval + CloudFront Pro           | 27       |

What's left in the DIY bill is almost all R2 storage. The biggest remaining savings are lower bitrates (tested with
VMAF), not storing a fallback for every clip, and shorter retention.

## When to move to AWS

**Trigger:** roughly **3 TB of cold clips**, about twice the default library. Above that the AWS stack is cheaper,
and the gap keeps widening.

**Target stack:**

- S3 Standard for a clip's first week (covered by Pro's 50 GB S3 Standard credit), then **Glacier Instant
  Retrieval** or **Intelligent-Tiering**.
- **CloudFront Pro flat-rate plan** at $15/month, including 50 TB of transfer and 10M requests. Usage over the
  allowance isn't billed; sustained overuse slows delivery instead. Signed URLs and CloudFront Functions are
  supported. Pro only offers the default caching rules; CachingOptimized is suitable.
- Overflow workers write to S3 in the same region, so there's no egress cost.
- Accounts on the AWS Free Tier aren't eligible for flat-rate plans, and the Free plan's 100 GB of transfer is far
  too little.

**Keep the switch cheap now:** S3-compatible APIs only, the storage provider behind an interface, and each clip's
storage location in the database. Migration is then a copy job plus a change of delivery domain.

## Considered and ruled out

| Option                        | Why not                                                                                                     |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Cloudflare Containers         | About $0.10/vCPU-hour once the required memory is included, roughly 5x AWS Spot; max 4 vCPU per instance    |
| Hetzner Cloud                 | 2026 price rises (CCX up to about 169%); hourly billing doesn't suit scale-to-zero; a vCPU is a hyperthread |
| Railway Buckets               | No infrequent-access tier, no public buckets or CDN                                                         |
| S3 One Zone-IA                | No cheaper than R2 Infrequent Access; all copies in one data centre                                         |
| Backblaze B2 + Cloudflare CDN | Cloudflare's terms don't allow serving video from non-Cloudflare storage                                    |
| Backblaze B2 + Bunny CDN      | Bunny's CDN fees make it more expensive than R2                                                             |
| GPU / NVENC encoding          | Worse quality per bit; CPU is fast enough for short clips                                                   |
| VP9 / HEVC                    | libvpx is very slow; HEVC has the weakest browser support and licensing baggage                             |

Still worth considering:

- **Backblaze B2 served directly, no CDN** (~$15/month): cheapest on paper, but single-region delivery and abuse
  can cost money. A fit if the audience is mainly UK and Europe, or as cold storage behind R2.
- **Oracle Cloud:** 10 TB/month of free egress and a free Arm instance tier; a possible extra worker pool.
