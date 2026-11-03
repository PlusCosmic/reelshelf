import { Link } from "@tanstack/react-router";
import type { CSSProperties, MouseEvent, PointerEvent } from "react";
import { useEffect, useRef } from "react";
import { IconCheck, IconPlayerPlayFilled } from "@tabler/icons-react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import {
  clipStageFor,
  clipStageLabel,
  type ClipStage,
} from "@/components/AddClips/addClipsModel";
import { useUploadForClip } from "@/components/AddClips/UploadsProvider";
import { binCopy } from "@/copy/bin";
import { libraryCopy } from "@/copy/library";
import {
  bookBinding,
  categoryForClip,
  formatDate,
  formatDuration,
  legendPortraitUrl,
  thumbnailUrl,
} from "../reelshelf-model";
import { isChosen, type ClipSelection } from "../clipSelection";
import type { ClipListSearch } from "../libraryList";
import { StitchedDot } from "../StitchedDot";
import { SharedIndicator } from "./Navigation";

/**
 * What a link to a clip's page can carry: the collection it is being played through, or the library list it was
 * opened from (`ClipListSearch`).
 */
export type ClipLinkSearch = { collection?: string } & ClipListSearch;

export function ClipThumb({
  clip,
  category,
  compact = false,
  onClick,
  stage,
}: {
  clip: Clip;
  category?: GameCategoryResponse | null;
  compact?: boolean;
  onClick?: () => void;
  /** A clip still on its way in: shown dimmed with its progress along the bottom. */
  stage?: ClipStage;
}) {
  const arriving =
    stage && (stage.kind === "uploading" || stage.kind === "processing");
  const firstTag = clip.tags[0];
  const Tag = onClick ? "button" : "span";
  return (
    <Tag
      {...(onClick ? { type: "button" as const } : {})}
      className={`rs-thumb${arriving ? " arriving" : ""}`}
      onClick={onClick}
      // The game's cloth shows while the thumbnail loads, or instead of one that's missing.
      style={
        category
          ? ({ background: bookBinding(category).cloth } as CSSProperties)
          : undefined
      }
      aria-label={onClick ? libraryCopy.clip.open(clip.video.title) : undefined}
    >
      {thumbnailUrl(clip) ? (
        <img
          // Keyed by URL: a thumbnail that arrives after the clip was first listed gets a fresh element,
          // not one an earlier failure hid.
          key={thumbnailUrl(clip)}
          src={thumbnailUrl(clip)!}
          alt=""
          loading="lazy"
          onError={(event) => {
            event.currentTarget.style.display = "none";
          }}
        />
      ) : null}
      <div className="rs-play-reticle">
        <span>
          <IconPlayerPlayFilled size={14} />
        </span>
      </div>
      {!compact && firstTag ? (
        <span className="rs-tag-badge">{firstTag}</span>
      ) : null}
      {compact && clip.share.shared ? <SharedIndicator compact /> : null}
      {clip.video.durationSeconds > 0 ? (
        <span className="rs-duration">
          {formatDuration(clip.video.durationSeconds)}
        </span>
      ) : null}
      {arriving ? (
        <span className={`rs-thumb-progress ${stage.kind}`} aria-hidden="true">
          <span style={{ width: `${stage.progress ?? 0}%` }} />
        </span>
      ) : null}
    </Tag>
  );
}

/**
 * An Apex legend's portrait as a small 2:1 plate, underlined in the game's cloth. The legend's name always goes
 * beside it, so the picture itself is decoration.
 */
export function LegendPlate({
  legend,
  cloth,
  size = "medium",
}: {
  legend: string;
  cloth?: string;
  size?: "small" | "medium" | "large" | "tile";
}) {
  return (
    <span
      className={`rs-legend-plate ${size}`}
      style={cloth ? ({ "--cloth": cloth } as CSSProperties) : undefined}
      aria-hidden="true"
    >
      <img
        src={legendPortraitUrl(legend)}
        alt=""
        loading="lazy"
        onError={(event) => {
          event.currentTarget.style.display = "none";
        }}
      />
    </span>
  );
}

function ClipCard({
  clip,
  categories,
  compact = false,
}: {
  clip: Clip;
  categories: GameCategoryResponse[];
  compact?: boolean;
}) {
  const category = categoryForClip(clip, categories);
  return (
    <article className="rs-card">
      <Link
        to="/games/$slug/$clipId"
        params={{ slug: clip.categorySlug, clipId: clip.clipId }}
      >
        <ClipThumb clip={clip} category={category} compact={compact} />
      </Link>
      <Link
        to="/games/$slug/$clipId"
        params={{ slug: clip.categorySlug, clipId: clip.clipId }}
      >
        <h3 className="rs-card-title">{clip.video.title}</h3>
      </Link>
      <div className="rs-meta">
        <span>
          {category?.name ??
            clip.categorySlug ??
            libraryCopy.clip.uncategorised}
        </span>
        <span className="rs-dot" />
        <span>{formatDate(clip.createdAt)}</span>
        {!clip.isViewed ? (
          <>
            <span className="rs-dot" />
            <span>{libraryCopy.clip.new}</span>
          </>
        ) : null}
        {clip.share.shared ? (
          <>
            <span className="rs-dot" />
            <SharedIndicator />
          </>
        ) : null}
      </div>
    </article>
  );
}

const monthFormatter = new Intl.DateTimeFormat(undefined, { month: "long" });

type FilmstripItem =
  | {
      kind: "month";
      key: string;
      month: string;
      year: number;
      clipIds: string[];
    }
  | { kind: "clip"; clip: Clip };

/** Interleaves a month heading wherever the recording month changes. */
function filmstripItems(clips: Clip[], groupByMonth: boolean): FilmstripItem[] {
  if (!groupByMonth) return clips.map((clip) => ({ kind: "clip", clip }));

  const items: FilmstripItem[] = [];
  let lastKey = "";
  for (const clip of clips) {
    const recorded = new Date(clip.createdAt);
    const key = `${recorded.getFullYear()}-${recorded.getMonth()}`;
    if (key !== lastKey) {
      items.push({
        kind: "month",
        key,
        month: monthFormatter.format(recorded),
        year: recorded.getFullYear(),
        clipIds: [],
      });
      lastKey = key;
    }
    const month = items.findLast((item) => item.kind === "month");
    if (month?.kind === "month") month.clipIds.push(clip.clipId);
    items.push({ kind: "clip", clip });
  }
  return items;
}

/**
 * Choosing clips in a filmstrip: the list's selection, and what toggling a row, a run of rows or a month does to it.
 * Dragging from one row's check over others sets them all the way the first went.
 */
export type ClipListChoosing = {
  selection: ClipSelection;
  onToggle: (clipId: string, range: boolean) => void;
  onSet: (clipIds: string[], selected: boolean) => void;
};

/** A drag across the rows' checks, choosing (or clearing) each row it passes. */
type Paint = { selected: boolean } | null;

function ClipRow({
  clip,
  category,
  showGame,
  showLegend,
  current,
  search,
  choosing,
  paint,
  fromList,
}: {
  clip: Clip;
  category: GameCategoryResponse | undefined;
  showGame: boolean;
  /** The legend column, kept even for a clip whose legend isn't known so the columns line up. */
  showLegend: boolean;
  current: boolean;
  search: ClipLinkSearch | undefined;
  choosing?: ClipListChoosing;
  paint?: { current: Paint };
  fromList?: boolean;
}) {
  const stage = clipStageFor(clip, useUploadForClip(clip.clipId));
  const cloth = category ? bookBinding(category).cloth : undefined;
  const legend = showLegend ? clip.legend : null;
  const row = (
    <Link
      className={`rs-row${showGame ? "" : " no-game"}${showLegend ? " has-legend" : ""}${current ? " current" : ""}`}
      to="/games/$slug/$clipId"
      params={{ slug: clip.categorySlug, clipId: clip.clipId }}
      search={search}
      // Back from the clip returns to this list where it was, rather than opening it afresh at the top.
      state={fromList ? { fromList: true } : undefined}
      aria-current={current ? "page" : undefined}
      onClick={
        choosing?.selection.active
          ? (event: MouseEvent) => {
              // While choosing, a row toggles; a modified click still opens the clip in a new tab.
              if (event.metaKey || event.ctrlKey || event.button !== 0) return;
              event.preventDefault();
              choosing.onToggle(clip.clipId, event.shiftKey);
            }
          : undefined
      }
    >
      <ClipThumb clip={clip} category={category} compact stage={stage} />
      <span className="rs-row-body">
        {current ? (
          <span className="rs-row-now">{libraryCopy.clip.nowPlaying}</span>
        ) : null}
        <span className="rs-row-heading">
          <strong className="rs-row-title">{clip.video.title}</strong>
          {!clip.isViewed ? (
            <>
              <StitchedDot className="rs-row-new" />
              <span className="rs-visually-hidden">
                {libraryCopy.clip.notWatched}
              </span>
            </>
          ) : null}
        </span>
        {stage.kind !== "ready" ? (
          <span className={`rs-row-stage ${stage.kind}`}>
            {clipStageLabel(stage)}
          </span>
        ) : null}
        <span className="rs-meta">
          {/* Where there's no room for the legend column (beside the player, or on a phone), the legend
              leads the meta line instead. */}
          {legend ? (
            <span className="rs-row-legend-inline">
              <LegendPlate legend={legend} cloth={cloth} size="small" />
              {legend}
            </span>
          ) : null}
          {clip.tags
            .slice(0, 3)
            .map((tag) => `#${tag}`)
            .join(" ")}
          {clip.share.shared ? (
            <span className="rs-row-shared">
              <SharedIndicator />
            </span>
          ) : null}
        </span>
      </span>
      {showGame ? (
        <span className="wide-only rs-row-game">
          {category?.coverUrl ? (
            <img src={category.coverUrl} alt="" loading="lazy" />
          ) : null}
          {category?.name ?? libraryCopy.clip.game}
        </span>
      ) : null}
      {showLegend ? (
        <span className="wide-only rs-row-legend">
          {legend ? (
            <>
              <LegendPlate legend={legend} cloth={cloth} />
              <span className="rs-visually-hidden">
                {libraryCopy.clip.playedAs}
              </span>
              {legend}
            </>
          ) : null}
        </span>
      ) : null}
      <span className="wide-only rs-meta rs-row-duration">
        {formatDuration(clip.video.durationSeconds)}
      </span>
    </Link>
  );
  if (!choosing) return row;

  const chosen = isChosen(choosing.selection, clip.clipId);
  return (
    <div
      className={`rs-row-select${chosen ? " chosen" : ""}${choosing.selection.active ? " choosing" : ""}`}
      data-clip-id={clip.clipId}
      onPointerEnter={(event: PointerEvent) => {
        if (paint?.current && event.buttons === 1)
          choosing.onSet([clip.clipId], paint.current.selected);
      }}
    >
      <button
        type="button"
        role="checkbox"
        aria-checked={chosen}
        className="rs-row-check"
        aria-label={binCopy.selection.toggle(clip.video.title)}
        onPointerDown={(event: PointerEvent) => {
          if (event.button !== 0 || event.shiftKey || !paint) return;
          // Pressing starts a drag; the press itself toggles this row.
          paint.current = { selected: !chosen };
          choosing.onSet([clip.clipId], !chosen);
        }}
        onClick={(event: MouseEvent) => {
          // The pointer already toggled it on press; keys and shift-clicks come through here.
          if (event.shiftKey) choosing.onToggle(clip.clipId, true);
          else if (event.detail === 0) choosing.onToggle(clip.clipId, false);
        }}
      >
        <IconCheck size={14} aria-hidden="true" />
      </button>
      {row}
    </div>
  );
}

export function ClipGrid({
  clips,
  categories,
  variant = "poster",
  groupByMonth = false,
  showGame = true,
  currentClipId,
  linkSearch,
  choosing,
  fromList,
}: {
  clips: Clip[];
  categories: GameCategoryResponse[];
  variant?: "poster" | "grid" | "filmstrip";
  /** Filmstrip only: a heading before each month's clips, for lists in date order. */
  groupByMonth?: boolean;
  /** Filmstrip only: the game column, which a single game's list does not need. */
  showGame?: boolean;
  /** Filmstrip only: the clip being watched, marked in the list. */
  currentClipId?: string;
  /** Filmstrip only: carried on each clip's link, such as the collection being played through. */
  linkSearch?: ClipLinkSearch;
  /** Filmstrip only: rows can be chosen, to bin together. */
  choosing?: ClipListChoosing;
  /** Filmstrip only: this is a library list, which a clip opened from it goes back to. */
  fromList?: boolean;
}) {
  const paint = useRef<Paint>(null);
  useEffect(() => {
    if (!choosing) return;
    const end = () => {
      paint.current = null;
    };
    window.addEventListener("pointerup", end);
    window.addEventListener("pointercancel", end);
    return () => {
      window.removeEventListener("pointerup", end);
      window.removeEventListener("pointercancel", end);
    };
  }, [choosing]);

  if (clips.length === 0) {
    return <div className="rs-empty">{libraryCopy.list.empty}</div>;
  }

  if (variant === "filmstrip") {
    // The legend is only shown in one game's list (the Apex book, or more from it beside the player), never
    // beside the game column of a mixed list.
    const showLegend = !showGame && clips.some((clip) => clip.legend);
    return (
      <div className="rs-filmstrip">
        {filmstripItems(clips, groupByMonth).map((item) =>
          item.kind === "month" ? (
            <h3 className="rs-month" key={item.key}>
              {choosing ? (
                <MonthCheck
                  label={binCopy.selection.month(`${item.month} ${item.year}`)}
                  clipIds={item.clipIds}
                  choosing={choosing}
                />
              ) : null}
              <span className="rs-display">{item.month}</span>
              <span className="rs-month-year">{item.year}</span>
            </h3>
          ) : (
            <ClipRow
              key={item.clip.clipId}
              clip={item.clip}
              category={categoryForClip(item.clip, categories)}
              showGame={showGame}
              showLegend={showLegend}
              current={item.clip.clipId === currentClipId}
              search={linkSearch}
              choosing={choosing}
              paint={paint}
              fromList={fromList}
            />
          ),
        )}
      </div>
    );
  }

  return (
    <div className={variant === "grid" ? "rs-compact-grid" : "rs-grid"}>
      {clips.map((clip) => (
        <ClipCard
          key={clip.clipId}
          clip={clip}
          categories={categories}
          compact={variant === "grid"}
        />
      ))}
    </div>
  );
}

/** Chooses (or clears) every loaded clip from one month. */
function MonthCheck({
  label,
  clipIds,
  choosing,
}: {
  label: string;
  clipIds: string[];
  choosing: ClipListChoosing;
}) {
  const all = clipIds.every((clipId) => isChosen(choosing.selection, clipId));
  const some =
    !all && clipIds.some((clipId) => isChosen(choosing.selection, clipId));
  return (
    <button
      type="button"
      role="checkbox"
      aria-checked={all ? true : some ? "mixed" : false}
      className={`rs-row-check rs-month-check${all ? " chosen" : some ? " partly" : ""}`}
      aria-label={label}
      onClick={() => choosing.onSet(clipIds, !all)}
    >
      <IconCheck size={14} aria-hidden="true" />
    </button>
  );
}
