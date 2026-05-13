import { createFileRoute, useNavigate } from "@tanstack/react-router";
import type { CSSProperties, DragEvent } from "react";
import { useMemo, useReducer, useRef } from "react";
import { IconPlus } from "@tabler/icons-react";
import { AddCategoryModal } from "@/components/AddCategoryModal";
import {
  Chip,
  ClipGrid,
  SearchBox,
  StatLine,
} from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  makeGameShelf,
  newestClips,
  topTags,
} from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";
import { setPendingBulkUploadEntry } from "@/utils/bulkUploadEntry";
import {
  bulkUploadInputsFromDataTransfer,
  dataTransferHasFiles,
} from "@/utils/bulkUploadDrop";

export const Route = createFileRoute("/")({
  component: LibraryRoute,
});

type LibraryRouteState = {
  addCategoryOpen: boolean;
  draggingUpload: boolean;
  gameId: string | null;
  query: string;
  tag: string | null;
};

type LibraryRouteAction =
  | { type: "setAddCategoryOpen"; value: boolean }
  | { type: "setDraggingUpload"; value: boolean }
  | { type: "setGameId"; value: string | null }
  | { type: "setQuery"; value: string }
  | { type: "setTag"; value: string | null };

function libraryRouteReducer(
  state: LibraryRouteState,
  action: LibraryRouteAction,
): LibraryRouteState {
  switch (action.type) {
    case "setAddCategoryOpen":
      return { ...state, addCategoryOpen: action.value };
    case "setDraggingUpload":
      return { ...state, draggingUpload: action.value };
    case "setGameId":
      return { ...state, gameId: action.value };
    case "setQuery":
      return { ...state, query: action.value };
    case "setTag":
      return { ...state, tag: action.value };
  }
}

function LibraryRoute() {
  const navigate = useNavigate();
  const { categories, clips, isLoading, isError } = useLibraryData();
  const [state, dispatch] = useReducer(libraryRouteReducer, {
    addCategoryOpen: false,
    draggingUpload: false,
    gameId: null,
    query: "",
    tag: null,
  });
  const dragDepthRef = useRef(0);
  const shelf = useMemo(
    () => makeGameShelf(categories, clips),
    [categories, clips],
  );
  const tags = useMemo(() => topTags(clips, 8), [clips]);

  const filtered = useMemo(() => {
    const normalizedQuery = state.query.trim().toLowerCase();
    return newestClips(clips).filter((clip) => {
      const category = categories.find(
        (item) => item.id === clip.gameCategoryId,
      );
      if (state.gameId && clip.gameCategoryId !== state.gameId) return false;
      if (state.tag && !clip.tags.includes(state.tag)) return false;
      if (!normalizedQuery) return true;
      return (
        clip.video.title.toLowerCase().includes(normalizedQuery) ||
        clip.tags.some((clipTag) =>
          clipTag.toLowerCase().includes(normalizedQuery),
        ) ||
        category?.name.toLowerCase().includes(normalizedQuery)
      );
    });
  }, [categories, clips, state.gameId, state.query, state.tag]);

  if (isLoading)
    return <div className="rs-section rs-empty">Loading your archive…</div>;
  if (isError)
    return (
      <div className="rs-section rs-empty">
        The archive could not be loaded.
      </div>
    );

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
      fallbackCategoryId: null,
      source: "library",
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

      <section className="rs-hero">
        <div>
          <div>
            <div className="rs-eyebrow">
              Your archive - <StatLine clips={clips} />
            </div>
            <h1 className="rs-display rs-h1">
              Welcome back.{" "}
              <em>
                {clips.filter((clip) => !clip.isViewed).length || "No"} new
                clips
              </em>{" "}
              are waiting on the shelf.
            </h1>
          </div>
        </div>

        <div className="rs-shelf">
          <div className="rs-shelf-track">
            {shelf.map((game) => (
              <button
                key={game.id}
                className={`rs-spine${state.gameId === game.id ? " active" : ""}`}
                type="button"
                style={
                  {
                    "--game-a": game.colorA,
                    "--game-b": game.colorB,
                    backgroundImage: game.coverUrl
                      ? `linear-gradient(to bottom, rgba(12, 9, 6, 0.08), rgba(12, 9, 6, 0.42)), url("${game.coverUrl}")`
                      : undefined,
                  } as CSSProperties
                }
                title={game.name}
                onClick={() =>
                  navigate({ to: "/games/$slug", params: { slug: game.slug } })
                }
              >
                <span>{game.name}</span>
                <span>{game.clipCount}</span>
              </button>
            ))}
            <button
              className="rs-spine rs-spine-add"
              type="button"
              title="Add a new book"
              onClick={() =>
                dispatch({ type: "setAddCategoryOpen", value: true })
              }
              aria-label="Add a new book"
            >
              <IconPlus size={18} aria-hidden="true" />
            </button>
          </div>
        </div>
      </section>

      <section className="rs-filterbar">
        <span className="rs-eyebrow rs-filter-label">Filter</span>
        <Chip
          active={!state.gameId}
          onClick={() => dispatch({ type: "setGameId", value: null })}
        >
          All games
        </Chip>
        {shelf.slice(0, 6).map((game) => (
          <Chip
            key={game.id}
            active={state.gameId === game.id}
            onClick={() =>
              dispatch({
                type: "setGameId",
                value: state.gameId === game.id ? null : game.id,
              })
            }
          >
            {game.name} <span className="rs-muted-count">{game.clipCount}</span>
          </Chip>
        ))}
        <span className="rs-filter-divider" />
        <Chip
          active={!state.tag}
          onClick={() => dispatch({ type: "setTag", value: null })}
        >
          All tags
        </Chip>
        {tags.map(([tagName, count]) => (
          <Chip
            key={tagName}
            active={state.tag === tagName}
            onClick={() =>
              dispatch({
                type: "setTag",
                value: state.tag === tagName ? null : tagName,
              })
            }
          >
            #{tagName} <span className="rs-muted-count">{count}</span>
          </Chip>
        ))}
        <div className="rs-filter-spacer" />
        <SearchBox
          value={state.query}
          onChange={(value) => dispatch({ type: "setQuery", value })}
        />
      </section>

      <section className="rs-section">
        <ClipGrid
          clips={filtered}
          categories={categories}
          variant="filmstrip"
        />
      </section>

      <AddCategoryModal
        opened={state.addCategoryOpen}
        onClose={() => dispatch({ type: "setAddCategoryOpen", value: false })}
      />
    </main>
  );
}
