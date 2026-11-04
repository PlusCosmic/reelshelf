import {
  Link,
  useNavigate,
  useRouter,
  useRouterState,
} from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import type { CSSProperties, MouseEvent } from "react";
import { useEffect, useEffectEvent, useMemo, useRef, useState } from "react";
import {
  IconArrowBackUp,
  IconCheck,
  IconChevronLeft,
  IconChevronRight,
  IconTrash,
  IconLayoutSidebarRight,
  IconRectangle,
  IconStack2,
} from "@tabler/icons-react";
import type {
  Clip,
  GameCategoryResponse,
  PlaylistWithDetails,
} from "@/api-client";
import {
  useClipNeighbours,
  useClipSession,
  useClipsInfinite,
} from "@/hooks/clips.queries";
import { useClip, useCurrentUser, useMarkAsViewed } from "@/hooks/queries";
import { useRestoreClips } from "@/hooks/bin.queries";
import { binCopy } from "@/copy/bin";
import { clipCopy } from "@/copy/clip";
import { commonCopy } from "@/copy/common";
import { ClipVideoStage } from "@/components/Video/ClipVideoStage";
import { fetchPlaylistById } from "@/shared/services/playlists";
import { binDaysLeft } from "./binModel";
import { useBin } from "./BinToast";
import { collectionClips } from "./collections-model";
import {
  librarySearchOf,
  listFiltersFor,
  type ClipListSearch,
} from "./libraryList";
import {
  isShortcutKey,
  readSortTally,
  recordSort,
  unrecordSort,
  type SortTally,
} from "./sortThrough";
import type { ClipLinkSearch } from "./primitives/ClipViews";
import {
  ClipGrid,
  ClipThumb,
  LegendPlate,
  PlayerActions,
  SharedIndicator,
} from "./ReelshelfPrimitives";
import {
  bookBinding,
  formatDate,
  formatDuration,
  makeGameShelf,
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

// The player is sized so it and the details under it (title, meta, tags, actions) fit the window together. The
// details' height varies with a wrapped title or a row of tags, so it is measured and handed to the stylesheet.
function trackDetailsHeight(details: HTMLElement | null) {
  const watch = details?.closest<HTMLElement>(".rs-watch");
  if (!details || !watch) return;
  const observer = new ResizeObserver(() => {
    watch.style.setProperty(
      "--rs-watch-details-height",
      `${details.offsetHeight}px`,
    );
  });
  observer.observe(details);
  return () => observer.disconnect();
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

type PlayThrough = {
  playlist: PlaylistWithDetails;
  clips: Clip[];
  index: number;
};

function GameStrip({
  game,
  slug,
  list,
  onBack,
  playThrough,
  theatre,
  onToggleTheatre,
}: {
  game: GameShelfItem | undefined;
  slug: string;
  list: ClipListSearch;
  /** Goes back to the list the clip was opened from, where it was; the link is the fallback. */
  onBack: (event: MouseEvent) => void;
  playThrough: PlayThrough | null;
  theatre: boolean;
  onToggleTheatre: () => void;
}) {
  return (
    <nav className="rs-watch-strip" aria-label={clipCopy.strip.label}>
      {playThrough ? (
        <Link
          to="/collections/$playlistId"
          params={{ playlistId: playThrough.playlist.id }}
          className="rs-watch-back"
        >
          <IconChevronLeft size={18} aria-hidden="true" />
          <IconStack2
            className="rs-watch-back-mark"
            size={26}
            aria-hidden="true"
          />
          <span>
            <small>{clipCopy.strip.backTo}</small>
            <span className="rs-display">{playThrough.playlist.name}</span>
          </span>
        </Link>
      ) : list.from === "library" ? (
        <Link
          to="/"
          search={librarySearchOf(list)}
          className="rs-watch-back"
          onClick={onBack}
        >
          <IconChevronLeft size={18} aria-hidden="true" />
          <span>
            <small>{clipCopy.strip.backTo}</small>
            <span className="rs-display">{clipCopy.strip.theLibrary}</span>
          </span>
        </Link>
      ) : (
        <Link
          to="/games/$slug"
          params={{ slug }}
          search={librarySearchOf(list)}
          className="rs-watch-back"
          onClick={onBack}
        >
          <IconChevronLeft size={18} aria-hidden="true" />
          {game?.coverUrl ? <img src={game.coverUrl} alt="" /> : null}
          <span>
            <small>{clipCopy.strip.backTo}</small>
            <span className="rs-display">
              {game?.name ?? clipCopy.strip.theShelf}
            </span>
          </span>
        </Link>
      )}
      {playThrough ? (
        <span className="rs-watch-counts">
          {clipCopy.strip.position(
            playThrough.index + 1,
            playThrough.clips.length,
          )}
        </span>
      ) : game ? (
        <span className="rs-watch-counts">
          {commonCopy.clips(game.clipCount)}
          {game.unviewedCount > 0 ? (
            <strong>
              <StitchedDot />
              {clipCopy.strip.newCount(game.unviewedCount)}
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
        {clipCopy.strip.theatre}
      </button>
    </nav>
  );
}

function NeighbourLink({
  clip,
  direction,
  inCollection,
  list,
  fromList,
}: {
  clip: Clip | null | undefined;
  direction: "older" | "newer";
  /** Playing through a collection, the neighbours are the clips before and after in its order. */
  inCollection?: string;
  /** The library list the clip was opened from, which stepping walks through. */
  list?: ClipListSearch;
  /** Stepping replaces this clip in the history, so back still returns to the list. */
  fromList?: boolean;
}) {
  const label = inCollection
    ? direction === "older"
      ? clipCopy.neighbour.previous
      : clipCopy.neighbour.next
    : direction === "older"
      ? clipCopy.neighbour.older
      : clipCopy.neighbour.newer;
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
      search={inCollection ? { collection: inCollection } : list}
      replace={fromList}
      state={fromList ? { fromList: true } : undefined}
      className={`rs-watch-neighbour ${direction}`}
      aria-label={clipCopy.neighbour.label(label, clip.video.title)}
    >
      {direction === "older" ? (
        <IconChevronLeft size={20} aria-hidden="true" />
      ) : null}
      {thumbnailUrl(clip) ? (
        <img src={thumbnailUrl(clip)!} alt="" loading="lazy" />
      ) : (
        <span className="rs-watch-neighbour-thumb" aria-hidden="true" />
      )}
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
  day,
  current,
  category,
}: {
  session: Clip[];
  day: Date;
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
          {clipCopy.session.title}
        </h2>
        <span>
          {clipCopy.session.summary(
            dayFormatter.format(day),
            session.length,
            formatTime(first.createdAt),
            formatTime(last.createdAt),
          )}
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
                  <span className="rs-session-card-now">
                    {clipCopy.session.nowPlaying}
                  </span>
                ) : clip.isViewed ? (
                  <span>{clipCopy.session.watched}</span>
                ) : (
                  <span>
                    <StitchedDot />
                    {clipCopy.session.new}
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
    <aside className="rs-watch-facts" aria-label={clipCopy.facts.label}>
      <dl>
        <dt>{clipCopy.facts.game}</dt>
        <dd>
          <Link
            to="/games/$slug"
            params={{ slug: clip.categorySlug }}
            className="rs-watch-facts-game"
          >
            {game?.coverUrl ? <img src={game.coverUrl} alt="" /> : null}
            {game?.name ?? clip.categorySlug}
          </Link>
        </dd>
        {clip.legend ? (
          <>
            <dt>{clipCopy.facts.legend}</dt>
            <dd className="rs-watch-facts-legend">
              <LegendPlate legend={clip.legend} size="small" />
              {clip.legend}
            </dd>
          </>
        ) : null}
        <dt>{clipCopy.facts.recorded}</dt>
        <dd>
          {formatDate(clip.createdAt)}, {formatTime(clip.createdAt)}
        </dd>
        <dt>{clipCopy.facts.length}</dt>
        <dd>{formatDuration(clip.video.durationSeconds)}</dd>
        <dt>{clipCopy.facts.watched}</dt>
        <dd>
          {clip.isViewed ? clipCopy.facts.watchedYes : clipCopy.facts.watchedNo}
        </dd>
        <dt>{clipCopy.facts.sharing}</dt>
        <dd>
          {clip.share.shared
            ? clipCopy.facts.sharedWithLink
            : clipCopy.facts.onlyYou}
        </dd>
      </dl>
    </aside>
  );
}

/**
 * How long the bin keeps a clip, for the note on a binned clip's page. The bin page reads the API's own figure; this
 * one only words the note, and `Bin:Retention` keeps to it unless changed.
 */
const BIN_RETENTION_DAYS = 30;

/**
 * Sorting through a list: keep (K) or bin (X) this clip and move on to the next, with a running tally. At the end of
 * the list it says how it went and goes back.
 */
function SortBar({
  tally,
  done,
  binning,
  canBin,
  onKeep,
  onBin,
  onStop,
}: {
  tally: SortTally;
  done: boolean;
  binning: boolean;
  canBin: boolean;
  onKeep: () => void;
  onBin: () => void;
  onStop: () => void;
}) {
  const copy = binCopy.sort;
  if (done)
    return (
      <div className="rs-sort-bar done" role="status">
        <span>
          <strong className="rs-display">{copy.doneTitle}</strong>{" "}
          {copy.doneBody(tally.kept, tally.binned)}
        </span>
        <span className="rs-sort-actions">
          <Link to="/bin" className="rs-small-button">
            <IconTrash size={13} aria-hidden="true" />
            {binCopy.toast.openBin}
          </Link>
          <button type="button" className="rs-small-button" onClick={onStop}>
            <IconChevronLeft size={13} aria-hidden="true" />
            {copy.backToList}
          </button>
        </span>
      </div>
    );
  return (
    <div className="rs-sort-bar" role="group" aria-label={copy.label}>
      <span className="rs-sort-tally">
        <small className="rs-eyebrow">{copy.label}</small>
        {copy.tally(tally.kept, tally.binned)}
      </span>
      <span className="rs-sort-actions">
        <button
          type="button"
          className="rs-small-button rs-sort-keep"
          onClick={onKeep}
        >
          <IconCheck size={13} aria-hidden="true" />
          {copy.keep}
          <kbd>{copy.keepKey}</kbd>
        </button>
        <button
          type="button"
          className="rs-small-button rs-small-button-danger rs-sort-bin"
          onClick={onBin}
          disabled={!canBin || binning}
        >
          <IconTrash size={13} aria-hidden="true" />
          {copy.bin}
          <kbd>{copy.binKey}</kbd>
        </button>
        <button type="button" className="rs-small-button" onClick={onStop}>
          {copy.stop}
        </button>
      </span>
    </div>
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
  collectionId,
  list = {},
}: {
  slug: string;
  clipId: string;
  /** Set when the clip is being played as part of a collection, in the collection's order. */
  collectionId?: string;
  /** The library list the clip was opened from: back returns to it, and stepping on walks through it. */
  list?: ClipListSearch;
}) {
  const { data: clip, isLoading, isError } = useClip(clipId);
  const navigate = useNavigate();
  const router = useRouter();
  const fromList = useRouterState({
    select: (state) => !!state.location.state.fromList,
  });
  const { bin, pending: binning } = useBin();
  const restore = useRestoreClips();
  const currentUserId = useCurrentUser().data?.id;
  const [tally, setTally] = useState<SortTally>(readSortTally);
  // Sorting reached the end of the list.
  const [sortDone, setSortDone] = useState(false);
  // The clip a keep is moving on from, so presses while the next clip loads count once.
  const keptFrom = useRef<string | null>(null);
  useEffect(() => {
    keptFrom.current = null;
  }, [clipId]);
  const { data: playlist } = useQuery({
    queryKey: ["playlists", collectionId],
    queryFn: () => fetchPlaylistById(collectionId!),
    enabled: !!collectionId,
  });
  const playThrough = useMemo((): PlayThrough | null => {
    if (!playlist) return null;
    const clips = collectionClips(playlist).map(({ clip: item }) => item);
    const index = clips.findIndex((item) => item.clipId === clipId);
    return index === -1 ? null : { playlist, clips, index };
  }, [playlist, clipId]);
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
  // Playing through a collection, the next clip follows on by itself.
  const upNext = playThrough
    ? (playThrough.clips[playThrough.index + 1] ?? null)
    : null;
  const playNext =
    upNext && playThrough
      ? () =>
          void navigate({
            to: "/games/$slug/$clipId",
            params: { slug: upNext.categorySlug, clipId: upNext.clipId },
            search: { collection: playThrough.playlist.id },
          })
      : undefined;
  const { data: sessionData } = useClipSession(clip);
  const session = useMemo(() => sessionData?.clips ?? [], [sessionData]);
  const neighboursQuery = useClipNeighbours(
    clip,
    collectionId ? undefined : listFiltersFor(list, clip?.gameCategoryId),
  );
  const neighbours = neighboursQuery.data;

  /** Back to the list the clip was opened from: the history entry itself where it is the one before, so it opens where it was left. */
  const backToList = () => {
    if (fromList) {
      router.history.back();
      return;
    }
    const search = librarySearchOf(list);
    void (list.from === "library"
      ? navigate({ to: "/", search })
      : navigate({
          to: "/games/$slug",
          params: { slug: clip?.categorySlug ?? slug },
          search,
        }));
  };
  const onBack = (event: MouseEvent) => {
    if (!fromList || event.metaKey || event.ctrlKey || event.button !== 0)
      return;
    event.preventDefault();
    router.history.back();
  };

  /** Moves on in the list (down it, to older clips; else up it), replacing this clip so back still returns to the list. */
  const stepOn = async (current: Clip) => {
    // A key pressed before this clip's neighbours arrived waits for them, rather than taking it for the list's end.
    const around =
      neighboursQuery.data ?? (await neighboursQuery.refetch()).data;
    const target = around?.older ?? (list.sort ? null : around?.newer);
    if (!target || target.clipId === current.clipId) {
      if (list.sort) setSortDone(true);
      else backToList();
      return;
    }
    void navigate({
      to: "/games/$slug/$clipId",
      params: { slug: target.categorySlug, clipId: target.clipId },
      search: list,
      replace: true,
      state: { fromList },
    });
  };

  /** Bins this clip and moves on; the undo note brings it back, and brings this page back to it. */
  const binAndStepOn = async (current: Clip) => {
    if (binning || current.binnedAt) return;
    try {
      await bin(
        { clipIds: [current.clipId] },
        {
          onUndone: () => {
            // Back to sorting the clip that was put back, which no longer counts as binned.
            if (list.sort) {
              setTally(unrecordSort("binned"));
              setSortDone(false);
              keptFrom.current = null;
            }
            void navigate({
              to: "/games/$slug/$clipId",
              params: { slug: current.categorySlug, clipId: current.clipId },
              search: list,
              replace: true,
              state: { fromList },
            });
          },
        },
      );
    } catch {
      return;
    }
    if (list.sort) setTally(recordSort("binned"));
    if (collectionId) backToList();
    else await stepOn(current);
  };

  const keep = (current: Clip) => {
    if (keptFrom.current === current.clipId) return;
    keptFrom.current = current.clipId;
    setTally(recordSort("kept"));
    void stepOn(current);
  };

  // X bins the clip and moves on; while sorting through, K keeps it and moves on.
  const onKey = useEffectEvent((event: KeyboardEvent) => {
    if (!clip || !isShortcutKey(event) || sortDone) return;
    const key = event.key.toLowerCase();
    const owner = clip.ownerId === currentUserId;
    if ((key === "x" || key === "delete") && owner && !clip.binnedAt) {
      event.preventDefault();
      void binAndStepOn(clip);
    } else if (key === "k" && list.sort) {
      event.preventDefault();
      keep(clip);
    }
  });
  useEffect(() => {
    const listener = (event: KeyboardEvent) => onKey(event);
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);
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
    return <div className="rs-section rs-empty">{clipCopy.loading}</div>;
  if (isError || !clip)
    return <div className="rs-section rs-empty">{clipCopy.notFound}</div>;

  const { cloth } = game ? bookBinding(game) : { cloth: "var(--accent)" };
  const gameName = game?.name ?? clip.categorySlug;
  const collectionSearch: ClipLinkSearch | undefined = playThrough
    ? { collection: playThrough.playlist.id }
    : undefined;
  const previous = playThrough
    ? (playThrough.clips[playThrough.index - 1] ?? null)
    : neighbours?.older;
  const next = playThrough
    ? (playThrough.clips[playThrough.index + 1] ?? null)
    : neighbours?.newer;

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
        list={list}
        onBack={onBack}
        playThrough={playThrough}
        theatre={theatre}
        onToggleTheatre={toggleTheatre}
      />

      <div className="rs-watch-layout">
        <section className="rs-watch-stage" aria-label={clipCopy.stage}>
          {game?.keyArtUrl ? (
            <img className="rs-watch-stage-art" src={game.keyArtUrl} alt="" />
          ) : null}
          <NeighbourLink
            clip={previous}
            direction="older"
            inCollection={collectionSearch?.collection}
            list={list}
            fromList={fromList}
          />
          {/* The thumbnail sits behind the video so loading looks like the video arriving; the
              cloth gradient underneath covers a missing thumbnail. Until the video is ready, its
              processing state stands in for the player. */}
          <div
            className="rs-player"
            style={{
              backgroundImage: [
                thumbnailUrl(clip) ? `url("${thumbnailUrl(clip)}")` : null,
                "linear-gradient(135deg, var(--game-a), var(--game-b))",
              ]
                .filter(Boolean)
                .join(", "),
            }}
          >
            <ClipVideoStage
              status={clip.video.status}
              encodeProgress={clip.video.encodeProgress}
              sources={clip.video.sources}
              poster={thumbnailUrl(clip)}
              title={clip.video.title}
              className="rs-player-frame"
              autoPlay
              onLoaded={() => {
                if (!clip.isViewed && !markViewed.isPending) {
                  markViewed.mutate(clip.clipId);
                }
              }}
              onEnded={playNext}
            />
          </div>
          <NeighbourLink
            clip={next}
            direction="newer"
            inCollection={collectionSearch?.collection}
            list={list}
            fromList={fromList}
          />
        </section>

        <section className="rs-watch-details" ref={trackDetailsHeight}>
          {clip.binnedAt ? (
            <div className="rs-watch-binned" role="status">
              <IconTrash size={16} aria-hidden="true" />
              <span>
                <strong>{clipCopy.binned.title}</strong>{" "}
                {clipCopy.binned.body(
                  binDaysLeft(new Date(clip.binnedAt), BIN_RETENTION_DAYS),
                )}
              </span>
              <button
                type="button"
                className="rs-small-button"
                onClick={() => restore.mutate([clip.clipId])}
                disabled={restore.isPending}
              >
                <IconArrowBackUp size={13} aria-hidden="true" />
                {restore.isPending
                  ? clipCopy.binned.restoring
                  : clipCopy.binned.restore}
              </button>
            </div>
          ) : null}
          {list.sort && !playThrough ? (
            <SortBar
              tally={tally}
              done={sortDone}
              binning={binning}
              canBin={clip.ownerId === currentUserId && !clip.binnedAt}
              onKeep={() => keep(clip)}
              onBin={() => void binAndStepOn(clip)}
              onStop={backToList}
            />
          ) : null}
          <h1 className="rs-display rs-watch-title">{clip.video.title}</h1>
          <div className="rs-watch-meta">
            {clip.legend ? (
              <span className="rs-watch-legend">
                <LegendPlate legend={clip.legend} size="large" />
                <small>{clipCopy.playedAs}</small>
                {clip.legend}
              </span>
            ) : null}
            <span>
              {formatDate(clip.createdAt)}, {formatTime(clip.createdAt)}
            </span>
            <span>{formatDuration(clip.video.durationSeconds)}</span>
            {clip.isViewed ? null : (
              <strong>
                <StitchedDot />
                {clipCopy.notWatchedYet}
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
            <PlayerActions
              clip={clip}
              onBin={() => void binAndStepOn(clip)}
              binning={binning}
            />
          </div>
        </section>

        <ClipFacts clip={clip} game={game} />

        {playThrough ? (
          <>
            <aside
              className="rs-watch-more"
              aria-labelledby="rs-collection-heading-side"
            >
              <h2 id="rs-collection-heading-side" className="rs-eyebrow">
                {clipCopy.upNextIn(playThrough.playlist.name)}
              </h2>
              <ClipGrid
                clips={playThrough.clips}
                categories={categories}
                variant="filmstrip"
                showGame={false}
                currentClipId={clipId}
                linkSearch={collectionSearch}
              />
            </aside>
            <section
              className="rs-watch-more-full"
              aria-labelledby="rs-collection-heading-full"
            >
              <div className="rs-watch-section-head">
                <h2 id="rs-collection-heading-full" className="rs-display">
                  {playThrough.playlist.name}
                </h2>
                <span>
                  {clipCopy.strip.position(
                    playThrough.index + 1,
                    playThrough.clips.length,
                  )}
                </span>
              </div>
              <ClipGrid
                clips={playThrough.clips}
                categories={categories}
                variant="filmstrip"
                currentClipId={clipId}
                linkSearch={collectionSearch}
              />
            </section>
          </>
        ) : null}

        {!playThrough && session.length > 1 ? (
          <SameSession
            session={session}
            day={sessionData!.day}
            current={clip}
            category={game}
          />
        ) : null}

        {!playThrough && more.length > 0 ? (
          <>
            <aside
              className="rs-watch-more"
              aria-labelledby="rs-more-heading-side"
            >
              <h2 id="rs-more-heading-side" className="rs-eyebrow">
                {clipCopy.moreFrom(gameName)}
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
                {clipCopy.allGameClips(game?.clipCount, gameName)}
                <IconChevronRight size={16} aria-hidden="true" />
              </Link>
            </aside>
            <section
              className="rs-watch-more-full"
              aria-labelledby="rs-more-heading-full"
            >
              <div className="rs-watch-section-head">
                <h2 id="rs-more-heading-full" className="rs-display">
                  {clipCopy.moreFrom(gameName)}
                </h2>
                <Link
                  to="/games/$slug"
                  params={{ slug: clip.categorySlug }}
                  className="rs-watch-all-inline"
                >
                  {clipCopy.allClips(game?.clipCount)}
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
