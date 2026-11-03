import { createFileRoute } from "@tanstack/react-router";
import { readLibrarySearch } from "@/components/Reelshelf/libraryList";

// Rendered by the _library layout, with every book on the shelf. The list's filters live in the URL.
export const Route = createFileRoute("/_library/")({
  validateSearch: readLibrarySearch,
});
