import { Link } from "@tanstack/react-router";
import { useState } from "react";
import type { CSSProperties } from "react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import {
  IconChevronLeft,
  IconClock,
  IconDownload,
  IconEye,
  IconFolderPlus,
  IconPlayerPlayFilled,
  IconPlus,
  IconSearch,
  IconShare3,
} from "@tabler/icons-react";
import {
  categoryForClip,
  formatDate,
  formatDuration,
  formatSize,
  getGameColors,
  thumbnailUrl,
} from "./reelshelf-model";

export function Avatar({
  name = "You",
  src,
  size = 28,
}: {
  name?: string | null;
  src?: string | null;
  size?: number;
}) {
  const [imageFailed, setImageFailed] = useState(false);
  const initials = (name ?? "You")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
  return (
    <span
      className="rs-avatar"
      style={{ "--size": `${size}px` } as CSSProperties}
    >
      {src && !imageFailed ? (
        <img
          src={src}
          alt={name ?? "User avatar"}
          onError={() => setImageFailed(true)}
        />
      ) : (
        initials || "Y"
      )}
    </span>
  );
}

export function Chip({
  children,
  active,
  onClick,
}: {
  children: React.ReactNode;
  active?: boolean;
  onClick?: () => void;
}) {
  return (
    <button
      className={`rs-chip${active ? " active" : ""}`}
      type="button"
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export function SearchBox({
  value,
  onChange,
  placeholder = "Search clips, games, tags...",
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <label className="rs-search">
      <IconSearch size={15} />
      <input
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        placeholder={placeholder}
      />
    </label>
  );
}

export function GameVars({
  category,
  children,
}: {
  category?: GameCategoryResponse | null;
  children: React.ReactNode;
}) {
  const [colorA, colorB] = getGameColors(category?.id ?? "fallback");
  return (
    <div style={{ "--game-a": colorA, "--game-b": colorB } as CSSProperties}>
      {children}
    </div>
  );
}

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
  if (clips.length === 0)
    return <div className="rs-empty">No clips match this view.</div>;

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
              <span
                className="rs-display"
                style={{ color: "var(--fg-soft)", fontSize: 25 }}
              >
                {String(index + 1).padStart(2, "0")}
              </span>
              <ClipThumb clip={clip} category={category} compact />
              <span>
                <strong style={{ display: "block", fontSize: 14 }}>
                  {clip.video.title}
                </strong>
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

export function PlayerActions() {
  return (
    <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
      <button className="rs-small-button" type="button">
        <IconShare3 size={13} />
        Share
      </button>
      <button className="rs-small-button" type="button">
        <IconFolderPlus size={13} />
        Add to collection
      </button>
      <button className="rs-small-button" type="button">
        <IconDownload size={13} />
        Download
      </button>
    </div>
  );
}

export function BackToLibrary({ to = "/" }: { to?: string }) {
  return (
    <Link to={to} className="rs-small-button" style={{ marginBottom: 16 }}>
      <IconChevronLeft size={14} />
      Library
    </Link>
  );
}

export function StatLine({ clips }: { clips: Clip[] }) {
  const totalSeconds = clips.reduce(
    (sum, clip) => sum + (clip.video.length || 0),
    0,
  );
  const totalBytes = clips.reduce(
    (sum, clip) => sum + (clip.video.storageSize || 0),
    0,
  );
  return (
    <span>
      {clips.length} clips - {formatDuration(totalSeconds)} -{" "}
      {formatSize(totalBytes)}
    </span>
  );
}

export const Icons = {
  eye: IconEye,
  clock: IconClock,
  plus: IconPlus,
};
