import { useMemo } from "react";
import { useQueries, useQuery } from "@tanstack/react-query";
import { fetchClips } from "@/shared/.";
import { fetchPlaylists } from "@/shared/services/playlists";
import type { Clip, GameCategoryResponse, PlaylistSummary } from "@/api-client";
import { useCategories } from "@/hooks/queries";

export function useLibraryData() {
  const categoriesQuery = useCategories();
  const categories = categoriesQuery.data ?? [];
  const clipQueries = useQueries({
    queries: categories.map((category) => ({
      queryKey: ["clips", category.id, "library-preview"],
      queryFn: () =>
        fetchClips({
          categoryId: category.id,
          page: 1,
          pageSize: 96,
          sortOrder: 0,
        }),
      enabled: categories.length > 0,
      staleTime: 30_000,
    })),
  });

  const clips = useMemo(
    () => clipQueries.flatMap((query) => query.data?.clips ?? []),
    [clipQueries],
  );
  const isLoading =
    categoriesQuery.isLoading || clipQueries.some((query) => query.isLoading);
  const isError =
    categoriesQuery.isError || clipQueries.some((query) => query.isError);

  return {
    categories,
    clips,
    isLoading,
    isError,
  };
}

export function usePlaylistsData() {
  const playlistsQuery = useQuery({
    queryKey: ["playlists"],
    queryFn: fetchPlaylists,
    staleTime: 30_000,
  });

  return {
    playlists: (playlistsQuery.data ?? []) as PlaylistSummary[],
    isLoading: playlistsQuery.isLoading,
    isError: playlistsQuery.isError,
  };
}

export type LibraryData = {
  categories: GameCategoryResponse[];
  clips: Clip[];
  isLoading: boolean;
  isError: boolean;
};
