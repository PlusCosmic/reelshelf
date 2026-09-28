import { createFileRoute } from "@tanstack/react-router";
import { ClipPlayerPage } from "@/components/Reelshelf/ClipPlayerPage";
import type { ClipLinkSearch } from "@/components/Reelshelf/primitives/ClipViews";

export const Route = createFileRoute("/games/$slug/$clipId")({
  component: ClipDetailRoute,
  // ?collection= plays the clip as part of that collection, in its order.
  validateSearch: (search: Record<string, unknown>): ClipLinkSearch => ({
    collection:
      typeof search.collection === "string" ? search.collection : undefined,
  }),
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();
  const { collection } = Route.useSearch();
  return (
    <ClipPlayerPage slug={slug} clipId={clipId} collectionId={collection} />
  );
}
