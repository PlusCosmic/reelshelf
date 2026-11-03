import { createFileRoute } from "@tanstack/react-router";
import { ClipPlayerPage } from "@/components/Reelshelf/ClipPlayerPage";
import { readClipListSearch } from "@/components/Reelshelf/libraryList";
import type { ClipLinkSearch } from "@/components/Reelshelf/primitives/ClipViews";

export const Route = createFileRoute("/games/$slug/$clipId")({
  component: ClipDetailRoute,
  // ?collection= plays the clip as part of that collection, in its order. Otherwise the library list it was opened
  // from (its filters, and from=library for every game) comes along, so back returns to it and next steps through it.
  validateSearch: (search: Record<string, unknown>): ClipLinkSearch =>
    typeof search.collection === "string"
      ? { collection: search.collection }
      : readClipListSearch(search),
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();
  const { collection, ...list } = Route.useSearch();
  return (
    <ClipPlayerPage
      slug={slug}
      clipId={clipId}
      collectionId={collection}
      list={list}
    />
  );
}
