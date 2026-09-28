import { useNavigate } from "@tanstack/react-router";
import type { DragEvent } from "react";
import { useMemo, useReducer, useRef } from "react";
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

  const selectGame = (game: GameShelfItem) => {
    dispatch({ type: "setTag", value: null });
    void navigate({ to: "/games/$slug", params: { slug: game.slug } });
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

      <section className="rs-library-top">
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
        ) : (
          <div className="rs-library-greeting">
            <div className="rs-eyebrow">
              Your archive - <StatLine totals={totals} />
            </div>
            <h1 className="rs-display rs-h1">
              Welcome back. <em>{totals.unviewedCount || "No"} new clips</em>{" "}
              are waiting on the shelf.
            </h1>
          </div>
        )}
      </section>

      {selected?.clipCount === 0 ? null : (
        <>
          <section className="rs-filterbar">
            <div className="rs-filter-chips">
              <span className="rs-eyebrow rs-filter-label">Tags</span>
              <Chip
                active={!state.tag}
                onClick={() => dispatch({ type: "setTag", value: null })}
              >
                All tags
              </Chip>
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

          <section className="rs-section">
            <PagedClipGrid
              filters={filters}
              categories={categories}
              variant="filmstrip"
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
