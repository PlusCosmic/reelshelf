import { describe, expect, it } from "vitest";
import {
  activityVideoCdnBaseUrl,
  videoEmbedUrl,
  videoPlaylistUrl,
  videoThumbnailUrl,
} from "./clipMediaUrls";

const videoId = "11111111-2222-3333-4444-555555555555";

describe("clipMediaUrls", () => {
  it("builds CDN URLs on the video host", () => {
    expect(videoThumbnailUrl(videoId)).toBe(
      `https://vz-cd8f9809-39a.b-cdn.net/${videoId}/thumbnail.jpg`,
    );
    expect(videoPlaylistUrl(videoId)).toBe(
      `https://vz-cd8f9809-39a.b-cdn.net/${videoId}/playlist.m3u8`,
    );
  });

  it("keeps the Activity on its Discord URL Mapping", () => {
    expect(videoPlaylistUrl(videoId, activityVideoCdnBaseUrl)).toBe(
      `/bunny-cdn/${videoId}/playlist.m3u8`,
    );
    expect(videoThumbnailUrl(videoId, activityVideoCdnBaseUrl)).toBe(
      `/bunny-cdn/${videoId}/thumbnail.jpg`,
    );
  });

  it("builds the embed player URL", () => {
    expect(videoEmbedUrl(12345, videoId, true)).toBe(
      `https://player.mediadelivery.net/embed/12345/${videoId}?autoplay=true`,
    );
  });
});
