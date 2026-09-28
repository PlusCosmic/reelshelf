import { createFileRoute, useParams } from "@tanstack/react-router";
import { LibraryPage } from "@/components/Reelshelf/LibraryPage";

// The library at / and a game's page at /games/$slug are one page with a different book pulled
// out. Sharing this layout keeps the bookcase mounted between them, so its books can slide and
// turn between the two instead of the whole shelf being built again.
export const Route = createFileRoute("/_library")({
  component: LibraryLayout,
});

function LibraryLayout() {
  const { slug } = useParams({ strict: false });
  return <LibraryPage selectedSlug={slug ?? null} />;
}
