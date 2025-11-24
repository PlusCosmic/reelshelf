import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/mockups')({
  component: RouteComponent,
})

function RouteComponent() {
  return <div>Hello "/mockups"!</div>
}
