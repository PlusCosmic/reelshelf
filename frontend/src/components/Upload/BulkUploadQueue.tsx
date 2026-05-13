import type {
  ChangeEventHandler,
  Dispatch,
  DragEventHandler,
  KeyboardEventHandler,
  RefObject,
  SetStateAction,
} from "react";
import {
  IconAlertTriangle,
  IconChevronDown,
  IconFolder,
  IconPlus,
  IconTag,
  IconUpload,
} from "@tabler/icons-react";
import { Link } from "@tanstack/react-router";
import type {
  BulkUploadFileInput,
  BulkUploadRow,
  BulkUploadSession,
} from "@/hooks/bulkUploadQueue";
import {
  setAllRowsSelected,
  setRowCategory,
  setRowTitle,
  setSelectedRowsPlaylist,
  setSessionRowsSelected,
  toggleRowSelection,
} from "@/hooks/bulkUploadQueue";
import {
  Badge,
  Button,
  Checkbox,
  Field,
  Input,
  Select,
  type SelectOption,
} from "@/components/ui";
import { formatFileSize } from "@/shared/utils/format";
import { useBulkUploadController } from "./useBulkUploadController";
import type { GameCategoryResponse, PlaylistSummary } from "@/api-client";

const directoryInputProps = {
  directory: "",
  webkitdirectory: "",
} as Record<string, string>;

export type BulkUploadQueueProps = {
  fallbackCategoryId?: string | null;
  initialFiles?: BulkUploadFileInput[];
};

export function BulkUploadQueue(props: BulkUploadQueueProps) {
  const controller = useBulkUploadController(props);

  return (
    <section className="rs-upload-page rs-bulk-upload-page">
      <QueueInputs
        fileInputRef={controller.fileInputRef}
        folderInputRef={controller.folderInputRef}
        onInputChange={controller.onInputChange}
      />

      <QueueHeader
        activeCount={controller.activeCount}
        needsGameCount={controller.needsGameCount}
        readyCount={controller.readyCount}
        rowCount={controller.rows.length}
        totalBytes={controller.totalBytes}
      />

      <QueueDropzone
        dragOver={controller.dragOver}
        fileInputRef={controller.fileInputRef}
        folderInputRef={controller.folderInputRef}
        onDragLeave={() => controller.setDragOver(false)}
        onDragOver={(event) => {
          event.preventDefault();
          controller.setDragOver(true);
        }}
        onDrop={controller.onDrop}
        onKeyDown={controller.handleDropzoneKeyDown}
      />

      {controller.rejected.length ? (
        <div className="rs-upload-error" role="status">
          {controller.rejected[0].message}
          {controller.rejected.length > 1
            ? ` ${controller.rejected.length - 1} more skipped.`
            : ""}
        </div>
      ) : null}

      {controller.rows.length ? (
        <>
          <QueueBulkActions
            activeCount={controller.activeCount}
            allSelected={controller.allSelected}
            globalPaused={controller.globalPaused}
            onApplyTags={controller.applyTags}
            onPauseOrResumeAll={
              controller.globalPaused
                ? controller.resumeAllUploads
                : controller.pauseAllUploads
            }
            onSelectAll={() =>
              controller.setRows((current) =>
                setAllRowsSelected(current, !controller.allSelected),
              )
            }
            onSetSelectedPlaylist={(playlistId) =>
              controller.setRows((current) =>
                setSelectedRowsPlaylist(current, playlistId),
              )
            }
            onStartSelectedUploads={controller.startSelectedUploads}
            playlistOptions={controller.playlistOptions}
            rowCount={controller.rows.length}
            selectedReadyCount={controller.selectedReadyRows.length}
            selectedRows={controller.selectedRows}
            setTagInput={controller.setTagInput}
            tagInput={controller.tagInput}
          />

          <QueueSessions
            categoriesLoading={controller.categoriesLoading}
            categoryById={controller.categoryById}
            categoryOptions={controller.categoryOptions}
            collapsedSessions={controller.collapsedSessions}
            onCancelRow={controller.cancelRow}
            onPauseRow={controller.pauseRow}
            onResumeRow={controller.resumeRow}
            onRetryRow={controller.retryRow}
            onToggleSession={controller.toggleSession}
            playlistById={controller.playlistById}
            sessions={controller.sessions}
            setRows={controller.setRows}
          />
        </>
      ) : (
        <div className="rs-empty">
          No clips queued. Add a file or folder to start the review queue.
        </div>
      )}
    </section>
  );
}

function QueueInputs({
  fileInputRef,
  folderInputRef,
  onInputChange,
}: {
  fileInputRef: RefObject<HTMLInputElement | null>;
  folderInputRef: RefObject<HTMLInputElement | null>;
  onInputChange: ChangeEventHandler<HTMLInputElement>;
}) {
  return (
    <>
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
    </>
  );
}

function QueueHeader({
  activeCount,
  needsGameCount,
  readyCount,
  rowCount,
  totalBytes,
}: {
  activeCount: number;
  needsGameCount: number;
  readyCount: number;
  rowCount: number;
  totalBytes: number;
}) {
  return (
    <div className="rs-bulk-upload-header">
      <div>
        <div className="rs-eyebrow">Bulk upload</div>
        <h1 className="rs-display rs-upload-title">
          Review clips before they hit the shelf.
        </h1>
      </div>
      <div className="rs-bulk-upload-summary" aria-label="Queue summary">
        <span>{rowCount} clips</span>
        <span>{formatFileSize(totalBytes)}</span>
        <span>{readyCount} ready</span>
        {activeCount > 0 ? <span>{activeCount} uploading</span> : null}
        {needsGameCount > 0 ? <span>{needsGameCount} need game</span> : null}
      </div>
    </div>
  );
}

function QueueDropzone({
  dragOver,
  fileInputRef,
  folderInputRef,
  onDragLeave,
  onDragOver,
  onDrop,
  onKeyDown,
}: {
  dragOver: boolean;
  fileInputRef: RefObject<HTMLInputElement | null>;
  folderInputRef: RefObject<HTMLInputElement | null>;
  onDragLeave: DragEventHandler<HTMLDivElement>;
  onDragOver: DragEventHandler<HTMLDivElement>;
  onDrop: DragEventHandler<HTMLDivElement>;
  onKeyDown: KeyboardEventHandler<HTMLDivElement>;
}) {
  return (
    <div
      role="button"
      tabIndex={0}
      className={`rs-upload-dropzone rs-bulk-dropzone${
        dragOver ? " active" : ""
      }`}
      onClick={() => fileInputRef.current?.click()}
      onKeyDown={onKeyDown}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
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
        <button
          type="button"
          onClick={(event) => {
            event.stopPropagation();
            folderInputRef.current?.click();
          }}
        >
          <IconFolder size={14} /> Add folder
        </button>
      </span>
    </div>
  );
}

function QueueBulkActions({
  activeCount,
  allSelected,
  globalPaused,
  onApplyTags,
  onPauseOrResumeAll,
  onSelectAll,
  onSetSelectedPlaylist,
  onStartSelectedUploads,
  playlistOptions,
  rowCount,
  selectedReadyCount,
  selectedRows,
  setTagInput,
  tagInput,
}: {
  activeCount: number;
  allSelected: boolean;
  globalPaused: boolean;
  onApplyTags: () => void;
  onPauseOrResumeAll: () => void;
  onSelectAll: () => void;
  onSetSelectedPlaylist: (playlistId: string | null) => void;
  onStartSelectedUploads: () => void;
  playlistOptions: SelectOption[];
  rowCount: number;
  selectedReadyCount: number;
  selectedRows: BulkUploadRow[];
  setTagInput: (tagInput: string) => void;
  tagInput: string;
}) {
  return (
    <div className="rs-bulk-actions" aria-label="Bulk upload actions">
      <label className="rs-bulk-select-all">
        <Checkbox checked={allSelected} onChange={onSelectAll} />
        {selectedRows.length} of {rowCount} selected
      </label>

      <Field className="rs-bulk-inline-field" label="Tags">
        <Input
          compact
          value={tagInput}
          onChange={(event) => setTagInput(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") onApplyTags();
          }}
          placeholder="ranked, clutch"
        />
        <Button size="sm" onClick={onApplyTags}>
          <IconTag size={14} />
          Apply
        </Button>
      </Field>

      <Field className="rs-bulk-inline-field" label="Collection">
        <Select
          aria-label="Collection for selected clips"
          options={playlistOptions}
          value={selectedRows[0]?.playlistId ?? ""}
          onValueChange={(value) => onSetSelectedPlaylist(value || null)}
        />
      </Field>

      <Button disabled={activeCount === 0} onClick={onPauseOrResumeAll}>
        {globalPaused ? "Resume all" : "Pause all"}
      </Button>
      <Button
        className="rs-bulk-primary-action"
        disabled={selectedReadyCount === 0}
        onClick={onStartSelectedUploads}
        variant="primary"
      >
        Add {selectedReadyCount || selectedRows.length} to library
      </Button>
    </div>
  );
}

function QueueSessions({
  categoriesLoading,
  categoryById,
  categoryOptions,
  collapsedSessions,
  onCancelRow,
  onPauseRow,
  onResumeRow,
  onRetryRow,
  onToggleSession,
  playlistById,
  sessions,
  setRows,
}: {
  categoriesLoading: boolean;
  categoryById: Map<string, GameCategoryResponse>;
  categoryOptions: SelectOption[];
  collapsedSessions: Set<string>;
  onCancelRow: (row: BulkUploadRow) => void;
  onPauseRow: (rowId: string) => void;
  onResumeRow: (rowId: string) => void;
  onRetryRow: (row: BulkUploadRow) => void;
  onToggleSession: (sessionKey: string) => void;
  playlistById: Map<string, PlaylistSummary>;
  sessions: BulkUploadSession[];
  setRows: Dispatch<SetStateAction<BulkUploadRow[]>>;
}) {
  return (
    <div className="rs-bulk-queue" aria-label="Queued clips">
      <div className="rs-bulk-table-head">
        <span />
        <span>Clip</span>
        <span>Game</span>
        <span>Tags</span>
        <span>Collection</span>
        <span>Status</span>
      </div>
      {sessions.map((session) => (
        <QueueSession
          key={session.key}
          categoriesLoading={categoriesLoading}
          categoryById={categoryById}
          categoryOptions={categoryOptions}
          collapsed={collapsedSessions.has(session.key)}
          onCancelRow={onCancelRow}
          onPauseRow={onPauseRow}
          onResumeRow={onResumeRow}
          onRetryRow={onRetryRow}
          onToggleSession={onToggleSession}
          playlistById={playlistById}
          session={session}
          setRows={setRows}
        />
      ))}
    </div>
  );
}

function QueueSession({
  categoriesLoading,
  categoryById,
  categoryOptions,
  collapsed,
  onCancelRow,
  onPauseRow,
  onResumeRow,
  onRetryRow,
  onToggleSession,
  playlistById,
  session,
  setRows,
}: {
  categoriesLoading: boolean;
  categoryById: Map<string, GameCategoryResponse>;
  categoryOptions: SelectOption[];
  collapsed: boolean;
  onCancelRow: (row: BulkUploadRow) => void;
  onPauseRow: (rowId: string) => void;
  onResumeRow: (rowId: string) => void;
  onRetryRow: (row: BulkUploadRow) => void;
  onToggleSession: (sessionKey: string) => void;
  playlistById: Map<string, PlaylistSummary>;
  session: BulkUploadSession;
  setRows: Dispatch<SetStateAction<BulkUploadRow[]>>;
}) {
  const sessionRowsSelected = session.rows.every((row) => row.selected);
  const sessionSomeSelected =
    !sessionRowsSelected && session.rows.some((row) => row.selected);
  const category = session.categoryId
    ? categoryById.get(session.categoryId)
    : null;
  const savedSessionPlaylistId = session.rows.find(
    (row) => row.status === "saved" && row.sessionPlaylistId,
  )?.sessionPlaylistId;
  const fullySaved = session.rows.every((row) => row.status === "saved");

  return (
    <div className="rs-bulk-session">
      <div
        className="rs-bulk-session-header"
        role="button"
        tabIndex={0}
        onClick={() => onToggleSession(session.key)}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            onToggleSession(session.key);
          }
        }}
      >
        <Checkbox
          checked={sessionRowsSelected}
          ref={(input) => {
            if (input) input.indeterminate = sessionSomeSelected;
          }}
          onClick={(event) => event.stopPropagation()}
          onChange={() =>
            setRows((current) =>
              setSessionRowsSelected(current, session, !sessionRowsSelected),
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
                row.categoryId ? categoryById.get(row.categoryId)?.name : null
              }
              categoriesLoading={categoriesLoading}
              categoryOptions={categoryOptions}
              collectionName={
                row.playlistId ? playlistById.get(row.playlistId)?.name : null
              }
              onCancel={onCancelRow}
              onPause={onPauseRow}
              onResume={onResumeRow}
              onRetry={onRetryRow}
              row={row}
              setRows={setRows}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function QueueRow({
  categoriesLoading,
  categoryName,
  categoryOptions,
  collectionName,
  onCancel,
  onPause,
  onResume,
  onRetry,
  row,
  setRows,
}: {
  categoriesLoading: boolean;
  categoryName: string | null | undefined;
  categoryOptions: SelectOption[];
  collectionName: string | null | undefined;
  onCancel: (row: BulkUploadRow) => void;
  onPause: (rowId: string) => void;
  onResume: (rowId: string) => void;
  onRetry: (row: BulkUploadRow) => void;
  row: BulkUploadRow;
  setRows: Dispatch<SetStateAction<BulkUploadRow[]>>;
}) {
  const checkboxId = `bulk-row-${row.id}`;

  return (
    <div className="rs-bulk-row">
      <label
        className="rs-bulk-row-check"
        htmlFor={checkboxId}
        aria-label={`Select ${row.title}`}
      >
        <Checkbox
          id={checkboxId}
          checked={row.selected}
          onChange={() =>
            setRows((current) => toggleRowSelection(current, row.id))
          }
        />
      </label>

      <div className="rs-bulk-row-clip">
        <div className="rs-upload-thumb" />
        <div>
          <Input
            aria-label={`Title for ${row.file.name}`}
            compact
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

      <Field className="rs-bulk-row-field" label="Game">
        <Select
          aria-label={`Game for ${row.title}`}
          options={categoryOptions}
          value={row.categoryId ?? ""}
          disabled={categoriesLoading}
          onValueChange={(value) => {
            const categoryId = value || null;
            setRows((current) => setRowCategory(current, row.id, categoryId));
          }}
        />
        {row.gameAssignmentSource === "source_path" ? (
          <small>Matched from path</small>
        ) : null}
      </Field>

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
  const danger = [
    "needs_game",
    "error",
    "filing_error",
    "duplicate",
    "cancelled",
  ].includes(status);

  return (
    <Badge
      className={`rs-bulk-status ${status}`}
      tone={danger ? "danger" : "accent"}
    >
      {label}
    </Badge>
  );
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
        <Button size="sm" onClick={onPause}>
          Pause
        </Button>
        <Button size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </span>
    );
  }

  if (row.status === "paused") {
    return (
      <span className="rs-bulk-row-actions">
        <Button size="sm" onClick={onResume}>
          Resume
        </Button>
        <Button size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </span>
    );
  }

  if (
    ["error", "filing_error", "duplicate", "cancelled"].includes(row.status)
  ) {
    return (
      <span className="rs-bulk-row-actions">
        <Button size="sm" onClick={onRetry}>
          Retry
        </Button>
      </span>
    );
  }

  return null;
}
