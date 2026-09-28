import { createFileRoute } from "@tanstack/react-router";
import { LibraryPage } from "@/components/Reelshelf/LibraryPage";

export const Route = createFileRoute("/")({
  component: LibraryRoute,
});

function LibraryRoute() {
  return <LibraryPage selectedSlug={null} />;
}
