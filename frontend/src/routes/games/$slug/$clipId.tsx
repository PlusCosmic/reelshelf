import { createFileRoute } from "@tanstack/react-router";
import type { CSSProperties } from "react";
import { useMemo } from "react";
import {
  BackToLibrary,
  Chip,
  ClipGrid,
  PlayerActions,
  SharedIndicator,
} from "@/components/Reelshelf/ReelshelfPrimitives";
import {
  categoryForClip,
  formatDate,
  formatSize,
  getGameColors,
  playerUrl,
} from "@/components/Reelshelf/reelshelf-model";
import { useClip, useMarkAsViewed } from "@/hooks/queries";
import { useClipsInfinite } from "@/hooks/clips.queries";
import { useLibraryData } from "@/components/Reelshelf/useLibraryData";

export const Route = createFileRoute("/games/$slug/$clipId")({
  component: ClipDetailRoute,
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();
  const { data: clip, isLoading, isError } = useClip(clipId);
  const { categories } = useLibraryData();
  const markViewed = useMarkAsViewed();
  const category = clip
    ? categoryForClip(clip, categories)
    : categories.find((item) => item.slug === slug);
  const [colorA, colorB] = getGameColors(category?.id ?? slug);
  // The sidebar shows a handful from the same category; the first page is far more than enough,
  // and it shares a cache entry with the category grid's unfiltered first page.
  const { data: categoryClips } = useClipsInfinite(
    { categoryId: category?.id },
    !!category,
  );
  const related = useMemo(
    () =>
      (categoryClips?.pages[0]?.clips ?? [])
        .filter((item) => item.clipId !== clipId)
        .slice(0, 4),
    [categoryClips, clipId],
  );

  if (isLoading)
    return <div className="rs-section rs-empty">Loading player…</div>;
  if (isError || !clip)
    return (
      <div className="rs-section rs-empty">This clip could not be found.</div>
    );

  return (
    <div
      className="rs-player-page"
      style={{ "--game-a": colorA, "--game-b": colorB } as CSSProperties}
    >
      <BackToLibrary to={`/games/${slug}`} />

      <div className="rs-player">
        <iframe
          src={playerUrl(clip)}
          loading="lazy"
          title={clip.video.title}
          className="rs-player-frame"
          allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture; fullscreen;"
          allowFullScreen
          onLoad={() => {
            if (!clip.isViewed && !markViewed.isPending) {
              markViewed.mutate(clipId);
            }
          }}
        />
      </div>

      <div className="rs-player-grid">
        <section>
          <h1 className="rs-display rs-h2">{clip.video.title}</h1>
          <div className="rs-meta rs-player-meta">
            <strong>{category?.name ?? clip.video.category}</strong>
            <span>{formatDate(clip.createdAt)}</span>
            <span>{formatSize(clip.video.storageSize)}</span>
            <span>{clip.isViewed ? "Viewed" : "New"}</span>
            {clip.share.shared ? <SharedIndicator /> : null}
          </div>
          <div className="rs-chip-row">
            {clip.tags.map((tag) => (
              <Chip key={tag}>#{tag}</Chip>
            ))}
          </div>
          <div className="rs-player-actions">
            <PlayerActions clip={clip} />
          </div>
        </section>

        <aside className="rs-sidebar-panel">
          <h2 className="rs-eyebrow">
            More from {category?.name ?? "this game"}
          </h2>
          <ClipGrid clips={related} categories={categories} variant="grid" />
        </aside>
      </div>
    </div>
  );
}
