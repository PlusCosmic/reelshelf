import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addCustomCategory,
  addGameCategoryFromIgdb,
  fetchCategories,
  searchGames,
} from "@/shared/services/categories";

export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: fetchCategories,
    staleTime: 10 * 60_000,
    refetchOnMount: "always",
  });
}

export function useGameSearch(query: string) {
  return useQuery({
    queryKey: ["games", "search", query],
    queryFn: () => searchGames(query),
    enabled: query.length >= 2,
    staleTime: 5 * 60_000,
  });
}

export function useAddGameFromIgdb() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (igdbId: number) => addGameCategoryFromIgdb(igdbId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["clips", "library"] });
    },
  });
}

export function useAddCustomCategory() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ name, coverUrl }: { name: string; coverUrl?: string }) =>
      addCustomCategory(name, coverUrl),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["clips", "library"] });
    },
  });
}
