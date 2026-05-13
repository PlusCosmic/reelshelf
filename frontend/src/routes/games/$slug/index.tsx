import { createFileRoute, useNavigate } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useMemo, useState } from "react";
import { IconUpload } from "@tabler/icons-react";
import {
  BackToLibrary,
  Chip,
  ClipGrid,
  StatLine,
} from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  getGameColors,
  newestClips,
  topTags,
} from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";
import { setPendingBulkUploadEntry } from "@/utils/bulkUploadEntry";

export const Route = createFileRoute("/games/$slug/")({
  component: GameCategoryRoute,
});

function GameCategoryRoute() {
  const { slug } = Route.useParams();
  const navigate = useNavigate();
  const { categories, clips, isLoading, isError } = useLibraryData();
  const [tag, setTag] = useState<string | null>(null);
  const category = categories.find((item) => item.slug === slug);
  const [colorA, colorB] = getGameColors(category?.id ?? slug);
  const gameClips = useMemo(
    () =>
      newestClips(
        clips.filter(
          (clip) =>
            clip.categorySlug === slug || clip.gameCategoryId === category?.id,
        ),
      ),
    [category?.id, clips, slug],
  );
  const tags = useMemo(() => topTags(gameClips, 10), [gameClips]);
  const filtered = tag
    ? gameClips.filter((clip) => clip.tags.includes(tag))
    : gameClips;

  if (isLoading)
    return <div className="rs-section rs-empty">Loading game shelf…</div>;
  if (isError || !category)
    return (
      <div className="rs-section rs-empty">This game could not be found.</div>
    );

  const categoryId = category.id;

  async function openGameUpload() {
    setPendingBulkUploadEntry({
      files: [],
      fallbackCategoryId: categoryId,
      source: "game",
    });
    await navigate({ to: "/upload" });
  }

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
        <div className="rs-eyebrow rs-over-media">
          {category.isCustom ? "Custom category" : "Game category"} -{" "}
          <StatLine clips={gameClips} />
        </div>
        <h1 className="rs-display rs-h1 rs-over-media-title">
          {category.name}
        </h1>
        <div className="rs-game-filter-row">
          <button
            className="rs-chip rs-game-upload-action"
            type="button"
            onClick={openGameUpload}
          >
            <IconUpload size={14} /> Upload clips
          </button>
          <Chip active={!tag} onClick={() => setTag(null)}>
            All clips
          </Chip>
          {tags.map(([tagName, count]) => (
            <Chip
              key={tagName}
              active={tag === tagName}
              onClick={() => setTag(tag === tagName ? null : tagName)}
            >
              #{tagName} - {count}
            </Chip>
          ))}
        </div>
      </section>

      <section className="rs-section">
        <div className="rs-section-heading">
          <h2 className="rs-display rs-h2">All clips</h2>
        </div>
        <ClipGrid
          clips={filtered}
          categories={categories}
          variant="filmstrip"
        />
      </section>
    </>
  );
}
