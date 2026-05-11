import type { ChangeEvent, DragEvent } from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  IconAlertTriangle,
  IconChevronDown,
  IconFolder,
  IconPlus,
  IconTag,
  IconUpload,
} from "@tabler/icons-react";
import { Link, useBlocker } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import type * as tus from "tus-js-client";
import type {
  BulkUploadRejectedFile,
  BulkUploadRow,
} from "@/hooks/bulkUploadQueue";
import {
  applyTagsToSelectedRows,
  buildBulkUploadQueue,
  groupRowsIntoSessions,
  setAllRowsSelected,
  setRowCategory,
  setRowTitle,
  setSelectedRowsPlaylist,
  setSessionRowsSelected,
  toggleRowSelection,
} from "@/hooks/bulkUploadQueue";
import { useCategories } from "@/hooks/queries";
import { ApiError } from "@/shared/services/api-error";
import { addTagToVideo } from "@/shared/services/clips";
import {
  addClipsToPlaylist,
  ensureGamingSessionPlaylist,
  fetchPlaylists,
} from "@/shared/services/playlists";
import { formatFileSize } from "@/shared/utils/format";
import {
  calculateClipUploadMd5,
  createPreparedClipUpload,
  createTusClipUpload,
  uploadErrorMessage,
} from "@/utils/clipUpload";

const directoryInputProps = {
  directory: "",
  webkitdirectory: "",
} as Record<string, string>;

const MAX_ACTIVE_UPLOADS = 3;

export function BulkUploadQueue() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);
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
    const nextFiles = Array.from(files);
    if (!nextFiles.length) return;

    const result = buildBulkUploadQueue(nextFiles, categories);
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

  function onInputChange(event: ChangeEvent<HTMLInputElement>) {
    if (event.currentTarget.files) addFiles(event.currentTarget.files);
    event.currentTarget.value = "";
  }

  function onDrop(event: DragEvent<HTMLElement>) {
    event.preventDefault();
    setDragOver(false);
    addFiles(event.dataTransfer.files);
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
    rows
      .filter((row) => row.status === "uploading")
      .forEach((row) => pauseRow(row.id));
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

  async function fileSessionRows(
    sessionKey: string,
    uploadedRows: BulkUploadRow[],
  ) {
    const clipIds = uploadedRows
      .map((row) => row.uploadedClipId)
      .filter((clipId): clipId is string => Boolean(clipId));
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

  return (
    <section className="rs-upload-page rs-bulk-upload-page">
      <input
        ref={fileInputRef}
        aria-label="Choose video files"
        className="rs-visually-hidden"
        type="file"
        accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
        multiple
        onChange={onInputChange}
      />
      <input
        ref={folderInputRef}
        aria-label="Choose recorder folder"
        className="rs-visually-hidden"
        type="file"
        accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
        multiple
        onChange={onInputChange}
        {...directoryInputProps}
      />

      <div className="rs-bulk-upload-header">
        <div>
          <div className="rs-eyebrow">Bulk upload</div>
          <h1 className="rs-display rs-upload-title">
            Review clips before they hit the shelf.
          </h1>
        </div>
        <div className="rs-bulk-upload-summary" aria-label="Queue summary">
          <span>{rows.length} clips</span>
          <span>{formatFileSize(totalBytes)}</span>
          <span>{readyCount} ready</span>
          {activeCount > 0 ? <span>{activeCount} uploading</span> : null}
          {needsGameCount > 0 ? <span>{needsGameCount} need game</span> : null}
        </div>
      </div>

      <button
        className={`rs-upload-dropzone rs-bulk-dropzone${
          dragOver ? " active" : ""
        }`}
        type="button"
        onClick={() => fileInputRef.current?.click()}
        onDragOver={(event) => {
          event.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={onDrop}
      >
        <span className="rs-upload-drop-icon">
          <IconUpload size={26} />
        </span>
        <span className="rs-upload-drop-title">
          Drop clips here, or click to browse
        </span>
        <span className="rs-upload-drop-copy">
          Add one clip, many clips, or use folder import for recorder folders.
        </span>
        <span className="rs-bulk-drop-actions">
          <span>
            <IconPlus size={14} /> Add files
          </span>
          <span
            onClick={(event) => {
              event.stopPropagation();
              folderInputRef.current?.click();
            }}
          >
            <IconFolder size={14} /> Add folder
          </span>
        </span>
      </button>

      {rejected.length ? (
        <div className="rs-upload-error" role="status">
          {rejected[0].message}
          {rejected.length > 1 ? ` ${rejected.length - 1} more skipped.` : ""}
        </div>
      ) : null}

      {rows.length ? (
        <>
          <div className="rs-bulk-actions" aria-label="Bulk upload actions">
            <label className="rs-bulk-select-all">
              <input
                type="checkbox"
                checked={allSelected}
                onChange={() =>
                  setRows((current) =>
                    setAllRowsSelected(current, !allSelected),
                  )
                }
              />
              {selectedRows.length} of {rows.length} selected
            </label>

            <label className="rs-bulk-inline-field">
              <span>Tags</span>
              <input
                value={tagInput}
                onChange={(event) => setTagInput(event.currentTarget.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") applyTags();
                }}
                placeholder="ranked, clutch"
              />
              <button type="button" onClick={applyTags}>
                <IconTag size={14} />
                Apply
              </button>
            </label>

            <label className="rs-bulk-inline-field">
              <span>Collection</span>
              <select
                value={selectedRows[0]?.playlistId ?? ""}
                onChange={(event) => {
                  const playlistId = event.currentTarget.value || null;
                  setRows((current) =>
                    setSelectedRowsPlaylist(current, playlistId),
                  );
                }}
              >
                <option value="">No collection</option>
                {playlists.map((playlist) => (
                  <option key={playlist.id} value={playlist.id}>
                    {playlist.name}
                  </option>
                ))}
              </select>
            </label>

            <button
              className="rs-button"
              type="button"
              disabled={activeCount === 0}
              onClick={globalPaused ? resumeAllUploads : pauseAllUploads}
            >
              {globalPaused ? "Resume all" : "Pause all"}
            </button>
            <button
              className="rs-button primary"
              type="button"
              disabled={selectedReadyRows.length === 0}
              onClick={startSelectedUploads}
            >
              Add {selectedReadyRows.length || selectedRows.length} to library
            </button>
          </div>

          <div className="rs-bulk-queue" aria-label="Queued clips">
            <div className="rs-bulk-table-head">
              <span />
              <span>Clip</span>
              <span>Game</span>
              <span>Tags</span>
              <span>Collection</span>
              <span>Status</span>
            </div>
            {sessions.map((session) => {
              const sessionRowsSelected = session.rows.every(
                (row) => row.selected,
              );
              const sessionSomeSelected =
                !sessionRowsSelected &&
                session.rows.some((row) => row.selected);
              const category = session.categoryId
                ? categoryById.get(session.categoryId)
                : null;
              const collapsed = collapsedSessions.has(session.key);
              const savedSessionPlaylistId = session.rows.find(
                (row) => row.status === "saved" && row.sessionPlaylistId,
              )?.sessionPlaylistId;
              const fullySaved = session.rows.every(
                (row) => row.status === "saved",
              );

              return (
                <div className="rs-bulk-session" key={session.key}>
                  <div
                    className="rs-bulk-session-header"
                    role="button"
                    tabIndex={0}
                    onClick={() => toggleSession(session.key)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        toggleSession(session.key);
                      }
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={sessionRowsSelected}
                      ref={(input) => {
                        if (input) input.indeterminate = sessionSomeSelected;
                      }}
                      onClick={(event) => event.stopPropagation()}
                      onChange={() =>
                        setRows((current) =>
                          setSessionRowsSelected(
                            current,
                            session,
                            !sessionRowsSelected,
                          ),
                        )
                      }
                    />
                    <span className="rs-bulk-session-swatch" />
                    <span className="rs-bulk-session-title">
                      {category?.name ?? "Needs game"}
                    </span>
                    <span className="rs-bulk-session-meta">
                      {session.rows.length} clips
                      {session.sessionDate ? ` · ${session.sessionDate}` : ""}
                    </span>
                    {!session.categoryId ? (
                      <span className="rs-bulk-session-warning">
                        <IconAlertTriangle size={13} /> Assign a game
                      </span>
                    ) : null}
                    {fullySaved && savedSessionPlaylistId ? (
                      <Link
                        className="rs-bulk-session-link"
                        to="/playlists/$playlistId"
                        params={{ playlistId: savedSessionPlaylistId }}
                        onClick={(event) => event.stopPropagation()}
                      >
                        Open collection
                      </Link>
                    ) : null}
                    <IconChevronDown
                      className={collapsed ? "collapsed" : undefined}
                      size={15}
                    />
                  </div>

                  {!collapsed ? (
                    <div className="rs-bulk-session-rows">
                      {session.rows.map((row) => (
                        <QueueRow
                          key={row.id}
                          categoryName={
                            row.categoryId
                              ? categoryById.get(row.categoryId)?.name
                              : null
                          }
                          categories={categories}
                          categoriesLoading={categoriesLoading}
                          collectionName={
                            row.playlistId
                              ? playlistById.get(row.playlistId)?.name
                              : null
                          }
                          onCancel={cancelRow}
                          onPause={pauseRow}
                          onResume={resumeRow}
                          onRetry={retryRow}
                          row={row}
                          setRows={setRows}
                        />
                      ))}
                    </div>
                  ) : null}
                </div>
              );
            })}
          </div>
        </>
      ) : (
        <div className="rs-empty">
          No clips queued. Add a file or folder to start the review queue.
        </div>
      )}
    </section>
  );
}

function QueueRow({
  categories,
  categoriesLoading,
  categoryName,
  collectionName,
  onCancel,
  onPause,
  onResume,
  onRetry,
  row,
  setRows,
}: {
  categories: { id: string; name: string }[];
  categoriesLoading: boolean;
  categoryName: string | null | undefined;
  collectionName: string | null | undefined;
  onCancel: (row: BulkUploadRow) => void;
  onPause: (rowId: string) => void;
  onResume: (rowId: string) => void;
  onRetry: (row: BulkUploadRow) => void;
  row: BulkUploadRow;
  setRows: React.Dispatch<React.SetStateAction<BulkUploadRow[]>>;
}) {
  return (
    <div className="rs-bulk-row">
      <label className="rs-bulk-row-check" aria-label={`Select ${row.title}`}>
        <input
          type="checkbox"
          checked={row.selected}
          onChange={() =>
            setRows((current) => toggleRowSelection(current, row.id))
          }
        />
      </label>

      <div className="rs-bulk-row-clip">
        <div className="rs-upload-thumb" />
        <div>
          <input
            aria-label={`Title for ${row.file.name}`}
            value={row.title}
            onChange={(event) => {
              const title = event.currentTarget.value;
              setRows((current) => setRowTitle(current, row.id, title));
            }}
          />
          <div className="rs-meta">
            {row.sourcePath} · {formatFileSize(row.file.size)}
          </div>
        </div>
      </div>

      <label className="rs-bulk-row-field">
        <span>Game</span>
        <select
          value={row.categoryId ?? ""}
          disabled={categoriesLoading}
          onChange={(event) => {
            const categoryId = event.currentTarget.value || null;
            setRows((current) => setRowCategory(current, row.id, categoryId));
          }}
        >
          <option value="">Choose game</option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
            </option>
          ))}
        </select>
        {row.gameAssignmentSource === "source_path" ? (
          <small>Matched from path</small>
        ) : null}
      </label>

      <div className="rs-bulk-row-tags">
        {row.tags.length ? (
          row.tags.map((tag) => <span key={tag}>#{tag}</span>)
        ) : (
          <span className="muted">No tags</span>
        )}
      </div>

      <div className="rs-bulk-row-collection">
        {collectionName ?? "No collection"}
      </div>

      <div className="rs-bulk-row-status">
        <StatusBadge status={row.status} />
        {row.status === "uploading" || row.status === "paused" ? (
          <span className="rs-bulk-row-progress">{row.progress}%</span>
        ) : null}
        {row.error ? <small>{row.error}</small> : null}
        <RowUploadActions
          onCancel={() => onCancel(row)}
          onPause={() => onPause(row.id)}
          onResume={() => onResume(row.id)}
          onRetry={() => onRetry(row)}
          row={row}
        />
      </div>

      <div className="rs-bulk-card-meta">
        <span>{categoryName ?? "Choose game"}</span>
        <span>{collectionName ?? "No collection"}</span>
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: BulkUploadRow["status"] }) {
  const label = status.replaceAll("_", " ");
  return <span className={`rs-bulk-status ${status}`}>{label}</span>;
}

function RowUploadActions({
  onCancel,
  onPause,
  onResume,
  onRetry,
  row,
}: {
  onCancel: () => void;
  onPause: () => void;
  onResume: () => void;
  onRetry: () => void;
  row: BulkUploadRow;
}) {
  if (row.status === "uploading") {
    return (
      <span className="rs-bulk-row-actions">
        <button type="button" onClick={onPause}>
          Pause
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </span>
    );
  }

  if (row.status === "paused") {
    return (
      <span className="rs-bulk-row-actions">
        <button type="button" onClick={onResume}>
          Resume
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </span>
    );
  }

  if (
    ["error", "filing_error", "duplicate", "cancelled"].includes(row.status)
  ) {
    return (
      <span className="rs-bulk-row-actions">
        <button type="button" onClick={onRetry}>
          Retry
        </button>
      </span>
    );
  }

  return null;
}
