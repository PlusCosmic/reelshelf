import { createFileRoute } from '@tanstack/react-router';
import { ClipDetail } from '@/components/Clips/ClipDetail';

export const Route = createFileRoute('/games/$slug/$clipId')({
  component: ClipDetailRoute,
});

function ClipDetailRoute() {
  const { slug, clipId } = Route.useParams();

  return <ClipDetail clipId={clipId} categorySlug={slug} />;
}
