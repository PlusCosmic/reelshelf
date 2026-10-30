import type { CSSProperties } from "react";
import { useEffect, useMemo, useRef, useState } from "react";
import { activityCopy } from "@/copy/activity";
import {
  bookBinding,
  formatDuration,
  spineTitle,
} from "@/components/Reelshelf/reelshelf-model";
import {
  activityThumbnailUrl,
  fetchOwnClips,
  fetchOwnGames,
  type ActivityClip,
  type ActivityClipOrder,
  type ActivityGame,
} from "@/shared/services/discordActivity";
import { clipTime, groupClipsByDay } from "./clipDays";

type PickerState = {
  clips: ActivityClip[];
  hasMore: boolean;
  page: number;
  /** "replace" while a new search, game or order loads over the old list; "more" while a page is added. */
  loading: "replace" | "more" | null;
  failed: boolean;
};

const initialState: PickerState = {
  clips: [],
  hasMore: false,
  page: 1,
  loading: "replace",
  failed: false,
};

export function ActivityClipPicker({
  roomToken,
  queuedClipIds,
  onPick,
}: {
  roomToken: string;
  queuedClipIds: ReadonlySet<string>;
  onPick: (clip: ActivityClip) => void;
}) {
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [gameId, setGameId] = useState<string | null>(null);
  const [order, setOrder] = useState<ActivityClipOrder>("newest");
  const [games, setGames] = useState<ActivityGame[]>([]);
  const [state, setState] = useState<PickerState>(initialState);
  // Each new search, game or order starts a new list; a page that lands after one does is dropped.
  const listVersion = useRef(0);

  // Search as the user pauses typing rather than on every key.
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  // The spines are loaded once, so they keep their order and counts while the member moves between them.
  useEffect(() => {
    let cancelled = false;
    fetchOwnGames(roomToken)
      .then((loaded) => {
        if (!cancelled) setGames(loaded);
      })
      .catch(() => {
        // Without the games the picker still lists every clip; it just can't filter them.
      });
    return () => {
      cancelled = true;
    };
  }, [roomToken]);

  useEffect(() => {
    const version = ++listVersion.current;
    // The old list stays up, dimmed, until the new one arrives, so switching games doesn't empty the page.
    setState((current) => ({
      ...current,
      loading: "replace",
      failed: false,
    }));
    fetchOwnClips(roomToken, { search: query, page: 1, gameId, order })
      .then((response) => {
        if (version !== listVersion.current) return;
        setState({
          clips: response.clips,
          hasMore: response.hasMore,
          page: 1,
          loading: null,
          failed: false,
        });
      })
      .catch(() => {
        if (version === listVersion.current)
          setState({ ...initialState, loading: null, failed: true });
      });
  }, [roomToken, query, gameId, order]);

  const loadMore = () => {
    const version = listVersion.current;
    const next = state.page + 1;
    setState((current) => ({ ...current, loading: "more" }));
    fetchOwnClips(roomToken, { search: query, page: next, gameId, order })
      .then((response) => {
        if (version !== listVersion.current) return;
        setState((current) => ({
          clips: [...current.clips, ...response.clips],
          hasMore: response.hasMore,
          page: next,
          loading: null,
          failed: false,
        }));
      })
      .catch(() => {
        if (version === listVersion.current)
          setState((current) => ({ ...current, loading: null, failed: true }));
      });
  };

  const days = useMemo(
    () => groupClipsByDay(state.clips, new Date()),
    [state.clips],
  );
  const selectedGame = games.find((game) => game.id === gameId) ?? null;
  const settled = state.loading === null && !state.failed;

  return (
    <section className="rs-activity-picker">
      <div className="rs-activity-picker-head">
        <h2 className="rs-activity-kicker">{activityCopy.picker.title}</h2>
        <label className="rs-search">
          <input
            type="search"
            placeholder={activityCopy.picker.search}
            aria-label={activityCopy.picker.search}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
      </div>

      {games.length > 1 ? (
        <ActivityShelf games={games} selectedId={gameId} onSelect={setGameId} />
      ) : null}

      <div className="rs-activity-picker-bar">
        <p className="rs-activity-picker-game">
          {selectedGame?.name ?? activityCopy.picker.allGames}
        </p>
        <div
          className="rs-activity-sort"
          role="group"
          aria-label={activityCopy.picker.sort}
        >
          {(["newest", "oldest"] as const).map((option) => (
            <button
              key={option}
              type="button"
              className="rs-activity-sort-option"
              aria-pressed={order === option}
              onClick={() => setOrder(option)}
            >
              {activityCopy.picker[option]}
            </button>
          ))}
        </div>
      </div>

      {state.failed ? (
        <p className="rs-activity-role">{activityCopy.picker.failed}</p>
      ) : null}
      {settled && state.clips.length === 0 ? (
        <p className="rs-activity-role">
          {query ? activityCopy.picker.noMatch : activityCopy.picker.empty}
        </p>
      ) : null}

      <div className="rs-activity-days" aria-busy={state.loading === "replace"}>
        {days.map((day) => (
          <section key={day.key} className="rs-activity-day">
            <h3 className="rs-activity-day-label">{day.label}</h3>
            <ul className="rs-activity-clips">
              {day.clips.map((clip) => (
                <li key={clip.clipId}>
                  <PickerRow
                    clip={clip}
                    queued={queuedClipIds.has(clip.clipId)}
                    showGame={gameId === null}
                    onPick={onPick}
                  />
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>

      {state.loading === "more" ||
      (state.loading === "replace" && state.clips.length === 0) ? (
        <span className="rs-spinner" aria-label={activityCopy.picker.loading} />
      ) : state.loading === null && state.hasMore ? (
        <button type="button" className="rs-primary" onClick={loadMore}>
          {activityCopy.picker.loadMore}
        </button>
      ) : null}
    </section>
  );
}

function PickerRow({
  clip,
  queued,
  showGame,
  onPick,
}: {
  clip: ActivityClip;
  queued: boolean;
  showGame: boolean;
  onPick: (clip: ActivityClip) => void;
}) {
  const thumbnail = activityThumbnailUrl(clip.media);
  return (
    <button
      type="button"
      className="rs-activity-clip"
      aria-current={queued ? "true" : undefined}
      aria-label={activityCopy.picker.add(clip.title)}
      disabled={!clip.ready || queued}
      onClick={() => onPick(clip)}
    >
      {thumbnail ? (
        <img src={thumbnail} alt="" loading="lazy" />
      ) : (
        <span className="rs-activity-clip-placeholder" />
      )}
      <span className="rs-activity-clip-text">
        <span className="rs-activity-clip-title">{clip.title}</span>
        <span className="rs-activity-clip-meta">
          {queued
            ? activityCopy.picker.inQueue
            : clip.ready
              ? [
                  showGame ? clip.game : null,
                  formatDuration(clip.durationSeconds),
                  clipTime(clip.createdAt),
                ]
                  .filter(Boolean)
                  .join(" · ")
              : activityCopy.picker.encoding}
        </span>
      </span>
    </button>
  );
}

/**
 * The member's games as a shelf of spines, "All games" first. Every spine is the same height and shows its
 * whole name and clip count: a long name wraps onto another line of the spine and makes it wider, and the
 * shelf wraps onto another plank rather than scrolling. Choosing a spine lifts it without moving the others.
 */
function ActivityShelf({
  games,
  selectedId,
  onSelect,
}: {
  games: ActivityGame[];
  selectedId: string | null;
  onSelect: (gameId: string | null) => void;
}) {
  const total = games.reduce((sum, game) => sum + game.clipCount, 0);
  return (
    <ul className="rs-activity-shelf" aria-label={activityCopy.picker.games}>
      <li>
        <Spine
          className="rs-activity-spine all"
          name={activityCopy.picker.allGames}
          count={total}
          selected={selectedId === null}
          onSelect={() => onSelect(null)}
        />
      </li>
      {games.map((game) => {
        const binding = bookBinding(game);
        return (
          <li key={game.id}>
            <Spine
              className={`rs-activity-spine${binding.light ? " light" : ""}`}
              style={
                {
                  "--cloth": binding.cloth,
                  "--ink": binding.ink,
                } as CSSProperties
              }
              name={game.name}
              count={game.clipCount}
              selected={selectedId === game.id}
              onSelect={() => onSelect(game.id)}
            />
          </li>
        );
      })}
    </ul>
  );
}

function Spine({
  className,
  style,
  name,
  count,
  selected,
  onSelect,
}: {
  className: string;
  style?: CSSProperties;
  name: string;
  count: number;
  selected: boolean;
  onSelect: () => void;
}) {
  const { series, title } = spineTitle(name);
  return (
    <button
      type="button"
      className={className}
      style={style}
      aria-pressed={selected}
      aria-label={activityCopy.picker.spine(name, count)}
      onClick={onSelect}
    >
      <span className="rs-activity-spine-title" aria-hidden="true">
        {series ? (
          <span className="rs-activity-spine-series">{series}</span>
        ) : null}
        <span className="rs-activity-spine-name">{title}</span>
      </span>
      <span className="rs-activity-spine-count" aria-hidden="true">
        {count}
      </span>
    </button>
  );
}
