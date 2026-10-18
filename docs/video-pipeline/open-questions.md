# Open questions and follow-ups

## From the storage and transcoding discussion

1. **Encode benchmark:** time SVT-AV1 on real clips, including a 4K60 HDR one and a fast-paced shooter. The
   calculator assumes 10 vCPU-minutes per output minute, and that figure drives every compute cost.
2. **Bitrate tuning:** test quality with VMAF against the assumed 4 Mbps AV1, adjusting bitrate per clip by
   content.
3. **Real usage:** replace the calculator's assumptions with measured views per clip, retention and cache hit
   ratio, and watch for the 3 TB cold-storage trigger.

## Undecided

- **Fallback:** the storage discussion lists an H.264 720p fallback, pre-generated or made on first request.
  Current call (2026-10-18): none for now, since everyone using Reelshelf can decode AV1. Build the seams (a
  `sources` list, a "can't play on this device" message, room in the queue for a fallback job) and add an
  on-demand fallback when needed. See [playback-and-codecs.md](playback-and-codecs.md).
- **Frame rate:** the encoding notes cap output at 30 fps. Many game clips are recorded at 60; confirm 30 is
  wanted, or allow 60 for sources that have it.
- **Originals:** keep the uploaded file or only the AV1 output? It decides re-encode quality later (a better
  encoder, a fallback, new bitrates), what counts toward the storage quota, and the storage bill.
- **Downloads:** serve the AV1 file as-is, or convert to H.264 on demand (some local players and editors can't
  open AV1)?
- **Quota:** what `storage_size` means once we own storage (AV1 output only, or plus the original or previews).
- **Migration from Bunny:** Bunny may hold only transcodes, so migration probably re-encodes Bunny's best rendition
  into AV1 through the same queue. The per-clip host column lets both hosts run side by side during the move.
- **Access control:** stay with unguessable unsigned URLs as today, or add signed URLs or a Worker check. Either
  must keep share links and the Discord Activity working.
- **Worker → API reporting:** an authenticated callback, or a results queue the API consumes.

## Follow-ups from #188 (not done)

- Drop `clip.video_status`, `HostedVideoInfo.HostStatus` and the double write once the V38 deploy has settled.
- Clips that reach `ready` through the status refresh (a missed webhook) don't queue legend detection or
  transcription. This was already true before #188, and matters more once our own worker reports status.
- `Clip.video_id` and `PlaylistPreviewClip.video_id` duplicate `video.id`.
- The `clip_collection_merge` table and the `thumbnail_file_name` column are unused.
