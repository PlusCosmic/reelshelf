import { Link } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  IconChevronLeft,
  IconChevronRight,
  IconLayoutSidebarRight,
  IconRectangle,
} from "@tabler/icons-react";
import type { Clip, GameCategoryResponse } from "@/api-client";
import {
  useClipNeighbours,
  useClipSession,
  useClipsInfinite,
} from "@/hooks/clips.queries";
import { useClip, useMarkAsViewed } from "@/hooks/queries";
import {
  ClipGrid,
  ClipThumb,
  PlayerActions,
  SharedIndicator,
} from "./ReelshelfPrimitives";
import {
  bookBinding,
  formatDate,
  formatDuration,
  formatSize,
  makeGameShelf,
  playerUrl,
  thumbnailUrl,
  type GameShelfItem,
} from "./reelshelf-model";
import { StitchedDot } from "./StitchedDot";
import { useLibraryData } from "./useLibraryData";

const THEATRE_KEY = "reelshelf.theatre";

const timeFormatter = new Intl.DateTimeFormat(undefined, {
  hour: "numeric",
  minute: "2-digit",
});
const dayFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: "long",
  month: "short",
  day: "numeric",
});

function formatTime(date: Date) {
  return timeFormatter.format(new Date(date));
}

function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}

/** Theatre mode is a viewing preference, so it is remembered in this browser and nowhere else. */
function useTheatreMode() {
  const [theatre, setTheatre] = useState(() => {
    try {
      return window.localStorage.getItem(THEATRE_KEY) === "on";
    } catch {
      return false;
    }
  });
  const toggle = () =>
    setTheatre((value) => {
      try {
        window.localStorage.setItem(THEATRE_KEY, value ? "off" : "on");
      } catch {
        // Unremembered is fine; the toggle still works for this visit.
      }
      return !value;
    });
  return [theatre, toggle] as const;
}

function GameStrip({
  game,
  slug,
  theatre,
  onToggleTheatre,
}: {
  game: GameShelfItem | undefined;
  slug: string;
  theatre: boolean;
  onToggleTheatre: () => void;
}) {
  return (
    <nav className="rs-watch-strip" aria-label="Where this clip lives">
      <Link to="/games/$slug" params={{ slug }} className="rs-watch-back">
        <IconChevronLeft size={18} aria-hidden="true" />
        {game?.coverUrl ? <img src={game.coverUrl} alt="" /> : null}
        <span>
          <small>Back to</small>
          <span className="rs-display">{game?.name ?? "the shelf"}</span>
        </span>
      </Link>
      {game ? (
        <span className="rs-watch-counts">
          {plural(game.clipCount, "clip", "clips")}
          {game.unviewedCount > 0 ? (
            <strong>
              <StitchedDot />
              {game.unviewedCount} new
            </strong>
          ) : null}
        </span>
      ) : null}
      <button
        type="button"
        className="rs-watch-theatre"
        aria-pressed={theatre}
        onClick={onToggleTheatre}
      >
        {theatre ? (
          <IconRectangle size={18} aria-hidden="true" />
        ) : (
          <IconLayoutSidebarRight size={18} aria-hidden="true" />
        )}
        Theatre mode
      </button>
    </nav>
  );
}

function NeighbourLink({
  clip,
  direction,
}: {
  clip: Clip | null | undefined;
  direction: "older" | "newer";
}) {
  const label = direction === "older" ? "Older" : "Newer";
  if (!clip)
    return (
      <span
        className={`rs-watch-neighbour ${direction} none`}
        aria-hidden="true"
      />
    );
  return (
    <Link
      to="/games/$slug/$clipId"
      params={{ slug: clip.categorySlug, clipId: clip.clipId }}
      className={`rs-watch-neighbour ${direction}`}
      aria-label={`${label} clip: ${clip.video.title}`}
    >
      {direction === "older" ? (
        <IconChevronLeft size={20} aria-hidden="true" />
      ) : null}
      <img src={thumbnailUrl(clip)} alt="" loading="lazy" />
      <span>
        <small>{label}</small>
        {formatDate(clip.createdAt).replace(/,? \d{4}$/, "")},{" "}
        {formatTime(clip.createdAt)}
      </span>
      {direction === "newer" ? (
        <IconChevronRight size={20} aria-hidden="true" />
      ) : null}
    </Link>
  );
}

function SameSession({
  session,
  current,
  category,
}: {
  session: Clip[];
  current: Clip;
  category: GameCategoryResponse | undefined;
}) {
  const first = session[0];
  const last = session[session.length - 1];
  const cardsRef = useRef<HTMLDivElement>(null);

  // A long night runs off the side; bring the clip being watched into view within the strip,
  // without moving the page.
  useEffect(() => {
    const cards = cardsRef.current;
    const card = cards?.querySelector<HTMLElement>('[aria-current="page"]');
    if (!cards || !card) return;
    cards.scrollLeft =
      card.offsetLeft - (cards.clientWidth - card.offsetWidth) / 2;
  }, [current.clipId, session]);

  return (
    <section className="rs-watch-session" aria-labelledby="rs-session-heading">
      <div className="rs-watch-section-head">
        <h2 id="rs-session-heading" className="rs-display">
          Same session
        </h2>
        <span>
          {dayFormatter.format(new Date(current.createdAt))} ·{" "}
          {plural(session.length, "clip", "clips")},{" "}
          {formatTime(first.createdAt)} to {formatTime(last.createdAt)}
        </span>
      </div>
      <div className="rs-session-cards" ref={cardsRef}>
        {session.map((clip) => {
          const isCurrent = clip.clipId === current.clipId;
          return (
            <Link
              key={clip.clipId}
              to="/games/$slug/$clipId"
              params={{ slug: clip.categorySlug, clipId: clip.clipId }}
              className="rs-session-card"
              aria-current={isCurrent ? "page" : undefined}
            >
              <ClipThumb clip={clip} category={category} compact />
              <span className="rs-session-card-top">
                <span className="rs-display">{formatTime(clip.createdAt)}</span>
                {isCurrent ? (
                  <span className="rs-session-card-now">Now playing</span>
                ) : clip.isViewed ? (
                  <span>Watched</span>
                ) : (
                  <span>
                    <StitchedDot />
                    New
                  </span>
                )}
              </span>
              <span className="rs-session-card-title">{clip.video.title}</span>
            </Link>
          );
        })}
      </div>
    </section>
  );
}

function ClipFacts({
  clip,
  game,
}: {
  clip: Clip;
  game: GameShelfItem | undefined;
}) {
  return (
    <aside className="rs-watch-facts" aria-label="Clip details">
      <dl>
        <dt>Game</dt>
        <dd>
          <Link
            to="/games/$slug"
            params={{ slug: clip.categorySlug }}
            className="rs-watch-facts-game"
          >
            {game?.coverUrl ? <img src={game.coverUrl} alt="" /> : null}
            {game?.name ?? clip.video.category}
          </Link>
        </dd>
        <dt>Recorded</dt>
        <dd>
          {formatDate(clip.createdAt)}, {formatTime(clip.createdAt)}
        </dd>
        <dt>Length</dt>
        <dd>{formatDuration(clip.video.length)}</dd>
        <dt>Size</dt>
        <dd>{formatSize(clip.video.storageSize)}</dd>
        <dt>Watched</dt>
        <dd>{clip.isViewed ? "Yes" : "Not yet"}</dd>
        <dt>Sharing</dt>
        <dd>{clip.share.shared ? "Anyone with the link" : "Only you"}</dd>
      </dl>
    </aside>
  );
}

/**
 * One clip: the player, what it is, the rest of that night's session, and more from its game.
 * Theatre mode widens the player onto a stage in the game's cloth and moves everything else below.
 * Both layouts are the same elements rearranged by CSS, so switching never reloads the video.
 */
export function ClipPlayerPage({
  slug,
  clipId,
}: {
  slug: string;
  clipId: string;
}) {
  const { data: clip, isLoading, isError } = useClip(clipId);
  const { categories, categoryTotals } = useLibraryData();
  const markViewed = useMarkAsViewed();
  const [theatre, toggleTheatre] = useTheatreMode();
  const shelf = useMemo(
    () => makeGameShelf(categories, categoryTotals),
    [categories, categoryTotals],
  );
  const game = shelf.find((item) =>
    clip ? item.id === clip.gameCategoryId : item.slug === slug,
  );
  const { data: session = [] } = useClipSession(clip);
  const { data: neighbours } = useClipNeighbours(clip);
  // The first page of the game's clips is plenty for "more from", and it shares its cache with the
  // game's own list.
  const { data: gameClips } = useClipsInfinite(
    { categoryId: clip?.gameCategoryId },
    !!clip,
  );
  const more = useMemo(() => {
    const inSession = new Set(session.map((item) => item.clipId));
    return (gameClips?.pages[0]?.clips ?? []).filter(
      (item) => item.clipId !== clipId && !inSession.has(item.clipId),
    );
  }, [gameClips, session, clipId]);

  if (isLoading)
    return <div className="rs-section rs-empty">Loading player…</div>;
  if (isError || !clip)
    return (
      <div className="rs-section rs-empty">This clip could not be found.</div>
    );

  const { cloth } = game ? bookBinding(game) : { cloth: "var(--accent)" };
  const gameName = game?.name ?? clip.video.category;

  return (
    <div
      className={`rs-watch${theatre ? " theatre" : ""}`}
      style={
        {
          "--cloth": cloth,
          "--game-a": cloth,
          "--game-b": `color-mix(in oklab, ${cloth} 40%, black)`,
        } as CSSProperties
      }
    >
      <GameStrip
        game={game}
        slug={clip.categorySlug}
        theatre={theatre}
        onToggleTheatre={toggleTheatre}
      />

      <div className="rs-watch-layout">
        <section className="rs-watch-stage" aria-label="Player">
          {game?.keyArtUrl ? (
            <img className="rs-watch-stage-art" src={game.keyArtUrl} alt="" />
          ) : null}
          <NeighbourLink clip={neighbours?.older} direction="older" />
          {/* The thumbnail sits behind the embed so loading looks like the video arriving; the
              cloth gradient underneath covers a missing thumbnail. */}
          <div
            className="rs-player"
            style={{
              backgroundImage: `url("${thumbnailUrl(clip)}"), linear-gradient(135deg, var(--game-a), var(--game-b))`,
            }}
          >
            <iframe
              src={playerUrl(clip)}
              loading="lazy"
              title={clip.video.title}
              className="rs-player-frame"
              allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture; fullscreen;"
              allowFullScreen
              onLoad={() => {
                if (!clip.isViewed && !markViewed.isPending) {
                  markViewed.mutate(clipId);
                }
              }}
            />
          </div>
          <NeighbourLink clip={neighbours?.newer} direction="newer" />
        </section>

        <section className="rs-watch-details">
          <h1 className="rs-display rs-watch-title">{clip.video.title}</h1>
          <div className="rs-watch-meta">
            <span>
              {formatDate(clip.createdAt)}, {formatTime(clip.createdAt)}
            </span>
            <span>{formatDuration(clip.video.length)}</span>
            <span>{formatSize(clip.video.storageSize)}</span>
            {clip.isViewed ? null : (
              <strong>
                <StitchedDot />
                Not watched yet
              </strong>
            )}
            {clip.share.shared ? <SharedIndicator /> : null}
          </div>
          {clip.tags.length > 0 ? (
            <div className="rs-chip-row">
              {clip.tags.map((tag) => (
                <span className="rs-chip" key={tag}>
                  #{tag}
                </span>
              ))}
            </div>
          ) : null}
          <div className="rs-player-actions">
            <PlayerActions clip={clip} />
          </div>
        </section>

        <ClipFacts clip={clip} game={game} />

        {session.length > 1 ? (
          <SameSession session={session} current={clip} category={game} />
        ) : null}

        {more.length > 0 ? (
          <>
            <aside
              className="rs-watch-more"
              aria-labelledby="rs-more-heading-side"
            >
              <h2 id="rs-more-heading-side" className="rs-eyebrow">
                More from {gameName}
              </h2>
              <ClipGrid
                clips={more.slice(0, 5)}
                categories={categories}
                variant="filmstrip"
                showGame={false}
              />
              <Link
                to="/games/$slug"
                params={{ slug: clip.categorySlug }}
                className="rs-watch-all"
              >
                All {game ? `${game.clipCount} ` : ""}
                {gameName} clips
                <IconChevronRight size={16} aria-hidden="true" />
              </Link>
            </aside>
            <section
              className="rs-watch-more-full"
              aria-labelledby="rs-more-heading-full"
            >
              <div className="rs-watch-section-head">
                <h2 id="rs-more-heading-full" className="rs-display">
                  More from {gameName}
                </h2>
                <Link
                  to="/games/$slug"
                  params={{ slug: clip.categorySlug }}
                  className="rs-watch-all-inline"
                >
                  All {game ? `${game.clipCount} ` : ""}clips
                  <IconChevronRight size={15} aria-hidden="true" />
                </Link>
              </div>
              <ClipGrid
                clips={more.slice(0, 12)}
                categories={categories}
                variant="filmstrip"
                groupByMonth
                showGame={false}
              />
            </section>
          </>
        ) : null}
      </div>
    </div>
  );
}
