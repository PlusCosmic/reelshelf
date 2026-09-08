import type { GameCategoryResponse } from "@/api-client";
import type { TwitchClipSummary } from "@/shared/services/twitch";
import { gamingSessionDate } from "./bulkUploadQueue";

export type TwitchImportStatus =
  | "already_imported"
  | "needs_game"
  | "ready"
  | "importing"
  | "imported"
  | "filing"
  | "saved"
  | "duplicate"
  | "error";

export type TwitchImportRow = {
  clip: TwitchClipSummary;
  title: string;
  categoryId: string | null;
  /** Gaming day the clip falls in, in the viewer's timezone; drives the auto-generated session collection. */
  sessionDate: string;
  selected: boolean;
  status: TwitchImportStatus;
  error: string | null;
  importedClipId: string | null;
  sessionPlaylistId: string | null;
};

export type TwitchImportSession = {
  key: string;
  categoryId: string;
  sessionDate: string;
  clipIds: string[];
  rowIds: string[];
};

/**
 * Turns the clips Twitch returned into review rows, keeping whatever the user already changed on rows
 * that were listed before (selection, game, title, import progress). Rows for clips that dropped out of
 * the listing are kept too, so an import in flight is never lost to a refetch.
 */
export function buildTwitchImportRows(
  clips: TwitchClipSummary[],
  existing: TwitchImportRow[],
  categories: GameCategoryResponse[],
): TwitchImportRow[] {
  const existingById = new Map(existing.map((row) => [row.clip.id, row]));
  const categoryIds = new Set(categories.map((category) => category.id));
  const seen = new Set<string>();
  const rows: TwitchImportRow[] = [];

  for (const clip of clips) {
    seen.add(clip.id);
    const previous = existingById.get(clip.id);
    if (previous) {
      rows.push({ ...previous, clip });
      continue;
    }

    const categoryId =
      clip.suggestedCategoryId && categoryIds.has(clip.suggestedCategoryId)
        ? clip.suggestedCategoryId
        : null;
    rows.push({
      clip,
      title: clip.title,
      categoryId,
      sessionDate: gamingSessionDate(new Date(clip.createdAt)),
      selected: !clip.alreadyImported && categoryId !== null,
      status: clip.alreadyImported
        ? "already_imported"
        : categoryId
          ? "ready"
          : "needs_game",
      error: null,
      importedClipId: null,
      sessionPlaylistId: null,
    });
  }

  for (const row of existing) {
    if (!seen.has(row.clip.id)) rows.push(row);
  }

  return rows;
}

export function setTwitchRowCategory(
  rows: TwitchImportRow[],
  clipId: string,
  categoryId: string | null,
): TwitchImportRow[] {
  return rows.map((row) => {
    if (row.clip.id !== clipId) return row;
    const editable = row.status === "ready" || row.status === "needs_game";
    return {
      ...row,
      categoryId,
      status: editable ? (categoryId ? "ready" : "needs_game") : row.status,
      selected: editable && categoryId ? row.selected : row.selected,
    };
  });
}

export function setTwitchRowTitle(
  rows: TwitchImportRow[],
  clipId: string,
  title: string,
): TwitchImportRow[] {
  return rows.map((row) => (row.clip.id === clipId ? { ...row, title } : row));
}

export function toggleTwitchRowSelection(
  rows: TwitchImportRow[],
  clipId: string,
): TwitchImportRow[] {
  return rows.map((row) =>
    row.clip.id === clipId && isSelectable(row)
      ? { ...row, selected: !row.selected }
      : row,
  );
}

export function setAllTwitchRowsSelected(
  rows: TwitchImportRow[],
  selected: boolean,
): TwitchImportRow[] {
  return rows.map((row) => (isSelectable(row) ? { ...row, selected } : row));
}

/** Rows the "Add to library" action will import: selected, not yet imported, with a game and a title. */
export function importableTwitchRows(rows: TwitchImportRow[]) {
  return rows.filter(
    (row) =>
      row.selected &&
      row.status === "ready" &&
      row.categoryId !== null &&
      row.title.trim().length > 0,
  );
}

export function isSelectable(row: TwitchImportRow) {
  return row.status === "ready" || row.status === "needs_game";
}

/**
 * Imported clips grouped the way the bulk uploader files them: one gaming session per game and gaming day,
 * so a batch of Twitch clips lands in the same auto-generated collections a local upload would.
 */
export function groupImportedTwitchRowsIntoSessions(
  rows: TwitchImportRow[],
): TwitchImportSession[] {
  const sessions = new Map<string, TwitchImportSession>();

  for (const row of rows) {
    if (row.status !== "imported" || !row.importedClipId || !row.categoryId) {
      continue;
    }

    const key = `${row.categoryId}:${row.sessionDate}`;
    const existing = sessions.get(key);
    if (existing) {
      existing.clipIds.push(row.importedClipId);
      existing.rowIds.push(row.clip.id);
    } else {
      sessions.set(key, {
        key,
        categoryId: row.categoryId,
        sessionDate: row.sessionDate,
        clipIds: [row.importedClipId],
        rowIds: [row.clip.id],
      });
    }
  }

  return [...sessions.values()];
}

/** Rows the import can still assign a game to with one click: Twitch knows the game but the library lacks it. */
export function twitchGamesToAdd(rows: TwitchImportRow[]) {
  const games = new Map<
    number,
    { igdbId: number; name: string; count: number }
  >();
  for (const row of rows) {
    if (row.status !== "needs_game" || row.clip.igdbId === null) continue;
    const igdbId = row.clip.igdbId;
    const existing = games.get(igdbId);
    if (existing) existing.count += 1;
    else
      games.set(igdbId, {
        igdbId,
        name: row.clip.gameName ?? "this game",
        count: 1,
      });
  }
  return [...games.values()];
}
