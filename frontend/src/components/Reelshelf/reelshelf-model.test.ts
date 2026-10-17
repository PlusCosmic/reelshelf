import { describe, expect, it } from "vitest";
import type { Clip } from "@/api-client";
import { thumbnailUrl } from "./reelshelf-model";

function clip(video: Partial<Clip["video"]>): Clip {
  return {
    video: {
      thumbnailUrl: "https://cdn.example/video/thumbnail.jpg",
      ...video,
    },
  } as Clip;
}

describe("clip media URLs", () => {
  it("uses the thumbnail the API built", () => {
    expect(thumbnailUrl(clip({}))).toBe(
      "https://cdn.example/video/thumbnail.jpg",
    );
  });
});
