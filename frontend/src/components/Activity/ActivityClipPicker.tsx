import { useEffect, useState } from "react";
import { activityCopy } from "@/copy/activity";
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

      {state.failed ? (
        <p className="rs-activity-role">{activityCopy.picker.failed}</p>
      ) : null}
      {!state.loading && !state.failed && state.clips.length === 0 ? (
        <p className="rs-activity-role">
          {query ? activityCopy.picker.noMatch : activityCopy.picker.empty}
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
                aria-label={activityCopy.picker.add(clip.title)}
                disabled={!clip.ready || queued}
                onClick={() => onPick(clip)}
              >
                {activityThumbnailUrl(clip.media) ? (
                  <img
                    src={activityThumbnailUrl(clip.media)!}
                    alt=""
                    loading="lazy"
                  />
                ) : (
                  <span className="rs-activity-clip-placeholder" />
                )}
                <span className="rs-activity-clip-title">{clip.title}</span>
                <span className="rs-activity-clip-meta">
                  {queued
                    ? activityCopy.picker.inQueue
                    : clip.ready
                      ? [clip.game, formatDuration(clip.durationSeconds)]
                          .filter(Boolean)
                          .join(" · ")
                      : activityCopy.picker.encoding}
                </span>
              </button>
            </li>
          );
        })}
      </ul>

      {state.loading ? (
        <span className="rs-spinner" aria-label={activityCopy.picker.loading} />
      ) : state.hasMore ? (
        <button type="button" className="rs-primary" onClick={loadMore}>
          {activityCopy.picker.loadMore}
        </button>
      ) : null}
    </section>
  );
}
