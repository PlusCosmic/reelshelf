import { createFileRoute } from '@tanstack/react-router'
import { ApexClips } from "../../components/Apex/ApexClips.tsx";

type ApexLegendsSearch = {
  page?: number
}

export const Route = createFileRoute('/apex-legends/')({
  component: RouteComponent,
  validateSearch: (search: Record<string, unknown>): ApexLegendsSearch => {
    return {
      page: Number(search?.page) || undefined,
    }
  },
})

function RouteComponent() {
  const search = Route.useSearch()

  return (
    <ApexClips initialPage={search.page} />
  )
}
