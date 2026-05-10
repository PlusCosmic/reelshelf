import { useQuery } from "@tanstack/react-query";
import { fetchPlaylists } from "@/shared/services/playlists";
import type { Clip, GameCategoryResponse, PlaylistSummary } from "@/api-client";
import { fetchClipLibrary } from "@/shared/services/clips";

export function useLibraryData() {
  const libraryQuery = useQuery({
    queryKey: ["clips", "library"],
    queryFn: fetchClipLibrary,
    staleTime: 30_000,
  });

  return {
    categories: libraryQuery.data?.categories ?? [],
    clips: libraryQuery.data?.clips ?? [],
    isLoading: libraryQuery.isLoading,
    isError: libraryQuery.isError,
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
