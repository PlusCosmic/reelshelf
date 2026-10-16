import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { useQueries } from "@tanstack/react-query";
import { IconChevronDown, IconChevronUp, IconX } from "@tabler/icons-react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import { useCategories } from "@/hooks/queries";
import { getVideo } from "@/shared/services/clips";
import { bookBinding } from "@/components/Reelshelf/reelshelf-model";
import {
  clipStage,
  clipStageLabel,
  isUnfinishedUpload,
  type ClipStage,
  type UploadItem,
} from "./addClipsModel";
import { useUploads } from "./UploadsProvider";

type Line = { item: UploadItem; stage: ClipStage | null };

const SHOWN_PER_BOOK = 4;

function stageOf(item: UploadItem, clip: Clip | undefined): ClipStage | null {
  if (item.status === "error" || item.status === "duplicate") return null;
  if (isUnfinishedUpload(item)) return clipStage(0, 0, item);
  return clip
    ? clipStage(clip.video.status, clip.video.encodeProgress)
    : { kind: "processing", progress: null };
}

function lineLabel(line: Line) {
  if (!line.stage) return line.item.error ?? "Couldn’t upload";
  if (line.item.status === "queued") return "Waiting";
  if (line.stage.kind === "ready") return "On the shelf";
  return clipStageLabel(line.stage);
}

function lineProgress(line: Line) {
  const stage = line.stage;
  if (!stage) return 0;
  if (stage.kind === "uploading") return stage.progress;
  if (stage.kind === "processing") return stage.progress ?? 0;
  return 100;
}

/** Ring of overall upload progress, by bytes. */
function Ring({ value, tone }: { value: number; tone: "upload" | "process" }) {
  const circumference = 2 * Math.PI * 9;
  return (
    <svg
      className={`rs-tray-ring ${tone}`}
      width="22"
      height="22"
      viewBox="0 0 22 22"
      aria-hidden="true"
    >
      <circle cx="11" cy="11" r="9" />
      <circle
        cx="11"
        cy="11"
        r="9"
        strokeDasharray={circumference}
        strokeDashoffset={circumference * (1 - value / 100)}
      />
    </svg>
  );
}

/**
 * The corner tray for clips on their way in. It follows the owner round the app: uploads first,
 * which need this tab, then Bunny's processing, which doesn't, then links to the books they landed
 * in.
 */
export function UploadTray() {
  const { items, paused, pauseAll, resumeAll, retry, clearFinished } =
    useUploads();
  const { data: categories = [] } = useCategories();
  const [open, setOpen] = useState(false);

  // Uploaded clips are polled until Bunny has finished with them.
  const uploaded = items.filter(
    (item) => item.status === "uploaded" && item.clipId,
  );
  const clipQueries = useQueries({
    queries: uploaded.map((item) => ({
      queryKey: ["clips", "arriving", item.clipId],
      queryFn: () => getVideo(item.clipId!),
      refetchInterval: (query: { state: { data?: Clip | null } }) => {
        const clip = query.state.data;
        return clip && clip.video.status === 3 ? false : 5000;
      },
    })),
  });
  const clipById = new Map<string, Clip>();
  clipQueries.forEach((query, index) => {
    if (query.data) clipById.set(uploaded[index].clipId!, query.data);
  });

  if (!items.length) return null;

  const lines: Line[] = items.map((item) => ({
    item,
    stage: stageOf(item, item.clipId ? clipById.get(item.clipId) : undefined),
  }));
  const uploading = lines.filter((line) => isUnfinishedUpload(line.item));
  const processing = lines.filter(
    (line) =>
      line.stage?.kind === "processing" || line.stage?.kind === "watchable",
  );
  const failed = lines.filter(
    (line) => !line.stage || line.stage.kind === "failed",
  );
  const categoryById = new Map(categories.map((c) => [c.id, c]));
  const books = [...new Set(items.map((item) => item.categoryId))];

  const totalBytes = uploading.reduce((sum, l) => sum + l.item.file.size, 0);
  const sentBytes = uploading.reduce(
    (sum, l) => sum + (l.item.file.size * l.item.progress) / 100,
    0,
  );
  const uploadPercent = totalBytes ? (sentBytes / totalBytes) * 100 : 100;
  const processedPercent =
    (100 * (lines.length - processing.length - uploading.length)) /
    lines.length;

  const summary = uploading.length
    ? {
        tone: "upload" as const,
        value: uploadPercent,
        title:
          books.length === 1
            ? `Adding ${items.length} clips to ${categoryById.get(books[0])?.name ?? "a book"}`
            : `Adding ${items.length} clips to ${books.length} books`,
        meta: paused ? "Paused" : `${uploading.length} still uploading`,
      }
    : processing.length
      ? {
          tone: "process" as const,
          value: processedPercent,
          title: `Processing ${processing.length} ${processing.length === 1 ? "clip" : "clips"}`,
          meta: "Safe to close this tab",
        }
      : {
          tone: "upload" as const,
          value: 100,
          title: `${items.length - failed.length} ${items.length - failed.length === 1 ? "clip" : "clips"} added`,
          meta: failed.length ? `${failed.length} didn’t make it` : "All done",
        };
  const finished = !uploading.length && !processing.length;

  return (
    <aside
      className={`rs-tray${open ? " open" : ""}`}
      aria-label="Clips being added"
    >
      {open ? (
        <>
          <header className="rs-tray-head">
            <span className="rs-display">{summary.title}</span>
            {uploading.length ? (
              <button
                type="button"
                className="rs-tray-text-button"
                onClick={paused ? resumeAll : pauseAll}
              >
                {paused ? "Resume" : "Pause"}
              </button>
            ) : null}
            <button
              type="button"
              className="rs-tray-icon"
              aria-label="Collapse"
              onClick={() => setOpen(false)}
            >
              <IconChevronDown size={16} />
            </button>
          </header>
          <div className="rs-tray-books">
            {books.map((categoryId) => (
              <TrayBook
                key={categoryId}
                category={categoryById.get(categoryId)}
                lines={lines.filter(
                  (line) => line.item.categoryId === categoryId,
                )}
                onRetry={retry}
              />
            ))}
          </div>
          <footer className="rs-tray-foot">
            {uploading.length
              ? "Keep this tab open until uploads finish. Processing carries on without it."
              : "Uploads are done; you can close this tab."}
            {finished ? (
              <button
                type="button"
                className="rs-tray-text-button"
                onClick={clearFinished}
              >
                Clear
              </button>
            ) : null}
          </footer>
        </>
      ) : (
        <div className="rs-tray-pill">
          <button
            type="button"
            className="rs-tray-summary"
            onClick={() => setOpen(true)}
            aria-expanded="false"
          >
            <Ring value={summary.value} tone={summary.tone} />
            <span className="rs-tray-title">{summary.title}</span>
            <span className="rs-meta">{summary.meta}</span>
            <IconChevronUp size={16} aria-hidden="true" />
          </button>
          {finished ? (
            <button
              type="button"
              className="rs-tray-icon"
              aria-label="Dismiss"
              onClick={clearFinished}
            >
              <IconX size={16} />
            </button>
          ) : null}
        </div>
      )}
    </aside>
  );
}

function TrayBook({
  category,
  lines,
  onRetry,
}: {
  category: GameCategoryResponse | undefined;
  lines: Line[];
  onRetry: (id: string) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  // Most interesting first: running uploads, then processing, then waiting, then done.
  const order = (line: Line) =>
    !line.stage
      ? 0
      : line.stage.kind === "uploading" && line.item.status !== "queued"
        ? 1
        : line.stage.kind === "processing"
          ? 2
          : line.stage.kind === "uploading"
            ? 3
            : 4;
  const sorted = [...lines].sort((a, b) => order(a) - order(b));
  const shown = expanded ? sorted : sorted.slice(0, SHOWN_PER_BOOK);
  const done = lines.filter((line) => line.stage?.kind === "ready").length;

  return (
    <section className="rs-tray-book">
      <div className="rs-tray-book-head">
        <i
          style={{
            background: category ? bookBinding(category).cloth : undefined,
          }}
        />
        {category ? (
          <Link to="/games/$slug" params={{ slug: category.slug }}>
            {category.name}
          </Link>
        ) : (
          <span>A book</span>
        )}
        <span className="rs-meta">
          {done} of {lines.length} on the shelf
        </span>
      </div>
      {shown.map((line) => (
        <div
          key={line.item.id}
          className={`rs-tray-line ${line.stage?.kind ?? "failed"}`}
        >
          <span className="rs-tray-line-title">{line.item.title}</span>
          <span className="rs-tray-bar">
            <span style={{ width: `${lineProgress(line)}%` }} />
          </span>
          <span className="rs-tray-line-status">
            {lineLabel(line)}
            {line.item.status === "error" ? (
              <>
                {" · "}
                <button type="button" onClick={() => onRetry(line.item.id)}>
                  Retry
                </button>
              </>
            ) : null}
          </span>
        </div>
      ))}
      {sorted.length > SHOWN_PER_BOOK ? (
        <button
          type="button"
          className="rs-tray-more"
          onClick={() => setExpanded((value) => !value)}
        >
          {expanded ? "Show fewer" : `${sorted.length - SHOWN_PER_BOOK} more`}
        </button>
      ) : null}
    </section>
  );
}
