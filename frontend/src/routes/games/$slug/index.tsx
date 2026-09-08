import { createFileRoute, useNavigate } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useMemo, useState } from "react";
import { IconUpload } from "@tabler/icons-react";
import {
  BackToLibrary,
  Chip,
  PagedClipGrid,
  StatLine,
} from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  categoryTotalsFor,
  getGameColors,
} from "@/components/Reelshelf/reelshelf-model";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";
import { useTopTags } from "@/hooks/clips.queries";
import { setPendingBulkUploadEntry } from "@/utils/bulkUploadEntry";

export const Route = createFileRoute("/games/$slug/")({
  component: GameCategoryRoute,
});

function GameCategoryRoute() {
  const { slug } = Route.useParams();
  const navigate = useNavigate();
  const { categories, categoryTotals, isLoading, isError } = useLibraryData();
  const [tag, setTag] = useState<string | null>(null);
  const category = categories.find((item) => item.slug === slug);
  const [colorA, colorB] = getGameColors(category?.id ?? slug);
  const tags = (useTopTags(category?.id, !!category).data ?? []).slice(0, 10);
  const totals = useMemo(
    () => categoryTotalsFor(categoryTotals, category?.id ?? ""),
    [categoryTotals, category?.id],
  );
  const filters = useMemo(
    () => ({ categoryId: category?.id ?? null, tag }),
    [category?.id, tag],
  );

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
          <StatLine totals={totals} />
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
          {tags.map((item) => (
            <Chip
              key={item.name}
              active={tag === item.name}
              onClick={() => setTag(tag === item.name ? null : item.name)}
            >
              #{item.name} - {item.count}
            </Chip>
          ))}
        </div>
      </section>

      <section className="rs-section">
        <div className="rs-section-heading">
          <h2 className="rs-display rs-h2">All clips</h2>
        </div>
        <PagedClipGrid
          filters={filters}
          categories={categories}
          variant="filmstrip"
        />
      </section>
    </>
  );
}
