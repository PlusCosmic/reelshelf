import { describe, expect, it } from "vitest";
import {
  activityStreamSource,
  activityThumbnailUrl,
  type ActivityMediaRef,
} from "./clipMediaUrls";

const videoId = "11111111-2222-3333-4444-555555555555";

const bunny: ActivityMediaRef = {
  host: "bunny",
  path: videoId,
  type: "application/vnd.apple.mpegurl",
};

const r2: ActivityMediaRef = {
  host: "r2",
  path: `clips/${videoId}/v2`,
  type: 'video/mp4; codecs="av01.0.08M.08,opus"',
};

describe("clipMediaUrls", () => {
  it("keeps a Bunny clip on the /bunny-cdn URL Mapping", () => {
    expect(activityStreamSource(bunny)).toEqual({
      url: `/bunny-cdn/${videoId}/playlist.m3u8`,
      type: "application/vnd.apple.mpegurl",
    });
    expect(activityThumbnailUrl(bunny)).toBe(
      `/bunny-cdn/${videoId}/thumbnail.jpg`,
    );
  });

  it("puts an R2 clip on the /media URL Mapping, under its version", () => {
    expect(activityStreamSource(r2)).toEqual({
      url: `/media/clips/${videoId}/v2/av1.mp4`,
      type: 'video/mp4; codecs="av01.0.08M.08,opus"',
    });
    expect(activityThumbnailUrl(r2)).toBe(
      `/media/clips/${videoId}/v2/thumbnail.jpg`,
    );
  });

  it("has nothing for a host without a mapping", () => {
    const elsewhere = { ...bunny, host: "elsewhere" };
    expect(activityStreamSource(elsewhere)).toBeNull();
    expect(activityThumbnailUrl(elsewhere)).toBeNull();
  });
});
