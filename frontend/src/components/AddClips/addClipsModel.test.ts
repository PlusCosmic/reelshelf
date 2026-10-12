import { describe, expect, it } from "vitest";
import type { TwitchClipSummary } from "@/api-client";
import { clipStage, sortTwitchClips, type UploadItem } from "./addClipsModel";

function twitchClip(
  id: string,
  overrides: Partial<TwitchClipSummary> = {},
): TwitchClipSummary {
  return {
    id,
    title: id,
    url: `https://clips.twitch.tv/${id}`,
    thumbnailUrl: null,
    durationSeconds: 30,
    viewCount: 0,
    createdAt: new Date("2026-10-01T20:00:00Z"),
    creatorName: null,
    gameName: "Apex Legends",
    igdbId: 114795,
    suggestedCategoryId: null,
    alreadyImported: false,
    ...overrides,
  };
}

function upload(overrides: Partial<UploadItem>): UploadItem {
  return {
    id: "1",
    file: new File(["x"], "clip.mp4", { type: "video/mp4" }),
    title: "clip",
    categoryId: "apex",
    createdAt: new Date(),
    sessionDate: "2026-10-10",
    status: "uploading",
    progress: 40,
    clipId: "clip-1",
    error: null,
    ...overrides,
  };
}

describe("sortTwitchClips", () => {
  it("puts clips in the book their game already has", () => {
    const { byCategory, ghosts } = sortTwitchClips([
      twitchClip("a", { suggestedCategoryId: "apex" }),
      twitchClip("b", { suggestedCategoryId: "apex" }),
    ]);
    expect(byCategory.get("apex")?.map((clip) => clip.id)).toEqual(["a", "b"]);
    expect(ghosts).toEqual([]);
  });

  it("offers games without a book as ghosts, most clips first", () => {
    const { ghosts } = sortTwitchClips([
      twitchClip("a", { igdbId: 1, gameName: "Valorant" }),
      twitchClip("b", { igdbId: 2, gameName: "Marvel Rivals" }),
      twitchClip("c", { igdbId: 2, gameName: "Marvel Rivals" }),
    ]);
    expect(ghosts.map((ghost) => [ghost.gameName, ghost.clips.length])).toEqual(
      [
        ["Marvel Rivals", 2],
        ["Valorant", 1],
      ],
    );
  });

  it("leaves out imported clips, games with no IGDB entry and dismissed games", () => {
    const { byCategory, ghosts } = sortTwitchClips(
      [
        twitchClip("imported", {
          suggestedCategoryId: "apex",
          alreadyImported: true,
        }),
        twitchClip("chatting", { igdbId: null, gameName: "Just Chatting" }),
        twitchClip("dismissed", { igdbId: 7 }),
      ],
      new Set([7]),
    );
    expect(byCategory.size).toBe(0);
    expect(ghosts).toEqual([]);
  });
});

describe("clipStage", () => {
  it("shows an upload still running in this tab over Bunny's status", () => {
    expect(clipStage(0, 0, upload({ progress: 40 }))).toEqual({
      kind: "uploading",
      progress: 40,
      paused: false,
    });
  });

  it("follows Bunny once the upload is done", () => {
    const done = upload({ status: "uploaded", progress: 100 });
    expect(clipStage(2, 35, done)).toEqual({
      kind: "processing",
      progress: 35,
    });
    expect(clipStage(1, 0, done)).toEqual({
      kind: "processing",
      progress: null,
    });
    expect(clipStage(4, 100, done)).toEqual({ kind: "watchable" });
    expect(clipStage(3, 100, done)).toEqual({ kind: "ready" });
    expect(clipStage(5, 0, done)).toEqual({ kind: "failed" });
  });
});
