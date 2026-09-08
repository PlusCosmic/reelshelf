import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBlocker } from "@tanstack/react-router";
import type { SelectOption } from "@/components/ui";
import {
  useAddGameFromIgdb,
  useCategories,
  useTwitchClips,
} from "@/hooks/queries";
import { storageUsageQueryKey } from "@/hooks/auth.queries";
import { invalidateClipCollections } from "@/hooks/clips.queries";
import { twitchClipsQueryKey } from "@/hooks/twitch.queries";
import {
  buildTwitchImportRows,
  groupImportedTwitchRowsIntoSessions,
  importableTwitchRows,
  isSelectable,
  setAllTwitchRowsSelected,
  setTwitchRowCategory,
  setTwitchRowTitle,
  toggleTwitchRowSelection,
  twitchGamesToAdd,
  type TwitchImportRow,
} from "@/hooks/twitchClipsImport";
import { ApiError } from "@/shared/services/api-error";
import { ensureGamingSessionPlaylist } from "@/shared/services/playlists";
import {
  importTwitchClip,
  type TwitchClipsWindow,
} from "@/shared/services/twitch";
import { uploadErrorMessage } from "@/utils/clipUpload";

/** Each import streams a whole clip through the API; two at a time keeps the page responsive. */
const MAX_ACTIVE_IMPORTS = 2;

export function useTwitchClipsImport() {
  const queryClient = useQueryClient();
  const [days, setDays] = useState<TwitchClipsWindow>(30);
  const [rows, setRows] = useState<TwitchImportRow[]>([]);
  const [addingGame, setAddingGame] = useState<number | null>(null);
  const [addGameError, setAddGameError] = useState<string | null>(null);
  const activeImportsRef = useRef(new Set<string>());
  const requestedImportsRef = useRef(new Set<string>());
  const filingSessionsRef = useRef(new Set<string>());

  const clipsQuery = useTwitchClips(days);
  const { data: categories = [], isLoading: categoriesLoading } =
    useCategories();
  const addGame = useAddGameFromIgdb();

  const firstPage = clipsQuery.data?.pages[0];
  const state = firstPage?.state ?? null;
  const twitchLogin = firstPage?.twitchLogin ?? null;

  const listedClips = useMemo(
    () => clipsQuery.data?.pages.flatMap((page) => page.clips) ?? [],
    [clipsQuery.data],
  );

  useEffect(() => {
    if (categoriesLoading) return;
    setRows((current) =>
      buildTwitchImportRows(listedClips, current, categories),
    );
  }, [categories, categoriesLoading, listedClips]);

  // Switching the time window shows a different set; forget review state that belonged to the old one.
  useEffect(() => {
    setRows([]);
    requestedImportsRef.current.clear();
  }, [days]);

  const categoryById = useMemo(
    () => new Map(categories.map((category) => [category.id, category])),
    [categories],
  );
  const categoryOptions = useMemo<SelectOption[]>(
    () => [
      { label: "Choose game", value: "" },
      ...categories.map((category) => ({
        label: category.name,
        value: category.id,
      })),
    ],
    [categories],
  );

  const selectableRows = rows.filter(isSelectable);
  const selectedRows = selectableRows.filter((row) => row.selected);
  const allSelected =
    selectableRows.length > 0 && selectedRows.length === selectableRows.length;
  const importableRows = importableTwitchRows(rows);
  const needsGameCount = rows.filter(
    (row) => row.status === "needs_game",
  ).length;
  const activeCount = rows.filter((row) =>
    ["importing", "imported", "filing"].includes(row.status),
  ).length;
  const savedCount = rows.filter((row) => row.status === "saved").length;
  const gamesToAdd = twitchGamesToAdd(rows);

  const updateRow = useCallback(
    (clipId: string, patch: Partial<TwitchImportRow>) => {
      setRows((current) =>
        current.map((row) =>
          row.clip.id === clipId ? { ...row, ...patch } : row,
        ),
      );
    },
    [],
  );

  const startImport = useCallback(
    (row: TwitchImportRow) => {
      if (!row.categoryId || !row.title.trim()) return;
      if (activeImportsRef.current.has(row.clip.id)) return;

      activeImportsRef.current.add(row.clip.id);
      updateRow(row.clip.id, { error: null, status: "importing" });

      void (async () => {
        try {
          const imported = await importTwitchClip({
            clipId: row.clip.id,
            categoryId: row.categoryId!,
            title: row.title.trim(),
          });
          updateRow(row.clip.id, {
            importedClipId: imported.clipId,
            status: "imported",
          });
          // The clip is in the library from here, whatever the session filing below does next.
          invalidateClipCollections(queryClient);
        } catch (error) {
          updateRow(row.clip.id, {
            error: importErrorMessage(error),
            status:
              error instanceof ApiError && error.status === 409
                ? "duplicate"
                : "error",
          });
        } finally {
          activeImportsRef.current.delete(row.clip.id);
          requestedImportsRef.current.delete(row.clip.id);
          void queryClient.invalidateQueries({
            queryKey: storageUsageQueryKey,
          });
        }
      })();
    },
    [queryClient, updateRow],
  );

  // Feed the requested rows through the import slots as earlier ones finish.
  useEffect(() => {
    const free = MAX_ACTIVE_IMPORTS - activeImportsRef.current.size;
    if (free <= 0) return;
    rows
      .filter(
        (row) =>
          requestedImportsRef.current.has(row.clip.id) &&
          row.status === "ready" &&
          row.selected,
      )
      .slice(0, free)
      .forEach(startImport);
  }, [rows, startImport]);

  // Once nothing requested is still on its way, file what landed into gaming-session collections.
  useEffect(() => {
    const pending = rows.some(
      (row) =>
        requestedImportsRef.current.has(row.clip.id) &&
        (row.status === "ready" || row.status === "importing"),
    );
    if (pending) return;

    for (const session of groupImportedTwitchRowsIntoSessions(rows)) {
      if (filingSessionsRef.current.has(session.key)) continue;
      filingSessionsRef.current.add(session.key);

      setRows((current) =>
        current.map((row) =>
          session.rowIds.includes(row.clip.id)
            ? { ...row, status: "filing" }
            : row,
        ),
      );

      void (async () => {
        try {
          const playlist = await ensureGamingSessionPlaylist({
            categoryId: session.categoryId,
            clipIds: session.clipIds,
            sessionDate: new Date(`${session.sessionDate}T00:00:00`),
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
          });
          setRows((current) =>
            current.map((row) =>
              session.rowIds.includes(row.clip.id)
                ? { ...row, sessionPlaylistId: playlist.id, status: "saved" }
                : row,
            ),
          );
          void queryClient.invalidateQueries({ queryKey: ["playlists"] });
        } catch (error) {
          // The clips are in the library; only the collection filing failed, and the library still shows them.
          const detail = uploadErrorMessage(error);
          setRows((current) =>
            current.map((row) =>
              session.rowIds.includes(row.clip.id)
                ? {
                    ...row,
                    error: `Added to your library, but the session collection could not be updated (${detail}).`,
                    status: "saved",
                  }
                : row,
            ),
          );
        } finally {
          filingSessionsRef.current.delete(session.key);
        }
      })();
    }
  }, [queryClient, rows]);

  useBlocker({
    shouldBlockFn: () => {
      if (activeCount === 0) return false;
      return !window.confirm(
        "Leave while Twitch clips are still importing? Clips already copied stay in your library.",
      );
    },
    enableBeforeUnload: activeCount > 0,
  });

  function importSelected() {
    importableRows.forEach((row) =>
      requestedImportsRef.current.add(row.clip.id),
    );
    setRows((current) => [...current]);
  }

  function retryRow(clipId: string) {
    requestedImportsRef.current.add(clipId);
    setRows((current) =>
      current.map((row) =>
        row.clip.id === clipId
          ? {
              ...row,
              error: null,
              selected: true,
              status: row.categoryId ? "ready" : "needs_game",
            }
          : row,
      ),
    );
  }

  /** Adds a game Twitch tagged the clips with to the library, then assigns it to every row waiting for it. */
  function addGameForRows(igdbId: number) {
    setAddGameError(null);
    setAddingGame(igdbId);
    addGame.mutate(igdbId, {
      onSuccess: (category) => {
        setRows((current) =>
          current.map((row) =>
            row.status === "needs_game" && row.clip.igdbId === igdbId
              ? {
                  ...row,
                  categoryId: category.id,
                  selected: true,
                  status: "ready",
                }
              : row,
          ),
        );
      },
      onError: (error) => {
        setAddGameError(
          error instanceof ApiError
            ? error.message
            : "The game could not be added.",
        );
      },
      onSettled: () => setAddingGame(null),
    });
  }

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: twitchClipsQueryKey });
  }

  return {
    activeCount,
    addGameError,
    addGameForRows,
    addingGame,
    allSelected,
    categoriesLoading,
    categoryById,
    categoryOptions,
    clipsQuery,
    days,
    gamesToAdd,
    importSelected,
    importableRows,
    needsGameCount,
    refresh,
    retryRow,
    rows,
    savedCount,
    selectedRows,
    setDays,
    setRowCategory: (clipId: string, categoryId: string | null) =>
      setRows((current) => setTwitchRowCategory(current, clipId, categoryId)),
    setRowTitle: (clipId: string, title: string) =>
      setRows((current) => setTwitchRowTitle(current, clipId, title)),
    state,
    toggleAll: () =>
      setRows((current) => setAllTwitchRowsSelected(current, !allSelected)),
    toggleRow: (clipId: string) =>
      setRows((current) => toggleTwitchRowSelection(current, clipId)),
    twitchLogin,
  };
}

function importErrorMessage(error: unknown) {
  if (error instanceof ApiError && error.status === 409) {
    return "This Twitch clip is already in your library.";
  }
  if (error instanceof ApiError && error.message) {
    return error.message;
  }
  return uploadErrorMessage(error);
}
