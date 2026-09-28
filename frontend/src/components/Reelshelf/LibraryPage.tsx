import { useNavigate, useRouterState } from "@tanstack/react-router";
import type { DragEvent } from "react";
import { useEffect, useMemo, useReducer, useRef, useState } from "react";
import { AddCategoryModal } from "@/components/AddCategoryModal";
import { useTopTags } from "@/hooks/clips.queries";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { setPendingBulkUploadEntry } from "@/utils/bulkUploadEntry";
import {
  bulkUploadInputsFromDataTransfer,
  dataTransferHasFiles,
} from "@/utils/bulkUploadDrop";
import { Bookcase } from "./Bookcase";
import { GameBanner } from "./GameBanner";
import { ShelfBar } from "./ShelfBar";
import {
  Chip,
  PagedClipGrid,
  SearchBox,
  StatLine,
} from "./ReelshelfPrimitives";
import {
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
};

type LibraryAction =
  | { type: "setAddCategoryOpen"; value: boolean }
  | { type: "setDraggingUpload"; value: boolean }
  | { type: "setQuery"; value: string }
  | { type: "setTag"; value: string | null };

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
  }
}

/**
 * The library: a bookcase of games over the clip list. The route decides which book is pulled out:
 * `/` leaves every book on the shelf, `/games/$slug` pulls that game's book out and narrows the
 * clips to it, so pulling books in and out is ordinary navigation with a working back button.
 */
export function LibraryPage({ selectedSlug }: { selectedSlug: string | null }) {
  const navigate = useNavigate();
  const { categories, categoryTotals, totals, isLoading, isError } =
    useLibraryData();
  const [state, dispatch] = useReducer(libraryReducer, {
    addCategoryOpen: false,
    draggingUpload: false,
    query: "",
    tag: null,
  });
  const dragDepthRef = useRef(0);
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
  // The grid queries the API, so let typing settle before asking for a new page of results.
  const search = useDebouncedValue(state.query);
  const filters = useMemo(
    () => ({ categoryId: selectedId, tag: state.tag, search }),
    [search, selectedId, state.tag],
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

  const openUpload = async (game: GameShelfItem | null) => {
    setPendingBulkUploadEntry({
      files: [],
      fallbackCategoryId: game?.id ?? null,
      source: game ? "game" : "library",
    });
    await navigate({ to: "/upload" });
  };

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

  async function onDrop(event: DragEvent<HTMLElement>) {
    if (!dataTransferHasFiles(event.dataTransfer)) return;
    event.preventDefault();
    dragDepthRef.current = 0;
    dispatch({ type: "setDraggingUpload", value: false });

    const files = await bulkUploadInputsFromDataTransfer(event.dataTransfer);
    if (!files.length) return;

    setPendingBulkUploadEntry({
      files,
      fallbackCategoryId: selectedId,
      source: selected ? "game" : "library",
    });
    await navigate({ to: "/upload" });
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
        <div className="rs-library-drop-overlay" role="status">
          <span className="rs-upload-drop-title">Drop clips to upload</span>
          <span className="rs-upload-drop-copy">
            Files and recorder folders will open in the bulk upload queue.
          </span>
        </div>
      ) : null}

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
        />

        {selected ? (
          <GameBanner
            game={selected}
            totals={categoryTotalsFor(categoryTotals, selected.id)}
            onUpload={() => void openUpload(selected)}
            onPutBack={putBack}
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
        onClose={() => dispatch({ type: "setAddCategoryOpen", value: false })}
      />
    </main>
  );
}
