import { describe, expect, it } from "vitest";
import type { GameCategoryResponse } from "@/api-client";
import type { TwitchClipSummary } from "@/shared/services/twitch";
import {
  buildTwitchImportRows,
  groupImportedTwitchRowsIntoSessions,
  importableTwitchRows,
  setAllTwitchRowsSelected,
  setTwitchRowCategory,
  toggleTwitchRowSelection,
  twitchGamesToAdd,
} from "./twitchClipsImport";

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
];

function makeClip(
  overrides: Partial<TwitchClipSummary> = {},
): TwitchClipSummary {
  return {
    id: "clip-1",
    title: "Big clutch",
    url: "https://clips.twitch.tv/clip-1",
    thumbnailUrl: null,
    durationSeconds: 30,
    viewCount: 12,
    createdAt: new Date(2026, 8, 5, 23, 30),
    creatorName: "harry",
    gameName: "Apex Legends",
    igdbId: 114795,
    suggestedCategoryId: "apex-id",
    alreadyImported: false,
    ...overrides,
  };
}

describe("twitch clip import rows", () => {
  it("pre-selects clips whose game is already in the library", () => {
    const rows = buildTwitchImportRows([makeClip()], [], categories);

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      categoryId: "apex-id",
      selected: true,
      status: "ready",
      sessionDate: "2026-09-05",
      title: "Big clutch",
    });
  });

  it("puts a clip captured after midnight in the previous gaming day", () => {
    const clip = makeClip({ createdAt: new Date(2026, 8, 6, 2, 15) });
    const [row] = buildTwitchImportRows([clip], [], categories);

    expect(row.sessionDate).toBe("2026-09-05");
  });

  it("leaves clips without a known game unselected and offers to add the game", () => {
    const unknownGame = makeClip({
      id: "clip-2",
      suggestedCategoryId: null,
      gameName: "Valorant",
      igdbId: 126459,
    });
    const rows = buildTwitchImportRows([unknownGame], [], categories);

    expect(rows[0]).toMatchObject({
      categoryId: null,
      selected: false,
      status: "needs_game",
    });
    expect(twitchGamesToAdd(rows)).toEqual([
      { igdbId: 126459, name: "Valorant", count: 1 },
    ]);
    // Nothing selectable-but-unassigned is importable.
    expect(importableTwitchRows(setAllTwitchRowsSelected(rows, true))).toEqual(
      [],
    );
  });

  it("marks clips the library already holds and keeps them out of the selection", () => {
    const rows = buildTwitchImportRows(
      [makeClip({ alreadyImported: true })],
      [],
      categories,
    );

    expect(rows[0]).toMatchObject({
      selected: false,
      status: "already_imported",
    });
    expect(toggleTwitchRowSelection(rows, "clip-1")[0].selected).toBe(false);
  });

  it("keeps review state for clips that were listed before when a refetch arrives", () => {
    const first = buildTwitchImportRows([makeClip()], [], categories);
    const edited = setTwitchRowCategory(
      toggleTwitchRowSelection(first, "clip-1"),
      "clip-1",
      null,
    );
    const importing = edited.map((row) => ({
      ...row,
      status: "importing" as const,
    }));

    const merged = buildTwitchImportRows(
      [makeClip({ viewCount: 99 }), makeClip({ id: "clip-2" })],
      importing,
      categories,
    );

    expect(merged.map((row) => row.clip.id)).toEqual(["clip-1", "clip-2"]);
    expect(merged[0]).toMatchObject({
      categoryId: null,
      selected: false,
      status: "importing",
    });
    expect(merged[0].clip.viewCount).toBe(99);
  });

  it("keeps a row whose clip fell out of the listing so an import in flight is not lost", () => {
    const first = buildTwitchImportRows([makeClip()], [], categories);
    const merged = buildTwitchImportRows(
      [makeClip({ id: "clip-2" })],
      first,
      categories,
    );

    expect(merged.map((row) => row.clip.id)).toEqual(["clip-2", "clip-1"]);
  });

  it("groups imported clips into one gaming session per game and day", () => {
    const rows = buildTwitchImportRows(
      [
        makeClip({ id: "a", createdAt: new Date(2026, 8, 5, 22, 0) }),
        makeClip({ id: "b", createdAt: new Date(2026, 8, 6, 1, 0) }),
        makeClip({ id: "c", createdAt: new Date(2026, 8, 6, 20, 0) }),
        makeClip({ id: "d" }),
      ],
      [],
      categories,
    ).map((row, index) => ({
      ...row,
      importedClipId: index < 3 ? `library-${row.clip.id}` : null,
      status: index < 3 ? ("imported" as const) : ("ready" as const),
    }));

    expect(groupImportedTwitchRowsIntoSessions(rows)).toEqual([
      {
        key: "apex-id:2026-09-05",
        categoryId: "apex-id",
        sessionDate: "2026-09-05",
        clipIds: ["library-a", "library-b"],
        rowIds: ["a", "b"],
      },
      {
        key: "apex-id:2026-09-06",
        categoryId: "apex-id",
        sessionDate: "2026-09-06",
        clipIds: ["library-c"],
        rowIds: ["c"],
      },
    ]);
  });
});
