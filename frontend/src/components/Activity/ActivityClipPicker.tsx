import { useEffect, useState } from "react";
import { formatDuration } from "@/components/Reelshelf/reelshelf-model";
import {
  activityThumbnailUrl,
  fetchOwnClips,
  type ActivityClip,
} from "@/shared/services/discordActivity";

type PickerState = {
  clips: ActivityClip[];
  hasMore: boolean;
  page: number;
  loading: boolean;
  failed: boolean;
};

const initialState: PickerState = {
  clips: [],
  hasMore: false,
  page: 1,
  loading: true,
  failed: false,
};

/** The member's own clips, newest first, with a search box; picking one adds it to the room's queue. */
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
  const [state, setState] = useState<PickerState>(initialState);

  // Search as the user pauses typing rather than on every key.
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    let cancelled = false;
    setState(initialState);
    fetchOwnClips(roomToken, { search: query, page: 1 })
      .then((response) => {
        if (cancelled) return;
        setState({
          clips: response.clips,
          hasMore: response.hasMore,
          page: 1,
          loading: false,
          failed: false,
        });
      })
      .catch(() => {
        if (!cancelled)
          setState({ ...initialState, loading: false, failed: true });
      });
    return () => {
      cancelled = true;
    };
  }, [roomToken, query]);

  const loadMore = () => {
    const next = state.page + 1;
    setState((current) => ({ ...current, loading: true }));
    fetchOwnClips(roomToken, { search: query, page: next })
      .then((response) =>
        setState((current) => ({
          clips: [...current.clips, ...response.clips],
          hasMore: response.hasMore,
          page: next,
          loading: false,
          failed: false,
        })),
      )
      .catch(() =>
        setState((current) => ({ ...current, loading: false, failed: true })),
      );
  };

  return (
    <section className="rs-activity-picker">
      <div className="rs-activity-picker-head">
        <h2 className="rs-activity-kicker">Add from your shelf</h2>
        <label className="rs-search">
          <input
            type="search"
            placeholder="Search your clips"
            aria-label="Search your clips"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
      </div>

      {state.failed ? (
        <p className="rs-activity-role">Couldn't load your clips.</p>
      ) : null}
      {!state.loading && !state.failed && state.clips.length === 0 ? (
        <p className="rs-activity-role">
          {query ? "No clips match that search." : "Your shelf is empty."}
        </p>
      ) : null}

      <ul className="rs-activity-clips">
        {state.clips.map((clip) => {
          const queued = queuedClipIds.has(clip.clipId);
          return (
            <li key={clip.clipId}>
              <button
                type="button"
                className="rs-activity-clip"
                aria-current={queued ? "true" : undefined}
                aria-label={`Add ${clip.title} to the queue`}
                disabled={!clip.ready || queued}
                onClick={() => onPick(clip)}
              >
                <img
                  src={activityThumbnailUrl(clip.videoId)}
                  alt=""
                  loading="lazy"
                />
                <span className="rs-activity-clip-title">{clip.title}</span>
                <span className="rs-activity-clip-meta">
                  {queued
                    ? "In the queue"
                    : clip.ready
                      ? [clip.game, formatDuration(clip.durationSeconds)]
                          .filter(Boolean)
                          .join(" · ")
                      : "Still encoding"}
                </span>
              </button>
            </li>
          );
        })}
      </ul>

      {state.loading ? (
        <span className="rs-spinner" aria-label="Loading clips" />
      ) : state.hasMore ? (
        <button type="button" className="rs-primary" onClick={loadMore}>
          Load more
        </button>
      ) : null}
    </section>
  );
}
