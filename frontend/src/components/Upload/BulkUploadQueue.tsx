import type { ChangeEvent, DragEvent } from "react";
import { useMemo, useRef, useState } from "react";
import {
  IconAlertTriangle,
  IconChevronDown,
  IconFolder,
  IconPlus,
  IconTag,
  IconUpload,
} from "@tabler/icons-react";
import { useBlocker } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
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
import { fetchPlaylists } from "@/shared/services/playlists";
import { formatFileSize } from "@/shared/utils/format";

const directoryInputProps = {
  directory: "",
  webkitdirectory: "",
} as Record<string, string>;

export function BulkUploadQueue() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);
  const [rows, setRows] = useState<BulkUploadRow[]>([]);
  const [rejected, setRejected] = useState<BulkUploadRejectedFile[]>([]);
  const [dragOver, setDragOver] = useState(false);
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

  function toggleSession(sessionKey: string) {
    setCollapsedSessions((current) => {
      const next = new Set(current);
      if (next.has(sessionKey)) next.delete(sessionKey);
      else next.add(sessionKey);
      return next;
    });
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

            <button className="rs-button primary" type="button" disabled>
              Add selected to library
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
  row,
  setRows,
}: {
  categories: { id: string; name: string }[];
  categoriesLoading: boolean;
  categoryName: string | null | undefined;
  collectionName: string | null | undefined;
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
