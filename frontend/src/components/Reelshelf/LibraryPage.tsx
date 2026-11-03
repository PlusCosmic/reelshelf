import { Link, useNavigate, useRouterState } from "@tanstack/react-router";
import { useQueryClient } from "@tanstack/react-query";
import type { DragEvent } from "react";
import {
  IconArrowsHorizontal,
  IconCards,
  IconCheck,
  IconListCheck,
  IconTrash,
} from "@tabler/icons-react";
import {
  useEffect,
  useEffectEvent,
  useMemo,
  useReducer,
  useRef,
  useState,
} from "react";
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
import { binCopy } from "@/copy/bin";
import { libraryCopy } from "@/copy/library";
import {
  useAddGameFromIgdb,
  useRemoveShelfBook,
  useSetShelfOrder,
} from "@/hooks/queries";
import {
  useClipLegends,
  useClipsInfinite,
  useTopTags,
} from "@/hooks/clips.queries";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import {
  bulkUploadInputsFromDataTransfer,
  dataTransferHasFiles,
} from "@/utils/bulkUploadDrop";
import { Bookcase } from "./Bookcase";
import { useBin } from "./BinToast";
import { chosenCount, useClipSelection } from "./clipSelection";
import {
  clipListSearchFor,
  readLibrarySearch,
  withoutEmpty,
  type LibrarySearch,
} from "./libraryList";
import { SelectionBar } from "./SelectionBar";
import { isShortcutKey, startSortTally } from "./sortThrough";
import { GameBanner } from "./GameBanner";
import { ShelfBar } from "./ShelfBar";
import { LegendPicker } from "./LegendPicker";
import { RemoveBookDialog } from "./RemoveBookDialog";
import {
  Chip,
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
    /** Set on a clip opened from a library list, so going back returns to that list where it was. */
    fromList?: boolean;
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
};

type LibraryAction =
  | { type: "setAddCategoryOpen"; value: boolean }
  | { type: "setDraggingUpload"; value: boolean };

function libraryReducer(
  state: LibraryState,
  action: LibraryAction,
): LibraryState {
  switch (action.type) {
    case "setAddCategoryOpen":
      return { ...state, addCategoryOpen: action.value };
    case "setDraggingUpload":
      return { ...state, draggingUpload: action.value };
  }
}

/**
 * The library: a bookcase of games over the clip list. The route decides which book is pulled out:
 * `/` leaves every book on the shelf, `/games/$slug` pulls that game's book out and narrows the
 * clips to it, so pulling books in and out is ordinary navigation with a working back button. The list's search,
 * tag and legend are in the URL too (`LibrarySearch`), so going back to it from a clip finds it as it was left.
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
  const [arranging, setArranging] = useState(false);
  const [removing, setRemoving] = useState<GameShelfItem | null>(null);
  // The book vanishing says it went; a screen reader hears it here.
  const [shelfStatus, setShelfStatus] = useState("");
  const setShelfOrder = useSetShelfOrder();
  const removeShelfBook = useRemoveShelfBook();
  const {
    categories,
    categoryTotals,
    totals,
    binnedCount,
    isLoading,
    isError,
  } = useLibraryData();
  const [state, dispatch] = useReducer(libraryReducer, {
    addCategoryOpen: false,
    draggingUpload: false,
  });
  const dragDepthRef = useRef(0);
  // A new game is a new URL with no filters, so it starts with a clean search and no tag or legend.
  const urlSearch = useRouterState({
    select: (router): LibrarySearch =>
      readLibrarySearch(router.location.search as Record<string, unknown>),
  });
  const { bin, pending: binning } = useBin();
  const [selection, select] = useClipSelection();
  // The search box answers each key at once; the URL (and the list) follow once typing settles.
  const [query, setQuery] = useState(urlSearch.q ?? "");
  const writtenQuery = useRef(urlSearch.q ?? "");
  const [shownUrlQuery, setShownUrlQuery] = useState(urlSearch.q ?? "");
  if (shownUrlQuery !== (urlSearch.q ?? "")) {
    setShownUrlQuery(urlSearch.q ?? "");
    // Back, forward or another book changed the URL's search: show it, unless it is what this box just wrote.
    if ((urlSearch.q ?? "") !== writtenQuery.current)
      setQuery(urlSearch.q ?? "");
  }
  const debouncedQuery = useDebouncedValue(query);
  const listKey = `${selectedSlug ?? ""}|${urlSearch.q ?? ""}|${urlSearch.tag ?? ""}|${urlSearch.legend ?? ""}`;
  const [shownListKey, setShownListKey] = useState(listKey);
  if (shownListKey !== listKey) {
    setShownListKey(listKey);
    // Choices belong to the list they were made in.
    select({ type: "clear" });
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
  const legend = apexBook ? (urlSearch.legend ?? null) : null;
  const tag = urlSearch.tag ?? null;
  const selectedCloth = selected ? bookBinding(selected).cloth : undefined;
  const search = urlSearch.q;
  const filters = useMemo(
    () => ({ categoryId: selectedId, tag, search, legend }),
    [search, selectedId, tag, legend],
  );
  // The same query the grid pages through (it shares the cache), for the order of its rows and how many match.
  const list = useClipsInfinite(filters, !selectedSlug || selectedId !== null);
  const listClips = useMemo(
    () => list.data?.pages.flatMap((page) => page.clips) ?? [],
    [list.data],
  );
  const order = useMemo(
    () => listClips.map((clip) => clip.clipId),
    [listClips],
  );
  const matching = list.data?.pages[0]?.totalClips ?? 0;
  const linkSearch = clipListSearchFor(
    withoutEmpty({
      q: search,
      tag: tag ?? undefined,
      legend: legend ?? undefined,
    }),
    !!selected,
  );

  /** Changes the list's filters in place: the URL changes, but no new history entry is added for each one. */
  const setFilters = (
    patch: Partial<Record<keyof LibrarySearch, string | null>>,
  ) => {
    const next = withoutEmpty({
      q: "q" in patch ? (patch.q ?? undefined) : urlSearch.q,
      tag: "tag" in patch ? (patch.tag ?? undefined) : urlSearch.tag,
      legend:
        "legend" in patch ? (patch.legend ?? undefined) : urlSearch.legend,
    });
    if ("q" in patch) writtenQuery.current = next.q ?? "";
    void navigate({
      to: ".",
      search: next as never,
      replace: true,
      resetScroll: false,
    });
  };

  // Typing settles into the URL, and so into the list.
  const writeQuery = useEffectEvent((value: string) => {
    if (value.trim() !== (urlSearch.q ?? ""))
      setFilters({ q: value.trim() || null });
  });
  useEffect(() => {
    if (isCurrentPage) writeQuery(debouncedQuery);
  }, [debouncedQuery, isCurrentPage]);

  const chosen = chosenCount(selection, matching);
  const binChosen = async () => {
    if (chosen === 0 || binning) return;
    const target = selection.allMatching
      ? {
          filter: {
            categoryId: selectedId,
            tags: tag ? [tag] : null,
            search: search ?? null,
            legend,
          },
        }
      : { clipIds: [...selection.selected] };
    try {
      await bin(target);
      select({ type: "clear" });
    } catch {
      // The note says it didn't go; the choice stays for another try.
    }
  };

  /** Opens the list's first clip to keep or bin each in turn. */
  const sortThrough = () => {
    const first = listClips[0];
    if (!first) return;
    startSortTally();
    select({ type: "clear" });
    void navigate({
      to: "/games/$slug/$clipId",
      params: { slug: first.categorySlug, clipId: first.clipId },
      search: { ...linkSearch, sort: true },
      state: { fromList: true },
    });
  };

  // While choosing: X chooses the row in focus, J and K move between rows, Delete bins what's chosen, and Escape
  // stops. Rows are links, so focus moves between them as a keyboard user would expect.
  const onListKey = useEffectEvent((event: KeyboardEvent) => {
    if (!isShortcutKey(event) || !isCurrentPage) return;
    const rows = [
      ...document.querySelectorAll<HTMLElement>(".rs-clips [data-clip-id]"),
    ];
    const focused = (
      document.activeElement as HTMLElement | null
    )?.closest<HTMLElement>("[data-clip-id]");
    const at = focused ? rows.indexOf(focused) : -1;
    const key = event.key.toLowerCase();
    if (key === "escape" && selection.active) {
      select({ type: "clear" });
    } else if ((key === "j" || key === "k") && rows.length > 0) {
      event.preventDefault();
      const next =
        rows[
          Math.max(0, Math.min(rows.length - 1, at + (key === "j" ? 1 : -1)))
        ];
      next.querySelector<HTMLElement>("a")?.focus();
      next.scrollIntoView({ block: "nearest" });
    } else if (key === "x" && focused) {
      event.preventDefault();
      select({
        type: "toggle",
        clipId: focused.dataset.clipId!,
        range: event.shiftKey,
        order,
      });
    } else if (
      (key === "delete" || key === "backspace" || key === "#") &&
      chosen > 0
    ) {
      event.preventDefault();
      void binChosen();
    } else {
      return;
    }
  });
  useEffect(() => {
    const listener = (event: KeyboardEvent) => onListKey(event);
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);

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

  // Escape puts the shelf down, unless a dialog over it has the key.
  useEffect(() => {
    if (!arranging || removing) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setArranging(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [arranging, removing]);

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
    return <div className="rs-section rs-empty">{libraryCopy.loading}</div>;
  if (isError)
    return <div className="rs-section rs-empty">{libraryCopy.loadFailed}</div>;
  if (selectedSlug && !selected)
    return (
      <div className="rs-section rs-empty">{libraryCopy.gameNotFound}</div>
    );

  // With a book pulled out, its banner names the page; the greeting steps down to a paragraph.
  const Greeting = selected ? "p" : "h1";

  const selectGame = (game: GameShelfItem) => {
    void navigate({ to: "/games/$slug", params: { slug: game.slug } });
  };

  const chooseFromBar = (game: GameShelfItem | null) => {
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
    void navigate({ to: "/" });
  };

  const reorderShelf = (ids: string[]) =>
    setShelfOrder.mutate(ids, {
      onSuccess: () => setNotice(null),
      onError: () => setNotice(libraryCopy.bookcase.arrange.orderFailed),
    });

  /** Takes an empty book off the shelf; on its own page, the library is shown first. */
  const removeBook = async (game: GameShelfItem) => {
    if (game.id === selectedId) await navigate({ to: "/" });
    await removeShelfBook.mutateAsync(game.id);
    setShelfStatus(libraryCopy.removeBook.removed(game.name));
    // A book with Twitch clips waiting comes back as a Twitch book.
    void queryClient.invalidateQueries({ queryKey: twitchSuggestionsKey });
  };

  // Arranging only appears once there is something to arrange: two books, or an empty one.
  const canArrange =
    shelf.length > 1 || shelf.some((game) => game.clipCount === 0);
  const shelfFooter = !canArrange ? null : arranging ? (
    <>
      <p className="rs-bookcase-arrange-hint">
        {libraryCopy.bookcase.arrange.hint}
        {shelf.some((game) => game.clipCount === 0)
          ? ` ${libraryCopy.bookcase.arrange.removeHint}`
          : null}
      </p>
      <button
        type="button"
        className="rs-bookcase-arrange done"
        onClick={() => setArranging(false)}
      >
        <IconCheck size={16} aria-hidden="true" />
        {libraryCopy.bookcase.arrange.done}
      </button>
    </>
  ) : (
    <button
      type="button"
      className="rs-bookcase-arrange"
      onClick={() => {
        setNotice(null);
        setArranging(true);
      }}
    >
      <IconArrowsHorizontal size={16} aria-hidden="true" />
      {libraryCopy.bookcase.arrange.start}
    </button>
  );

  const twitchCounts = new Map(
    [...twitch.byCategory].map(([id, clips]) => [id, clips.length]),
  );

  /** Puts clips into a book; anything that can't be uploaded is mentioned instead. */
  const fileInto = (inputs: BulkUploadFileInput[], categoryId: string) => {
    if (!inputs.length) return;
    const rejected = addClips(inputs, categoryId);
    setNotice(
      rejected.length
        ? libraryCopy.rejectedNotice(rejected[0].message, rejected.length - 1)
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
      setNotice(libraryCopy.dropOutsideBook);
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
              {libraryCopy.dropHint.intoBook(selected.name)}
              <span>{libraryCopy.dropHint.orAnotherBook}</span>
            </>
          ) : (
            <>
              {libraryCopy.dropHint.ontoShelf}
              <span>{libraryCopy.dropHint.orNewBook}</span>
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
        aria-label={libraryCopy.chooseClips}
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
            {libraryCopy.greeting.eyebrow(<StatLine totals={totals} />)}
          </div>
          <Greeting className="rs-display rs-h1">
            {libraryCopy.greeting.title(totals.unviewedCount)}
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
          footer={shelfFooter}
          arranging={arranging && canArrange}
          onReorder={reorderShelf}
          onRemoveBook={setRemoving}
        />
        <p className="rs-visually-hidden" role="status">
          {shelfStatus}
        </p>
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
            onRemove={() => setRemoving(selected)}
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
        query={query}
        onQueryChange={setQuery}
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
                  <span className="rs-eyebrow rs-filter-label">
                    {libraryCopy.filters.tags}
                  </span>
                  <Chip active={!tag} onClick={() => setFilters({ tag: null })}>
                    {libraryCopy.filters.allTags}
                  </Chip>
                </>
              ) : null}
              {tags.map((topTag) => (
                <Chip
                  key={topTag.name}
                  active={tag === topTag.name}
                  onClick={() =>
                    setFilters({
                      tag: tag === topTag.name ? null : topTag.name,
                    })
                  }
                >
                  #{topTag.name}{" "}
                  <span className="rs-muted-count">{topTag.count}</span>
                </Chip>
              ))}
            </div>
            {apexBook && legends.length > 0 ? (
              <LegendPicker
                legends={legends}
                value={legend}
                onChange={(value) => setFilters({ legend: value })}
                cloth={selectedCloth}
              />
            ) : null}
            <SearchBox value={query} onChange={setQuery} />
            <div className="rs-list-tools">
              {matching > 0 ? (
                <>
                  <button
                    type="button"
                    className="rs-small-button"
                    aria-pressed={selection.active}
                    onClick={() =>
                      select({ type: selection.active ? "clear" : "start" })
                    }
                  >
                    <IconListCheck size={13} aria-hidden="true" />
                    {binCopy.selection.start}
                  </button>
                  <button
                    type="button"
                    className="rs-small-button"
                    aria-label={binCopy.sort.startLabel(matching)}
                    onClick={sortThrough}
                  >
                    <IconCards size={13} aria-hidden="true" />
                    {binCopy.sort.start}
                  </button>
                </>
              ) : null}
              {binnedCount > 0 ? (
                <Link
                  to="/bin"
                  className="rs-small-button rs-bin-link"
                  aria-label={binCopy.libraryLinkLabel(binnedCount)}
                >
                  <IconTrash size={13} aria-hidden="true" />
                  {binCopy.libraryLink(binnedCount)}
                </Link>
              ) : null}
            </div>
          </section>

          <section className="rs-section rs-clips" ref={setClipsSection}>
            <PagedClipGrid
              filters={filters}
              categories={categories}
              variant="filmstrip"
              groupByMonth
              showGame={!selected}
              linkSearch={linkSearch}
              choosing={{
                selection,
                onToggle: (clipId, range) =>
                  select({ type: "toggle", clipId, range, order }),
                onSet: (clipIds, value) =>
                  select({ type: "set", clipIds, selected: value, order }),
              }}
            />
          </section>
          {selection.active ? (
            <SelectionBar
              chosen={chosen}
              matching={matching}
              loaded={order.length}
              allMatching={selection.allMatching}
              binning={binning}
              onSelectAll={() => select({ type: "allMatching" })}
              onBin={() => void binChosen()}
              onClear={() => select({ type: "clear" })}
            />
          ) : null}
        </>
      )}

      {removing ? (
        <RemoveBookDialog
          game={removing}
          pending={removeShelfBook.isPending}
          onConfirm={() => removeBook(removing)}
          onClose={() => setRemoving(null)}
        />
      ) : null}

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
