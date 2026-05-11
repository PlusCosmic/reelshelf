import { describe, expect, it } from "vitest";
import type { GameCategoryResponse } from "@/api-client";
import {
  applyTagsToSelectedRows,
  assignGame,
  buildBulkUploadQueue,
  deriveCreatedAt,
  gamingSessionDate,
  groupRowsIntoSessions,
  setAllRowsSelected,
  setSelectedRowsPlaylist,
  sourcePathForFile,
  toggleRowSelection,
} from "./bulkUploadQueue";

const categories: GameCategoryResponse[] = [
  {
    id: "apex-id",
    name: "Apex Legends",
    slug: "apex-legends",
    coverUrl: null,
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
  },
  {
    id: "valorant-id",
    name: "Valorant",
    slug: "valorant",
    coverUrl: null,
    keyArtUrl: null,
    gameLogoUrl: null,
    isCustom: false,
  },
];

describe("bulk upload queue foundations", () => {
  it("builds valid queue rows from files and rejects invalid files", () => {
    const video = makeFile("apex-legends_2026-05-10_2147.mp4", {
      type: "video/mp4",
      lastModified: new Date(2026, 4, 10, 21, 47).getTime(),
    });
    const text = makeFile("notes.txt", { type: "text/plain" });
    const large = makeFile("huge.mp4", {
      type: "video/mp4",
      size: 20,
    });

    const result = buildBulkUploadQueue([video, text, large], categories, {
      maxFileSize: 10,
    });

    expect(result.rows).toHaveLength(1);
    expect(result.rows[0]).toMatchObject({
      file: video,
      sourcePath: "apex-legends_2026-05-10_2147.mp4",
      title: "apex-legends_2026-05-10_2147",
      categoryId: "apex-id",
      gameAssignmentSource: "source_path",
      sessionDate: "2026-05-10",
      tags: [],
      playlistId: null,
      progress: 0,
      bytesUploaded: 0,
      md5Hash: null,
      uploadedClipId: null,
      uploadedVideoId: null,
      error: null,
      status: "ready",
      selected: true,
    });
    expect(result.rejected.map((item) => item.reason)).toEqual([
      "unsupported_type",
      "file_too_large",
    ]);
  });

  it("uses best available source path metadata", () => {
    const file = makeFile("clip.mp4", { type: "video/mp4" });
    Object.defineProperty(file, "webkitRelativePath", {
      value: "Apex Legends/session/clip.mp4",
    });

    expect(sourcePathForFile(file)).toBe("Apex Legends/session/clip.mp4");
    expect(sourcePathForFile(file, "dragged/Valorant/clip.mp4")).toBe(
      "dragged/Valorant/clip.mp4",
    );
  });

  it("uses source path game assignment before game-page fallback context", () => {
    expect(
      assignGame("captures/Apex Legends/clip.mp4", categories, {
        fallbackCategoryId: "valorant-id",
      }),
    ).toEqual({ categoryId: "apex-id", source: "source_path" });

    expect(
      assignGame("captures/unknown/clip.mp4", categories, {
        fallbackCategoryId: "valorant-id",
      }),
    ).toEqual({ categoryId: "valorant-id", source: "fallback_context" });
  });

  it("derives timestamps from metadata first and filename fallback second", () => {
    const metadataFile = makeFile("20260510_214700.mp4", {
      type: "video/mp4",
      lastModified: new Date(2026, 4, 11, 6, 0).getTime(),
    });
    const fallbackFile = makeFile("session_2026-05-10_2147.mp4", {
      type: "video/mp4",
      lastModified: 0,
    });

    expect(deriveCreatedAt(metadataFile).getHours()).toBe(6);
    expect(deriveCreatedAt(fallbackFile).getFullYear()).toBe(2026);
    expect(deriveCreatedAt(fallbackFile).getMonth()).toBe(4);
    expect(deriveCreatedAt(fallbackFile).getDate()).toBe(10);
    expect(deriveCreatedAt(fallbackFile).getHours()).toBe(21);
    expect(deriveCreatedAt(fallbackFile).getMinutes()).toBe(47);
  });

  it("groups rows by game assignment and 5am-to-5am browser-local session date", () => {
    const beforeMidnight = makeFile("apex-1.mp4", {
      type: "video/mp4",
      lastModified: new Date(2026, 4, 10, 23, 30).getTime(),
    });
    const afterMidnight = makeFile("apex-2.mp4", {
      type: "video/mp4",
      lastModified: new Date(2026, 4, 11, 2, 30).getTime(),
    });
    const nextMorning = makeFile("apex-3.mp4", {
      type: "video/mp4",
      lastModified: new Date(2026, 4, 11, 6, 0).getTime(),
    });

    const rows = buildBulkUploadQueue(
      [
        { file: beforeMidnight, sourcePath: "apex-legends/one.mp4" },
        { file: afterMidnight, sourcePath: "apex-legends/two.mp4" },
        { file: nextMorning, sourcePath: "apex-legends/three.mp4" },
      ],
      categories,
    ).rows;

    expect(gamingSessionDate(new Date(2026, 4, 11, 2, 30))).toBe("2026-05-10");
    expect(
      groupRowsIntoSessions(rows).map((session) => session.rows.length),
    ).toEqual([2, 1]);
  });

  it("supports selection and metadata operations without upload side effects", () => {
    const result = buildBulkUploadQueue(
      [
        {
          file: makeFile("apex-one.mp4", { type: "video/mp4" }),
          sourcePath: "apex-legends/one.mp4",
        },
        {
          file: makeFile("valorant-two.mp4", { type: "video/mp4" }),
          sourcePath: "valorant/two.mp4",
        },
      ],
      categories,
    );

    const firstDeselected = toggleRowSelection(result.rows, result.rows[0].id);
    const tagged = applyTagsToSelectedRows(firstDeselected, [
      "#Ranked",
      "clutch",
      "ranked",
    ]);
    const filed = setSelectedRowsPlaylist(tagged, "playlist-id");
    const allSelected = setAllRowsSelected(filed, true);

    expect(filed[0].selected).toBe(false);
    expect(filed[0].tags).toEqual([]);
    expect(filed[0].playlistId).toBeNull();
    expect(filed[1].tags).toEqual(["ranked", "clutch"]);
    expect(filed[1].playlistId).toBe("playlist-id");
    expect(allSelected.every((row) => row.selected)).toBe(true);
    expect(allSelected.every((row) => row.progress === 0)).toBe(true);
  });
});

function makeFile(
  name: string,
  options: { type?: string; size?: number; lastModified?: number } = {},
) {
  const content = "x".repeat(options.size ?? 1);
  return new File([content], name, {
    type: options.type,
    lastModified: options.lastModified ?? Date.now(),
  });
}
