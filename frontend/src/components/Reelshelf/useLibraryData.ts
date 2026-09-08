import { useQuery } from "@tanstack/react-query";
import { fetchPlaylists } from "@/shared/services/playlists";
import type { PlaylistSummary } from "@/api-client";
import { fetchClipLibrary } from "@/shared/services/clips";

const emptyTotals = {
  clipCount: 0,
  durationSeconds: 0,
  storageBytes: 0,
};

export function useLibraryData() {
  const libraryQuery = useQuery({
    queryKey: ["clips", "library"],
    queryFn: fetchClipLibrary,
    staleTime: 30_000,
  });

  return {
    categories: libraryQuery.data?.categories ?? [],
    clips: libraryQuery.data?.clips ?? [],
    categoryTotals: libraryQuery.data?.categoryTotals ?? [],
    totals: libraryQuery.data?.totals ?? emptyTotals,
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
