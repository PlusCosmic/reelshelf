import { createFileRoute } from '@tanstack/react-router'
import { ApexClips } from "../../components/Apex/ApexClips.tsx";


export const Route = createFileRoute('/apex-legends/')({
  component: RouteComponent,
})

function RouteComponent() {
  return (
    <ApexClips />
  )
}
