import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/snowboarding')({
  component: RouteComponent,
})

function RouteComponent() {
  return <div>Hello "/snowboarding"!</div>
}
