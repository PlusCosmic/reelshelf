import { describe, expect, it } from "vitest";
import {
  activityStreamSource,
  activityThumbnailUrl,
  type ActivityMediaRef,
} from "./clipMediaUrls";

const videoId = "11111111-2222-3333-4444-555555555555";

const r2: ActivityMediaRef = {
  host: "r2",
  path: `clips/${videoId}/v2`,
  type: 'video/mp4; codecs="av01.0.08M.08,opus"',
  hasThumbnail: true,
};

describe("clipMediaUrls", () => {
  it("puts an R2 clip on the /media URL Mapping, under its version", () => {
    expect(activityStreamSource(r2)).toEqual({
      url: `/media/clips/${videoId}/v2/av1.mp4`,
      type: 'video/mp4; codecs="av01.0.08M.08,opus"',
    });
    expect(activityThumbnailUrl(r2)).toBe(
      `/media/clips/${videoId}/v2/thumbnail.jpg`,
    );
  });

  it("has no stream or thumbnail for a clip that hasn't got them yet", () => {
    const processing: ActivityMediaRef = {
      ...r2,
      type: null,
      hasThumbnail: false,
    };
    expect(activityStreamSource(processing)).toBeNull();
    expect(activityThumbnailUrl(processing)).toBeNull();
    expect(activityThumbnailUrl({ ...processing, hasThumbnail: true })).toBe(
      `/media/clips/${videoId}/v2/thumbnail.jpg`,
    );
  });

  it("has nothing for a host without a mapping", () => {
    const elsewhere = { ...r2, host: "bunny" };
    expect(activityStreamSource(elsewhere)).toBeNull();
    expect(activityThumbnailUrl(elsewhere)).toBeNull();
  });
});
