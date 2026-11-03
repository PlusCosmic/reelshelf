import { createFileRoute } from "@tanstack/react-router";
import { readLibrarySearch } from "@/components/Reelshelf/libraryList";

// Rendered by the _library layout, with this game's book pulled out. The list's filters live in the URL.
export const Route = createFileRoute("/_library/games/$slug/")({
  validateSearch: readLibrarySearch,
});
