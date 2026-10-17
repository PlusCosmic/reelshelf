import { describe, expect, it } from "vitest";
import {
  activityVideoPlaylistUrl,
  activityVideoThumbnailUrl,
} from "./clipMediaUrls";

const videoId = "11111111-2222-3333-4444-555555555555";

describe("clipMediaUrls", () => {
  it("keeps the Activity on its Discord URL Mapping", () => {
    expect(activityVideoPlaylistUrl(videoId)).toBe(
      `/bunny-cdn/${videoId}/playlist.m3u8`,
    );
    expect(activityVideoThumbnailUrl(videoId)).toBe(
      `/bunny-cdn/${videoId}/thumbnail.jpg`,
    );
  });
});
