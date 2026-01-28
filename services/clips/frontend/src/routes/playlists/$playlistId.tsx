import { createFileRoute } from '@tanstack/react-router'
import { PlaylistPlayerPage } from '@/components/Playlists/PlaylistPlayerPage'

export const Route = createFileRoute('/playlists/$playlistId')({
  component: RouteComponent,
})

function RouteComponent() {
  const { playlistId } = Route.useParams()
  return <PlaylistPlayerPage playlistId={playlistId} />
}
