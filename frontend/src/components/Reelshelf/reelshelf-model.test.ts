import { describe, expect, it } from "vitest";
import type { Clip } from "@/api-client";
import { playerUrl, thumbnailUrl } from "./reelshelf-model";

function clip(video: Partial<Clip["video"]>): Clip {
  return {
    video: {
      thumbnailUrl: "https://cdn.example/video/thumbnail.jpg",
      embedUrl: "https://player.example/embed/1/video?autoplay=false",
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

  it("switches autoplay on the API's embed URL", () => {
    expect(playerUrl(clip({}))).toBe(
      "https://player.example/embed/1/video?autoplay=false",
    );
    expect(playerUrl(clip({}), true)).toBe(
      "https://player.example/embed/1/video?autoplay=true",
    );
    expect(
      playerUrl(
        clip({ embedUrl: "https://player.example/embed/1/video" }),
        true,
      ),
    ).toBe("https://player.example/embed/1/video?autoplay=true");
  });
});
