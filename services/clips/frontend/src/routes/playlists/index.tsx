import { createFileRoute } from '@tanstack/react-router'
import { PlaylistsPage } from '@/components/Playlists/PlaylistsPage'

export const Route = createFileRoute('/playlists/')({
  component: RouteComponent,
})

function RouteComponent() {
  return <PlaylistsPage />
}
