import { createFileRoute } from "@tanstack/react-router";
import { CollectionPage } from "@/components/Reelshelf/CollectionPage";

export const Route = createFileRoute("/collections/$playlistId")({
  component: PlaylistRoute,
});

function PlaylistRoute() {
  const { playlistId } = Route.useParams();
  return <CollectionPage playlistId={playlistId} />;
}
