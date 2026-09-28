import { createFileRoute } from "@tanstack/react-router";
import { ClipPlayerPage } from "@/components/Reelshelf/ClipPlayerPage";

export const Route = createFileRoute("/games/$slug/$clipId")({
  component: ClipDetailRoute,
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();
  return <ClipPlayerPage slug={slug} clipId={clipId} />;
}
