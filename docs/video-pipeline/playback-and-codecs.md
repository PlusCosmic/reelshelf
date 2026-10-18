# Playback and codecs

## Direction (2026-10-18)

- **Progressive MP4, not HLS.** Clips are short, so automatic quality switching buys little, and static files
  on object storage are much simpler to produce and serve than a segment ladder.
- **AV1 only** (SVT-AV1, 1080p, see [storage-transcoding-queue.md](storage-transcoding-queue.md)), to save
  storage. Everyone using Reelshelf today has a device that decodes AV1, so there is no fallback yet; the design
  leaves room to add one (below) without a contract change.

## How the app depends on HLS today

Only the players hard-depend on it. `attachHlsStream` (`frontend/src/shared/utils/hlsStream.ts`) hands the URL
to hls.js wherever MediaSource exists, and hls.js fails on an MP4. `ClipMediaUrls.Playlist` and the Activity's
`activityStreamUrl` hardcode `playlist.m3u8`. Everything else takes a URL that ffmpeg can read: transcription
audio and downloads work unchanged against an MP4 URL.

## What switching needs

1. **Contract:** replace `playback_url` with a `sources` list whose entries carry a full MIME type with codecs,
   e.g. `{ url, type: 'video/mp4; codecs="av01.0.08M.08"', height }`. One AV1 entry for now.
2. **Player:** generalise `attachHlsStream` to `attachStream`, which sets `video.src` for MP4 and keeps hls.js
   only for Bunny's HLS while both hosts exist. Before attaching, check `MediaCapabilities.decodingInfo()` (or
   `canPlayType`); if AV1 can't play, show "This clip can't play on this device" instead of a black box. That
   message is where a fallback flow would plug in.
3. **Encoding:** AV1 in MP4 with `-movflags +faststart` and sensible keyframe spacing for seeking. Opus or AAC
   audio (see the downloads point below).
4. **Serving:** HTTP Range (206) support, which Safari requires for `<video>`. CORS is not needed for a plain
   `<video src>`.
5. **Downloads:** the download endpoint (`api/FFmpeg/`) remuxes HLS to MP4 today. Decide whether it serves the
   AV1 file as-is or converts to H.264 on demand (some local players and editors can't open AV1). If it remuxes
   at all, drop the forced `-bsf:a aac_adtstoasc`, which fails on non-AAC audio.
6. **Activity:** a URL Mapping to the new host in place of `/bunny-cdn`. Its drift correction (playbackRate and
   seeks) works the same on progressive.

## Browser support for AV1 (from memory; check before relying on it)

Chrome, Edge, Firefox and Android Chrome decode AV1 (older phones in software, which costs battery). Safari
decodes it only on hardware with AV1 decode, roughly iPhone 15 Pro and M3 Macs onward. The Discord desktop client
is Chromium-based; mobile webviews vary.

## Future: on-demand H.264 fallback

Not needed now. If it is:

- Don't live-pipe an encode into the response. A plain MP4 can't be streamed while encoding, a fragmented one has
  no length or byte ranges, and Safari (the likeliest fallback user) won't play media without 206 responses.
- Instead, **transcode on first request and cache.** The player detects that AV1 won't play and calls something
  like `GET /api/clips/{id}/fallback`. That returns the cached H.264 URL, or `202` while a high-priority worker job
  encodes one 720p H.264/AAC `+faststart` file (seconds for a short clip, faster with a hardware encoder; not
  benchmarked). Evict fallbacks that go unused for N days, rate-limit the endpoint (share links are anonymous),
  and run one job per clip.
- The same job could serve downloads.

## Considered

- **HLS ladder (status quo on Bunny):** adaptive quality, but a segmenter, playlists and many files per clip.
- **AV1 + HEVC, or AV1 + H.264 for every clip:** full coverage, but more encode time and storage, which is what
  AV1 was meant to save.
- **Live HLS for the fallback (as Jellyfin does):** plays on iOS while encoding, but brings HLS back for one path
  plus per-viewer encoder processes.
