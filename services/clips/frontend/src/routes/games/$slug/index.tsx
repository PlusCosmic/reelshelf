import { createFileRoute } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useMemo, useState } from "react";
import { BackToLibrary, Chip, ClipGrid, StatLine } from "@/components/Reelshelf/ReelshelfPrimitives";
import { getGameColors, newestClips, topTags } from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/games/$slug/")({
  component: GameCategoryRoute,
});

function GameCategoryRoute() {
  const { slug } = Route.useParams();
  const { categories, clips, isLoading, isError } = useLibraryData();
  const [tag, setTag] = useState<string | null>(null);
  const [variant, setVariant] = useState<"poster" | "grid" | "filmstrip">("filmstrip");
  const category = categories.find((item) => item.slug === slug);
  const [colorA, colorB] = getGameColors(category?.id ?? slug);
  const gameClips = useMemo(
    () => newestClips(clips.filter((clip) => clip.categorySlug === slug || clip.gameCategoryId === category?.id)),
    [category?.id, clips, slug],
  );
  const tags = useMemo(() => topTags(gameClips, 10), [gameClips]);
  const filtered = tag ? gameClips.filter((clip) => clip.tags.includes(tag)) : gameClips;

  if (isLoading) return <div className="rs-section rs-empty">Loading game shelf...</div>;
  if (isError || !category) return <div className="rs-section rs-empty">This game could not be found.</div>;

  return (
    <>
      <section
        className="rs-game-hero"
        style={
          {
            "--game-a": colorA,
            "--game-b": colorB,
            backgroundImage: category.keyArtUrl
              ? `linear-gradient(90deg, rgba(15, 11, 8, 0.88), rgba(15, 11, 8, 0.46)), url("${category.keyArtUrl}")`
              : undefined,
          } as CSSProperties
        }
      >
        <BackToLibrary />
        <div className="rs-eyebrow" style={{ color: "rgba(255,255,255,.76)" }}>
          {category.isCustom ? "Custom category" : "Game category"} - <StatLine clips={gameClips} />
        </div>
        {category.gameLogoUrl ? (
          <img className="rs-game-logo" src={category.gameLogoUrl} alt={category.name} />
        ) : (
          <h1 className="rs-display rs-h1" style={{ color: "white" }}>
            {category.name}
          </h1>
        )}
        <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginTop: 24 }}>
          <Chip active={!tag} onClick={() => setTag(null)}>
            All clips
          </Chip>
          {tags.map(([tagName, count]) => (
            <Chip key={tagName} active={tag === tagName} onClick={() => setTag(tag === tagName ? null : tagName)}>
              #{tagName} - {count}
            </Chip>
          ))}
        </div>
      </section>

      <section className="rs-section">
        <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between", gap: 16, marginBottom: 24 }}>
          <h2 className="rs-display rs-h2">All clips</h2>
          <select
            value={variant}
            onChange={(event) => setVariant(event.currentTarget.value as typeof variant)}
            style={{
              border: "1px solid var(--line)",
              borderRadius: 8,
              background: "var(--bg-soft)",
              color: "var(--fg-soft)",
              padding: "7px 10px",
            }}
          >
            <option value="poster">Poster shelf</option>
            <option value="grid">Compact grid</option>
            <option value="filmstrip">Filmstrip</option>
          </select>
        </div>
        <ClipGrid clips={filtered} categories={categories} variant={variant} />
      </section>
    </>
  );
}
