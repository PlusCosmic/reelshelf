import { Link } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { IconPlayerPlayFilled } from "@tabler/icons-react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import {
  bookBinding,
  categoryForClip,
  formatDate,
  formatDuration,
  formatSize,
  thumbnailUrl,
} from "../reelshelf-model";
import { StitchedDot } from "../StitchedDot";
import { SharedIndicator } from "./Navigation";

/** What a link to a clip's page can carry: the collection it is being played through, if any. */
export type ClipLinkSearch = { collection?: string };

export function ClipThumb({
  clip,
  category,
  compact = false,
  onClick,
}: {
  clip: Clip;
  category?: GameCategoryResponse | null;
  compact?: boolean;
  onClick?: () => void;
}) {
  const firstTag = clip.tags[0];
  const Tag = onClick ? "button" : "span";
  return (
    <Tag
      {...(onClick ? { type: "button" as const } : {})}
      className="rs-thumb"
      onClick={onClick}
      // The game's cloth shows while the thumbnail loads, or instead of one that's missing.
      style={
        category
          ? ({ background: bookBinding(category).cloth } as CSSProperties)
          : undefined
      }
      aria-label={onClick ? `Open ${clip.video.title}` : undefined}
    >
      <img
        src={thumbnailUrl(clip)}
        alt=""
        loading="lazy"
        onError={(event) => {
          event.currentTarget.style.display = "none";
        }}
      />
      <div className="rs-play-reticle">
        <span>
          <IconPlayerPlayFilled size={14} />
        </span>
      </div>
      {!compact && firstTag ? (
        <span className="rs-tag-badge">{firstTag}</span>
      ) : null}
      {compact && clip.share.shared ? <SharedIndicator compact /> : null}
      {clip.video.length > 0 ? (
        <span className="rs-duration">{formatDuration(clip.video.length)}</span>
      ) : null}
    </Tag>
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
        <span>{category?.name ?? clip.video.category ?? "Uncategorized"}</span>
        <span className="rs-dot" />
        <span>{formatDate(clip.createdAt)}</span>
        <span className="rs-dot" />
        <span>{formatSize(clip.video.storageSize)}</span>
        {!clip.isViewed ? (
          <>
            <span className="rs-dot" />
            <span>New</span>
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
  | { kind: "month"; key: string; month: string; year: number }
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
      });
      lastKey = key;
    }
    items.push({ kind: "clip", clip });
  }
  return items;
}

function ClipRow({
  clip,
  category,
  showGame,
  current,
  search,
}: {
  clip: Clip;
  category: GameCategoryResponse | undefined;
  showGame: boolean;
  current: boolean;
  search: ClipLinkSearch | undefined;
}) {
  return (
    <Link
      className={`rs-row${showGame ? "" : " no-game"}${current ? " current" : ""}`}
      to="/games/$slug/$clipId"
      params={{ slug: clip.categorySlug, clipId: clip.clipId }}
      search={search}
      aria-current={current ? "page" : undefined}
    >
      <ClipThumb clip={clip} category={category} compact />
      <span className="rs-row-body">
        {current ? <span className="rs-row-now">Now playing</span> : null}
        <span className="rs-row-heading">
          <strong className="rs-row-title">{clip.video.title}</strong>
          {!clip.isViewed ? (
            <>
              <StitchedDot className="rs-row-new" />
              <span className="rs-visually-hidden">Not watched yet</span>
            </>
          ) : null}
        </span>
        <span className="rs-meta">
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
          {category?.name ?? "Game"}
        </span>
      ) : null}
      <span className="wide-only rs-meta rs-row-duration">
        {formatDuration(clip.video.length)}
      </span>
    </Link>
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
}) {
  if (clips.length === 0) {
    return <div className="rs-empty">No clips match this view.</div>;
  }

  if (variant === "filmstrip") {
    return (
      <div className="rs-filmstrip">
        {filmstripItems(clips, groupByMonth).map((item) =>
          item.kind === "month" ? (
            <h3 className="rs-month" key={item.key}>
              <span className="rs-display">{item.month}</span>
              <span className="rs-month-year">{item.year}</span>
            </h3>
          ) : (
            <ClipRow
              key={item.clip.clipId}
              clip={item.clip}
              category={categoryForClip(item.clip, categories)}
              showGame={showGame}
              current={item.clip.clipId === currentClipId}
              search={linkSearch}
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
