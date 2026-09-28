import { createFileRoute } from "@tanstack/react-router";
import { LibraryPage } from "@/components/Reelshelf/LibraryPage";

export const Route = createFileRoute("/games/$slug/")({
  component: GameRoute,
});

/** A game's page is the library with that game's book pulled out. */
function GameRoute() {
  const { slug } = Route.useParams();
  return <LibraryPage selectedSlug={slug} />;
}
