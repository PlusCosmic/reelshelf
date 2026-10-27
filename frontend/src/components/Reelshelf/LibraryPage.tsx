import { useNavigate, useRouterState } from "@tanstack/react-router";
import { useQueryClient } from "@tanstack/react-query";
import type { DragEvent } from "react";
import { useEffect, useMemo, useReducer, useRef, useState } from "react";
import type { GameCategoryResponse } from "@/api-client";
import { AddCategoryModal } from "@/components/AddCategoryModal";
import type { TwitchGhost } from "@/components/AddClips/addClipsModel";
import { TwitchBookStrip } from "@/components/AddClips/TwitchBookStrip";
import {
  twitchSuggestionsKey,
  useTwitchSuggestions,
} from "@/components/AddClips/twitchSuggestions";
import { useUploads } from "@/components/AddClips/UploadsProvider";
import type { BulkUploadFileInput } from "@/hooks/bulkUploadQueue";
import { useAddGameFromIgdb } from "@/hooks/queries";
import { useClipLegends, useTopTags } from "@/hooks/clips.queries";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import {
  bulkUploadInputsFromDataTransfer,
  dataTransferHasFiles,
} from "@/utils/bulkUploadDrop";
import { Bookcase } from "./Bookcase";
import { GameBanner } from "./GameBanner";
import { ShelfBar } from "./ShelfBar";
import {
  Chip,
  LegendPlate,
  PagedClipGrid,
  SearchBox,
  StatLine,
} from "./ReelshelfPrimitives";
import {
  bookBinding,
  categoryTotalsFor,
  makeGameShelf,
  type GameShelfItem,
} from "./reelshelf-model";
import { useLibraryData } from "./useLibraryData";

declare module "@tanstack/react-router" {
  interface HistoryState {
    /** Set when a game is chosen from the bar below the shelf: land on its clips, not the shelf. */
    toClips?: boolean;
  }
}

const APEX_LEGENDS_SLUG = "apex-legends";

function topbarHeight() {
  return (
    parseFloat(
      getComputedStyle(document.documentElement).getPropertyValue(
        "--rs-topbar-height",
      ),
    ) || 0
  );
}

type LibraryState = {
  addCategoryOpen: boolean;
  draggingUpload: boolean;
  query: string;
  tag: string | null;
  legend: string | null;
};

type LibraryAction =
  | { type: "setAddCategoryOpen"; value: boolean }
  | { type: "setDraggingUpload"; value: boolean }
  | { type: "setQuery"; value: string }
  | { type: "setTag"; value: string | null }
  | { type: "setLegend"; value: string | null };

function libraryReducer(
  state: LibraryState,
  action: LibraryAction,
): LibraryState {
  switch (action.type) {
    case "setAddCategoryOpen":
      return { ...state, addCategoryOpen: action.value };
    case "setDraggingUpload":
      return { ...state, draggingUpload: action.value };
    case "setQuery":
      return { ...state, query: action.value };
    case "setTag":
      return { ...state, tag: action.value };
    case "setLegend":
      return { ...state, legend: action.value };
  }
}

/**
 * The library: a bookcase of games over the clip list. The route decides which book is pulled out:
 * `/` leaves every book on the shelf, `/games/$slug` pulls that game's book out and narrows the
 * clips to it, so pulling books in and out is ordinary navigation with a working back button.
 */
export function LibraryPage({ selectedSlug }: { selectedSlug: string | null }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { addClips } = useUploads();
  const twitch = useTwitchSuggestions();
  const addFromIgdb = useAddGameFromIgdb();
  const fileInputRef = useRef<HTMLInputElement>(null);
  // Clips dropped on New book wait here while their game is chosen.
  const [pendingClips, setPendingClips] = useState<BulkUploadFileInput[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const { categories, categoryTotals, totals, isLoading, isError } =
    useLibraryData();
  const [state, dispatch] = useReducer(libraryReducer, {
    addCategoryOpen: false,
    draggingUpload: false,
    query: "",
    tag: null,
    legend: null,
  });
  const dragDepthRef = useRef(0);
  // The page stays mounted as books are pulled in and out, so a new game starts with a clean
  // search and no tag or legend, as it would on arriving fresh.
  const [shownSlug, setShownSlug] = useState(selectedSlug);
  if (shownSlug !== selectedSlug) {
    setShownSlug(selectedSlug);
    dispatch({ type: "setQuery", value: "" });
    dispatch({ type: "setTag", value: null });
    dispatch({ type: "setLegend", value: null });
  }
  const [topSection, setTopSection] = useState<HTMLElement | null>(null);
  const [clipsSection, setClipsSection] = useState<HTMLElement | null>(null);
  const [pastShelf, setPastShelf] = useState(false);
  // A fresh object per navigation, so the scroll below runs once each time the bar is used.
  const historyState = useRouterState({
    select: (router) => router.location.state,
  });
  const pathname = useRouterState({
    select: (router) => router.location.pathname.replace(/(.)\/$/, "$1"),
  });
  // During a navigation the outgoing page still sees the new location; only the page the location
  // now names should act on it.
  const isCurrentPage = selectedSlug
    ? pathname === `/games/${selectedSlug}`
    : pathname === "/";
  const shelf = useMemo(
    () => makeGameShelf(categories, categoryTotals),
    [categories, categoryTotals],
  );
  const selected = selectedSlug
    ? (shelf.find((game) => game.slug === selectedSlug) ?? null)
    : null;
  const selectedId = selected?.id ?? null;
  // On a game's page, wait for the library to say which category it is before asking for its tags.
  const tags = (
    useTopTags(selectedId, !selectedSlug || selectedId !== null).data ?? []
  ).slice(0, 8);
  // Only the Apex book filters by legend, the one game whose clips have them.
  const apexBook = selectedSlug === APEX_LEGENDS_SLUG;
  const legends = useClipLegends(apexBook).data ?? [];
  const legend = apexBook ? state.legend : null;
  const selectedCloth = selected ? bookBinding(selected).cloth : undefined;
  // The grid queries the API, so let typing settle before asking for a new page of results.
  const search = useDebouncedValue(state.query);
  const filters = useMemo(
    () => ({ categoryId: selectedId, tag: state.tag, search, legend }),
    [search, selectedId, state.tag, legend],
  );

  // The bar takes over once the whole shelf and its header have scrolled up under the topbar.
  useEffect(() => {
    if (!topSection || typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(
      ([entry]) => setPastShelf(!entry.isIntersecting),
      { rootMargin: `-${topbarHeight()}px 0px 0px 0px` },
    );
    observer.observe(topSection);
    return () => observer.disconnect();
  }, [topSection]);

  // A game chosen from the bar opens at the top of its clips rather than back up at the shelf.
  // The list's scroll-margin keeps it clear of the topbar and the bar; waiting a frame lets the
  // new game's shelf and header lay out first.
  useEffect(() => {
    if (!clipsSection || !historyState.toClips || !isCurrentPage) return;
    const frame = requestAnimationFrame(() =>
      clipsSection.scrollIntoView({ block: "start" }),
    );
    return () => cancelAnimationFrame(frame);
  }, [clipsSection, historyState, isCurrentPage]);

  if (isLoading)
    return <div className="rs-section rs-empty">Loading your archive…</div>;
  if (isError)
    return (
      <div className="rs-section rs-empty">
        The archive could not be loaded.
      </div>
    );
  if (selectedSlug && !selected)
    return (
      <div className="rs-section rs-empty">This game could not be found.</div>
    );

  // With a book pulled out, its banner names the page; the greeting steps down to a paragraph.
  const Greeting = selected ? "p" : "h1";

  const selectGame = (game: GameShelfItem) => {
    dispatch({ type: "setTag", value: null });
    void navigate({ to: "/games/$slug", params: { slug: game.slug } });
  };

  const chooseFromBar = (game: GameShelfItem | null) => {
    dispatch({ type: "setTag", value: null });
    void (game
      ? navigate({
          to: "/games/$slug",
          params: { slug: game.slug },
          resetScroll: false,
          state: { toClips: true },
        })
      : navigate({ to: "/", resetScroll: false, state: { toClips: true } }));
  };

  const putBack = () => {
    dispatch({ type: "setTag", value: null });
    void navigate({ to: "/" });
  };

  const twitchCounts = new Map(
    [...twitch.byCategory].map(([id, clips]) => [id, clips.length]),
  );

  /** Puts clips into a book; anything that can't be uploaded is mentioned instead. */
  const fileInto = (inputs: BulkUploadFileInput[], categoryId: string) => {
    if (!inputs.length) return;
    const rejected = addClips(inputs, categoryId);
    setNotice(
      rejected.length
        ? `${rejected[0].message}${rejected.length > 1 ? ` ${rejected.length - 1} more skipped.` : ""}`
        : null,
    );
  };

  /** Opens a book once the library knows about it, so its page never says it can't be found. */
  const openBook = async (category: GameCategoryResponse) => {
    await queryClient.refetchQueries({ queryKey: ["clips", "library"] });
    void queryClient.invalidateQueries({ queryKey: twitchSuggestionsKey });
    void navigate({ to: "/games/$slug", params: { slug: category.slug } });
  };

  const addGhost = (ghost: TwitchGhost, inputs: BulkUploadFileInput[] = []) =>
    addFromIgdb.mutate(ghost.igdbId, {
      onSuccess: (category) => {
        fileInto(inputs, category.id);
        void openBook(category);
      },
    });

  // Read the dropped files while the drop event is still live; the browser forgets them after.
  const readDrop = (data: DataTransfer) =>
    bulkUploadInputsFromDataTransfer(data);

  function onDragEnter(event: DragEvent<HTMLElement>) {
    if (!dataTransferHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    dragDepthRef.current += 1;
    dispatch({ type: "setDraggingUpload", value: true });
  }

  function onDragOver(event: DragEvent<HTMLElement>) {
    if (!dataTransferHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    dispatch({ type: "setDraggingUpload", value: true });
  }

  function onDragLeave(event: DragEvent<HTMLElement>) {
    if (!dataTransferHasFiles(event.dataTransfer)) return;
    dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
    if (dragDepthRef.current === 0) {
      dispatch({ type: "setDraggingUpload", value: false });
    }
  }

  function endDrag() {
    dragDepthRef.current = 0;
    dispatch({ type: "setDraggingUpload", value: false });
  }

  // Books on the shelf take their own drops; this one is for the open book's page.
  async function onDrop(event: DragEvent<HTMLElement>) {
    if (!dataTransferHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    endDrag();
    if (!selected) {
      setNotice("Drop clips onto a book, or onto New book for another game.");
      return;
    }
    fileInto(await readDrop(event.dataTransfer), selected.id);
  }

  return (
    <main
      className="rs-library-route"
      onDragEnter={onDragEnter}
      onDragLeave={onDragLeave}
      onDragOver={onDragOver}
      onDrop={onDrop}
    >
      {state.draggingUpload ? (
        <div className="rs-drop-hint" role="status">
          {selected ? (
            <>
              Drop anywhere to add to <strong>{selected.name}</strong>
              <span> · or onto another book</span>
            </>
          ) : (
            <>
              Drop on a book to add these clips
              <span> · or on New book for another game</span>
            </>
          )}
        </div>
      ) : null}
      <input
        ref={fileInputRef}
        className="rs-visually-hidden"
        type="file"
        accept="video/mp4,video/quicktime,video/webm,video/x-matroska"
        multiple
        aria-label="Choose clips to add"
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? []);
          event.currentTarget.value = "";
          if (selected) fileInto(files, selected.id);
        }}
      />

      <section className="rs-library-top" ref={setTopSection}>
        {/* The greeting heads the page whether or not a book is pulled out, so choosing a game
            never moves the shelf; the pulled-out book's banner opens under it. */}
        <div className="rs-library-greeting">
          <div className="rs-eyebrow">
            Your archive - <StatLine totals={totals} />
          </div>
          <Greeting className="rs-display rs-h1">
            Welcome back. <em>{totals.unviewedCount || "No"} new clips</em> are
            waiting on the shelf.
          </Greeting>
        </div>
        <Bookcase
          shelf={shelf}
          selectedId={selectedId}
          onSelect={selectGame}
          onPutBack={putBack}
          onAddGame={() =>
            dispatch({ type: "setAddCategoryOpen", value: true })
          }
          dropping={state.draggingUpload}
          onDropOnBook={(game, data) => {
            endDrag();
            void readDrop(data).then((inputs) => fileInto(inputs, game.id));
          }}
          onDropOnNewBook={(data) => {
            endDrag();
            void readDrop(data).then((inputs) => {
              if (!inputs.length) return;
              setPendingClips(inputs);
              dispatch({ type: "setAddCategoryOpen", value: true });
            });
          }}
          twitchCounts={twitchCounts}
          ghosts={twitch.ghosts}
          onAddGhost={(ghost) => addGhost(ghost)}
          onDismissGhost={(ghost) => twitch.dismissGame(ghost.igdbId)}
          onDropOnGhost={(ghost, data) => {
            endDrag();
            void readDrop(data).then((inputs) => addGhost(ghost, inputs));
          }}
        />
        {notice ? (
          <p className="rs-drop-notice" role="status">
            {notice}
          </p>
        ) : null}

        {selected ? (
          <GameBanner
            game={selected}
            totals={categoryTotalsFor(categoryTotals, selected.id)}
            onUpload={() => fileInputRef.current?.click()}
            onPutBack={putBack}
          />
        ) : null}
        {selected && twitch.byCategory.get(selected.id)?.length ? (
          <TwitchBookStrip
            key={selected.id}
            categoryId={selected.id}
            clips={twitch.byCategory.get(selected.id) ?? []}
          />
        ) : null}
      </section>

      <ShelfBar
        visible={pastShelf}
        shelf={shelf}
        selected={selected}
        unviewedTotal={totals.unviewedCount}
        clipTotal={totals.clipCount}
        query={state.query}
        onQueryChange={(value) => dispatch({ type: "setQuery", value })}
        onChoose={chooseFromBar}
        onBackToShelf={() => window.scrollTo({ top: 0, behavior: "smooth" })}
      />

      {selected?.clipCount === 0 ? null : (
        <>
          <section className="rs-filterbar">
            {/* Tags only appear once there are some; a library without any has nothing to filter by. */}
            <div className="rs-filter-chips">
              {tags.length > 0 ? (
                <>
                  <span className="rs-eyebrow rs-filter-label">Tags</span>
                  <Chip
                    active={!state.tag}
                    onClick={() => dispatch({ type: "setTag", value: null })}
                  >
                    All tags
                  </Chip>
                </>
              ) : null}
              {tags.map((tag) => (
                <Chip
                  key={tag.name}
                  active={state.tag === tag.name}
                  onClick={() =>
                    dispatch({
                      type: "setTag",
                      value: state.tag === tag.name ? null : tag.name,
                    })
                  }
                >
                  #{tag.name}{" "}
                  <span className="rs-muted-count">{tag.count}</span>
                </Chip>
              ))}
            </div>
            <SearchBox
              value={state.query}
              onChange={(value) => dispatch({ type: "setQuery", value })}
            />
            {/* Its own row under the tags and search: there can be a couple of dozen legends. */}
            {apexBook && legends.length > 0 ? (
              <div className="rs-filter-chips rs-legend-chips">
                <span className="rs-eyebrow rs-filter-label">Legends</span>
                <Chip
                  active={!legend}
                  onClick={() => dispatch({ type: "setLegend", value: null })}
                >
                  All legends
                </Chip>
                {legends.map((item) => (
                  <Chip
                    key={item.legend}
                    active={legend === item.legend}
                    onClick={() =>
                      dispatch({
                        type: "setLegend",
                        value: legend === item.legend ? null : item.legend,
                      })
                    }
                  >
                    <span className="rs-legend-chip">
                      <LegendPlate
                        legend={item.legend}
                        cloth={selectedCloth}
                        size="small"
                      />
                      {item.legend}{" "}
                      <span className="rs-muted-count">{item.count}</span>
                    </span>
                  </Chip>
                ))}
              </div>
            ) : null}
          </section>

          <section className="rs-section rs-clips" ref={setClipsSection}>
            <PagedClipGrid
              filters={filters}
              categories={categories}
              variant="filmstrip"
              groupByMonth
              showGame={!selected}
            />
          </section>
        </>
      )}

      <AddCategoryModal
        opened={state.addCategoryOpen}
        pendingClipCount={pendingClips.length}
        onClose={() => {
          setPendingClips([]);
          dispatch({ type: "setAddCategoryOpen", value: false });
        }}
        onAdded={(category) => {
          fileInto(pendingClips, category.id);
          void openBook(category);
        }}
      />
    </main>
  );
}
