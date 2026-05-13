import type { ChangeEvent, DragEvent, KeyboardEvent } from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useBlocker } from "@tanstack/react-router";
import type * as tus from "tus-js-client";
import type {
  BulkUploadFileInput,
  BulkUploadRejectedFile,
  BulkUploadRow,
} from "@/hooks/bulkUploadQueue";
import {
  applyTagsToSelectedRows,
  buildBulkUploadQueue,
  groupRowsIntoSessions,
} from "@/hooks/bulkUploadQueue";
import { useCategories } from "@/hooks/queries";
import { ApiError } from "@/shared/services/api-error";
import { addTagToVideo } from "@/shared/services/clips";
import {
  addClipsToPlaylist,
  ensureGamingSessionPlaylist,
  fetchPlaylists,
} from "@/shared/services/playlists";
import {
  calculateClipUploadMd5,
  createPreparedClipUpload,
  createTusClipUpload,
  uploadErrorMessage,
} from "@/utils/clipUpload";
import { bulkUploadInputsFromDataTransfer } from "@/utils/bulkUploadDrop";
import type { SelectOption } from "@/components/ui";

const MAX_ACTIVE_UPLOADS = 3;
const EMPTY_INITIAL_FILES: BulkUploadFileInput[] = [];

export type BulkUploadControllerOptions = {
  fallbackCategoryId?: string | null;
  initialFiles?: BulkUploadFileInput[];
};

export function useBulkUploadController({
  fallbackCategoryId = null,
  initialFiles = EMPTY_INITIAL_FILES,
}: BulkUploadControllerOptions) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);
  const initialFilesAppliedRef = useRef(false);
  const activeUploadIdsRef = useRef<Set<string>>(new Set());
  const requestedUploadIdsRef = useRef<Set<string>>(new Set());
  const uploadRefs = useRef<Map<string, tus.Upload>>(new Map());
  const duplicateKeysRef = useRef<Map<string, string>>(new Map());
  const filingSessionKeysRef = useRef<Set<string>>(new Set());
  const [rows, setRows] = useState<BulkUploadRow[]>([]);
  const [rejected, setRejected] = useState<BulkUploadRejectedFile[]>([]);
  const [dragOver, setDragOver] = useState(false);
  const [globalPaused, setGlobalPaused] = useState(false);
  const [tagInput, setTagInput] = useState("");
  const [collapsedSessions, setCollapsedSessions] = useState<Set<string>>(
    () => new Set(),
  );
  const { data: categories = [], isLoading: categoriesLoading } =
    useCategories();
  const { data: playlists = [] } = useQuery({
    queryKey: ["playlists"],
    queryFn: fetchPlaylists,
    staleTime: 5 * 60_000,
  });
  const categoryById = useMemo(
    () => new Map(categories.map((category) => [category.id, category])),
    [categories],
  );
  const playlistById = useMemo(
    () => new Map(playlists.map((playlist) => [playlist.id, playlist])),
    [playlists],
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
  const playlistOptions = useMemo<SelectOption[]>(
    () => [
      { label: "No collection", value: "" },
      ...playlists.map((playlist) => ({
        label: playlist.name,
        value: playlist.id,
      })),
    ],
    [playlists],
  );
  const sessions = useMemo(() => groupRowsIntoSessions(rows), [rows]);
  const selectedRows = rows.filter((row) => row.selected);
  const allSelected = rows.length > 0 && selectedRows.length === rows.length;
  const totalBytes = rows.reduce((total, row) => total + row.file.size, 0);
  const readyCount = rows.filter((row) => row.status === "ready").length;
  const activeCount = rows.filter((row) =>
    ["hashing", "creating", "uploading"].includes(row.status),
  ).length;
  const selectedReadyRows = selectedRows.filter(
    (row) => row.status === "ready" && row.categoryId && row.title.trim(),
  );
  const needsGameCount = rows.filter(
    (row) => row.status === "needs_game",
  ).length;
  const pendingCount = rows.filter((row) =>
    [
      "ready",
      "needs_game",
      "hashing",
      "creating",
      "uploading",
      "paused",
    ].includes(row.status),
  ).length;
  const shouldWarnOnLeave = rows.length > 0 && pendingCount > 0;

  function addFileInputs(inputs: BulkUploadFileInput[]) {
    if (!inputs.length) return;

    const result = buildBulkUploadQueue(inputs, categories, {
      fallbackCategoryId,
    });
    const batchId = `${Date.now()}:${rows.length}`;
    setRows((current) => [
      ...current,
      ...result.rows.map((row) => ({
        ...row,
        id: `${batchId}:${row.id}`,
      })),
    ]);
    setRejected((current) => [...result.rejected, ...current].slice(0, 6));
  }

  useEffect(() => {
    if (initialFilesAppliedRef.current || categoriesLoading) return;
    initialFilesAppliedRef.current = true;
    addFileInputs(initialFiles);
  }, [categoriesLoading, initialFiles]);

  const startRowUpload = useCallback((row: BulkUploadRow) => {
    if (!row.categoryId || !row.title.trim()) return;
    if (activeUploadIdsRef.current.has(row.id)) return;

    activeUploadIdsRef.current.add(row.id);
    setRows((current) =>
      current.map((item) =>
        item.id === row.id
          ? {
              ...item,
              bytesUploaded: 0,
              error: null,
              progress: 0,
              status: "hashing",
            }
          : item,
      ),
    );

    void (async () => {
      try {
        const md5Hash = await calculateClipUploadMd5(row.file);
        const duplicateKey = `${row.categoryId}:${md5Hash}`;
        const existingRowId = duplicateKeysRef.current.get(duplicateKey);

        if (existingRowId && existingRowId !== row.id) {
          activeUploadIdsRef.current.delete(row.id);
          requestedUploadIdsRef.current.delete(row.id);
          setRows((current) =>
            current.map((item) =>
              item.id === row.id
                ? {
                    ...item,
                    error: "This video is already queued for this game.",
                    md5Hash,
                    status: "duplicate",
                  }
                : item,
            ),
          );
          return;
        }

        duplicateKeysRef.current.set(duplicateKey, row.id);
        setRows((current) =>
          current.map((item) =>
            item.id === row.id
              ? {
                  ...item,
                  md5Hash,
                  status: "creating",
                }
              : item,
          ),
        );
        const response = await createPreparedClipUpload({
          categoryId: row.categoryId!,
          createdAt: row.createdAt,
          md5Hash,
          title: row.title.trim(),
        });

        const upload = createTusClipUpload({
          file: row.file,
          response,
          title: row.title.trim(),
          onProgress: (uploaded, total) => {
            setRows((current) =>
              current.map((item) =>
                item.id === row.id
                  ? {
                      ...item,
                      bytesUploaded: uploaded,
                      progress:
                        total > 0 ? Math.round((uploaded / total) * 100) : 0,
                    }
                  : item,
              ),
            );
          },
          onSuccess: () => {
            activeUploadIdsRef.current.delete(row.id);
            requestedUploadIdsRef.current.delete(row.id);
            setRows((current) =>
              current.map((item) =>
                item.id === row.id
                  ? {
                      ...item,
                      bytesUploaded: row.file.size,
                      error: null,
                      progress: 100,
                      status: "uploaded",
                      uploadedClipId: response.clipId,
                      uploadedVideoId: response.videoId,
                    }
                  : item,
              ),
            );
          },
          onError: (uploadError) => {
            activeUploadIdsRef.current.delete(row.id);
            requestedUploadIdsRef.current.delete(row.id);
            setRows((current) =>
              current.map((item) =>
                item.id === row.id
                  ? {
                      ...item,
                      error: uploadErrorMessage(uploadError),
                      status: "error",
                    }
                  : item,
              ),
            );
          },
        });

        uploadRefs.current.set(row.id, upload);
        const previousUploads = await upload.findPreviousUploads();
        if (previousUploads.length) {
          upload.resumeFromPreviousUpload(previousUploads[0]);
        }

        setRows((current) =>
          current.map((item) =>
            item.id === row.id ? { ...item, status: "uploading" } : item,
          ),
        );
        upload.start();
      } catch (error) {
        activeUploadIdsRef.current.delete(row.id);
        requestedUploadIdsRef.current.delete(row.id);
        setRows((current) =>
          current.map((item) =>
            item.id === row.id
              ? {
                  ...item,
                  error: uploadErrorMessage(error),
                  status:
                    error instanceof ApiError && error.status === 409
                      ? "duplicate"
                      : "error",
                }
              : item,
          ),
        );
      }
    })();
  }, []);

  async function fileSessionRows(
    sessionKey: string,
    uploadedRows: BulkUploadRow[],
  ) {
    const clipIds = uploadedRows.flatMap((row) =>
      row.uploadedClipId ? [row.uploadedClipId] : [],
    );
    const firstRow = uploadedRows[0];
    if (!firstRow?.categoryId || !firstRow.sessionDate || !clipIds.length) {
      filingSessionKeysRef.current.delete(sessionKey);
      return;
    }

    setRows((current) =>
      current.map((row) =>
        clipIds.includes(row.uploadedClipId ?? "")
          ? { ...row, error: null, status: "filing" }
          : row,
      ),
    );

    try {
      await Promise.all(
        uploadedRows.flatMap((row) =>
          row.tags
            .slice(0, 5)
            .map((tag) => addTagToVideo(row.uploadedClipId!, tag)),
        ),
      );

      const playlistClipIds = new Map<string, string[]>();
      for (const row of uploadedRows) {
        if (!row.playlistId || !row.uploadedClipId) continue;
        playlistClipIds.set(row.playlistId, [
          ...(playlistClipIds.get(row.playlistId) ?? []),
          row.uploadedClipId,
        ]);
      }

      await Promise.all(
        [...playlistClipIds.entries()].map(([playlistId, clipIds]) =>
          addClipsToPlaylist(playlistId, { clipIds }),
        ),
      );

      const sessionPlaylist = await ensureGamingSessionPlaylist({
        categoryId: firstRow.categoryId,
        clipIds,
        sessionDate: new Date(`${firstRow.sessionDate}T00:00:00`),
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
      });

      setRows((current) =>
        current.map((row) =>
          clipIds.includes(row.uploadedClipId ?? "")
            ? {
                ...row,
                error: null,
                sessionPlaylistId: sessionPlaylist.id,
                status: "saved",
              }
            : row,
        ),
      );
      setCollapsedSessions((current) => new Set(current).add(sessionKey));
    } catch (error) {
      setRows((current) =>
        current.map((row) =>
          clipIds.includes(row.uploadedClipId ?? "")
            ? {
                ...row,
                error: uploadErrorMessage(error),
                status: "filing_error",
              }
            : row,
        ),
      );
    } finally {
      filingSessionKeysRef.current.delete(sessionKey);
    }
  }

  useEffect(() => {
    if (globalPaused) return;

    const active = activeUploadIdsRef.current.size;
    if (active >= MAX_ACTIVE_UPLOADS) return;

    rows
      .filter(
        (row) =>
          requestedUploadIdsRef.current.has(row.id) &&
          row.status === "ready" &&
          row.selected &&
          row.categoryId &&
          row.title.trim(),
      )
      .slice(0, MAX_ACTIVE_UPLOADS - active)
      .forEach((row) => startRowUpload(row));
  }, [globalPaused, rows, startRowUpload]);

  useEffect(() => {
    sessions.forEach((session) => {
      if (!session.categoryId || !session.sessionDate) return;
      if (filingSessionKeysRef.current.has(session.key)) return;

      const uploadedRows = session.rows.filter(
        (row) => row.status === "uploaded" && row.uploadedClipId,
      );
      if (!uploadedRows.length) return;

      const hasUnfinishedRequestedRows = session.rows.some(
        (row) =>
          requestedUploadIdsRef.current.has(row.id) &&
          ["ready", "hashing", "creating", "uploading", "paused"].includes(
            row.status,
          ),
      );
      if (hasUnfinishedRequestedRows) return;

      filingSessionKeysRef.current.add(session.key);
      void fileSessionRows(session.key, uploadedRows);
    });
  }, [sessions]);

  useBlocker({
    shouldBlockFn: () => {
      if (!shouldWarnOnLeave) return false;
      return !window.confirm(
        "Leave this upload queue? Unsaved clips will be lost.",
      );
    },
    enableBeforeUnload: shouldWarnOnLeave,
  });

  function addFiles(files: FileList | File[]) {
    addFileInputs(Array.from(files));
  }

  function onInputChange(event: ChangeEvent<HTMLInputElement>) {
    if (event.currentTarget.files) addFiles(event.currentTarget.files);
    event.currentTarget.value = "";
  }

  async function onDrop(event: DragEvent<HTMLElement>) {
    event.preventDefault();
    setDragOver(false);
    addFileInputs(await bulkUploadInputsFromDataTransfer(event.dataTransfer));
  }

  function applyTags() {
    if (!tagInput.trim()) return;
    setRows((current) => applyTagsToSelectedRows(current, tagInput.split(",")));
    setTagInput("");
  }

  function startSelectedUploads() {
    selectedReadyRows.forEach((row) =>
      requestedUploadIdsRef.current.add(row.id),
    );
    setGlobalPaused(false);
    setRows((current) => [...current]);
  }

  function pauseRow(rowId: string) {
    uploadRefs.current.get(rowId)?.abort();
    activeUploadIdsRef.current.delete(rowId);
    setRows((current) =>
      current.map((row) =>
        row.id === rowId ? { ...row, status: "paused" } : row,
      ),
    );
  }

  function resumeRow(rowId: string) {
    const upload = uploadRefs.current.get(rowId);
    if (!upload || activeUploadIdsRef.current.size >= MAX_ACTIVE_UPLOADS)
      return;

    activeUploadIdsRef.current.add(rowId);
    requestedUploadIdsRef.current.add(rowId);
    setRows((current) =>
      current.map((row) =>
        row.id === rowId ? { ...row, error: null, status: "uploading" } : row,
      ),
    );
    upload.start();
  }

  function pauseAllUploads() {
    setGlobalPaused(true);
    for (const row of rows) {
      if (row.status === "uploading") pauseRow(row.id);
    }
  }

  function resumeAllUploads() {
    setGlobalPaused(false);
    rows
      .filter((row) => row.status === "paused")
      .slice(0, MAX_ACTIVE_UPLOADS)
      .forEach((row) => resumeRow(row.id));
  }

  function cancelRow(row: BulkUploadRow) {
    uploadRefs.current.get(row.id)?.abort(true);
    uploadRefs.current.delete(row.id);
    activeUploadIdsRef.current.delete(row.id);
    requestedUploadIdsRef.current.delete(row.id);
    if (row.categoryId && row.md5Hash) {
      duplicateKeysRef.current.delete(`${row.categoryId}:${row.md5Hash}`);
    }
    setRows((current) =>
      current.map((item) =>
        item.id === row.id
          ? {
              ...item,
              error: "Upload cancelled.",
              progress: 0,
              status: "cancelled",
            }
          : item,
      ),
    );
  }

  function retryRow(row: BulkUploadRow) {
    if (row.status === "filing_error" && row.uploadedClipId) {
      setRows((current) =>
        current.map((item) =>
          item.id === row.id
            ? { ...item, error: null, status: "uploaded" }
            : item,
        ),
      );
      return;
    }

    uploadRefs.current.delete(row.id);
    activeUploadIdsRef.current.delete(row.id);
    if (row.categoryId && row.md5Hash) {
      duplicateKeysRef.current.delete(`${row.categoryId}:${row.md5Hash}`);
    }
    requestedUploadIdsRef.current.add(row.id);
    setGlobalPaused(false);
    setRows((current) =>
      current.map((item) =>
        item.id === row.id
          ? {
              ...item,
              bytesUploaded: 0,
              error: null,
              md5Hash: null,
              progress: 0,
              status: item.categoryId ? "ready" : "needs_game",
              sessionPlaylistId: null,
              uploadedClipId: null,
              uploadedVideoId: null,
            }
          : item,
      ),
    );
  }

  function toggleSession(sessionKey: string) {
    setCollapsedSessions((current) => {
      const next = new Set(current);
      if (next.has(sessionKey)) next.delete(sessionKey);
      else next.add(sessionKey);
      return next;
    });
  }

  function handleDropzoneKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    fileInputRef.current?.click();
  }

  return {
    activeCount,
    allSelected,
    applyTags,
    cancelRow,
    categoriesLoading,
    categoryById,
    categoryOptions,
    collapsedSessions,
    dragOver,
    fileInputRef,
    folderInputRef,
    globalPaused,
    handleDropzoneKeyDown,
    needsGameCount,
    onDrop,
    onInputChange,
    pauseAllUploads,
    pauseRow,
    playlistById,
    playlistOptions,
    readyCount,
    rejected,
    resumeAllUploads,
    resumeRow,
    retryRow,
    rows,
    selectedReadyRows,
    selectedRows,
    sessions,
    setDragOver,
    setRows,
    setTagInput,
    startSelectedUploads,
    tagInput,
    toggleSession,
    totalBytes,
  };
}
