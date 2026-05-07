import { Link } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { IconPlayerPlayFilled } from "@tabler/icons-react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import {
  categoryForClip,
  formatDate,
  formatDuration,
  formatSize,
  getGameColors,
  thumbnailUrl,
} from "../reelshelf-model";

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
  const [colorA, colorB] = getGameColors(category?.id ?? clip.gameCategoryId);
  const firstTag = clip.tags[0];
  return (
    <button
      type="button"
      className="rs-thumb"
      onClick={onClick}
      style={{ "--game-a": colorA, "--game-b": colorB } as CSSProperties}
      aria-label={`Open ${clip.video.title}`}
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
      {clip.video.length > 0 ? (
        <span className="rs-duration">{formatDuration(clip.video.length)}</span>
      ) : null}
    </button>
  );
}

export function ClipCard({
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
      </div>
    </article>
  );
}

export function ClipGrid({
  clips,
  categories,
  variant = "poster",
}: {
  clips: Clip[];
  categories: GameCategoryResponse[];
  variant?: "poster" | "grid" | "filmstrip";
}) {
  if (clips.length === 0) {
    return <div className="rs-empty">No clips match this view.</div>;
  }

  if (variant === "filmstrip") {
    return (
      <div className="rs-filmstrip">
        {clips.map((clip, index) => {
          const category = categoryForClip(clip, categories);
          return (
            <Link
              className="rs-row"
              key={clip.clipId}
              to="/games/$slug/$clipId"
              params={{ slug: clip.categorySlug, clipId: clip.clipId }}
            >
              <span className="rs-display rs-row-index">
                {String(index + 1).padStart(2, "0")}
              </span>
              <ClipThumb clip={clip} category={category} compact />
              <span>
                <strong className="rs-row-title">{clip.video.title}</strong>
                <span className="rs-meta">
                  {clip.tags
                    .slice(0, 3)
                    .map((tag) => `#${tag}`)
                    .join(" ")}
                </span>
              </span>
              <span className="wide-only rs-meta">
                {category?.name ?? "Game"}
              </span>
              <span className="wide-only rs-meta">
                {formatDuration(clip.video.length)}
              </span>
            </Link>
          );
        })}
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
