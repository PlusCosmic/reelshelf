import { createFileRoute, useNavigate } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useMemo, useState } from "react";
import { IconPlus } from "@tabler/icons-react";
import { AddCategoryModal } from "@/components/AddCategoryModal";
import { Chip, ClipGrid, SearchBox, StatLine } from "@/components/Reelshelf/ReelshelfPrimitives";
import { makeGameShelf, newestClips, topTags } from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/")({
  component: LibraryRoute,
});

function LibraryRoute() {
  const navigate = useNavigate();
  const { categories, clips, isLoading, isError } = useLibraryData();
  const [gameId, setGameId] = useState<string | null>(null);
  const [tag, setTag] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [addCategoryOpen, setAddCategoryOpen] = useState(false);
  const shelf = useMemo(() => makeGameShelf(categories, clips), [categories, clips]);
  const tags = useMemo(() => topTags(clips, 8), [clips]);

  const filtered = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return newestClips(clips).filter((clip) => {
      const category = categories.find((item) => item.id === clip.gameCategoryId);
      if (gameId && clip.gameCategoryId !== gameId) return false;
      if (tag && !clip.tags.includes(tag)) return false;
      if (!normalizedQuery) return true;
      return (
        clip.video.title.toLowerCase().includes(normalizedQuery) ||
        clip.tags.some((clipTag) => clipTag.toLowerCase().includes(normalizedQuery)) ||
        category?.name.toLowerCase().includes(normalizedQuery)
      );
    });
  }, [categories, clips, gameId, query, tag]);

  if (isLoading) return <div className="rs-section rs-empty">Loading your archive...</div>;
  if (isError) return <div className="rs-section rs-empty">The archive could not be loaded.</div>;

  return (
    <>
      <section className="rs-hero">
        <div>
          <div>
            <div className="rs-eyebrow">
              Your archive - <StatLine clips={clips} />
            </div>
            <h1 className="rs-display rs-h1">
              Welcome back. <em>{clips.filter((clip) => !clip.isViewed).length || "No"} new clips</em> are waiting on the shelf.
            </h1>
          </div>
        </div>

        <div className="rs-shelf">
          <div className="rs-shelf-track">
            {shelf.map((game) => (
              <button
                key={game.id}
                className={`rs-spine${gameId === game.id ? " active" : ""}`}
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
                onClick={() => navigate({ to: "/games/$slug", params: { slug: game.slug } })}
              >
                <span>{game.name}</span>
                <span>{game.clipCount}</span>
              </button>
            ))}
            <button
              className="rs-spine rs-spine-add"
              type="button"
              title="Add a new book"
              onClick={() => setAddCategoryOpen(true)}
              aria-label="Add a new book"
            >
              <IconPlus size={18} aria-hidden="true" />
            </button>
          </div>
        </div>
      </section>

      <section className="rs-filterbar">
        <span className="rs-eyebrow" style={{ margin: "0 6px 0 0" }}>
          Filter
        </span>
        <Chip active={!gameId} onClick={() => setGameId(null)}>
          All games
        </Chip>
        {shelf.slice(0, 6).map((game) => (
          <Chip key={game.id} active={gameId === game.id} onClick={() => setGameId(gameId === game.id ? null : game.id)}>
            {game.name} <span style={{ opacity: 0.62 }}>{game.clipCount}</span>
          </Chip>
        ))}
        <span style={{ width: 1, height: 18, background: "var(--line)", margin: "0 6px" }} />
        <Chip active={!tag} onClick={() => setTag(null)}>
          All tags
        </Chip>
        {tags.map(([tagName, count]) => (
          <Chip key={tagName} active={tag === tagName} onClick={() => setTag(tag === tagName ? null : tagName)}>
            #{tagName} <span style={{ opacity: 0.62 }}>{count}</span>
          </Chip>
        ))}
        <div style={{ flex: 1 }} />
        <SearchBox value={query} onChange={setQuery} />
      </section>

      <section className="rs-section">
        <ClipGrid clips={filtered} categories={categories} variant="filmstrip" />
      </section>

      <AddCategoryModal opened={addCategoryOpen} onClose={() => setAddCategoryOpen(false)} />
    </>
  );
}
