import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ClipLibraryResponse } from "@/api-client";
import {
  addCustomCategory,
  addGameCategoryFromIgdb,
  fetchCategories,
  removeShelfBook,
  searchGames,
  setShelfOrder,
} from "@/shared/services/categories";

const libraryKey = ["clips", "library"];

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

/**
 * Saves the shelf's order. The library is reordered at once and put back if the save fails; saves run
 * one after another, so a quick run of moves lands in the order they were made.
 */
export function useSetShelfOrder() {
  const queryClient = useQueryClient();

  return useMutation({
    scope: { id: "shelf-order" },
    mutationFn: (categoryIds: string[]) => setShelfOrder(categoryIds),
    onMutate: async (categoryIds) => {
      await queryClient.cancelQueries({ queryKey: libraryKey });
      const previous =
        queryClient.getQueryData<ClipLibraryResponse>(libraryKey);
      if (previous) {
        const rank = new Map(categoryIds.map((id, index) => [id, index]));
        const categories = [...previous.categories].sort(
          (a, b) =>
            (rank.get(a.id) ?? Number.MAX_SAFE_INTEGER) -
            (rank.get(b.id) ?? Number.MAX_SAFE_INTEGER),
        );
        queryClient.setQueryData<ClipLibraryResponse>(libraryKey, {
          ...previous,
          categories,
        });
      }
      return { previous };
    },
    onError: (_error, _ids, context) => {
      if (context?.previous)
        queryClient.setQueryData(libraryKey, context.previous);
      void queryClient.invalidateQueries({ queryKey: libraryKey });
    },
  });
}

/** Takes an empty book off the shelf, at once, putting it back if the API refuses. */
export function useRemoveShelfBook() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (categoryId: string) => removeShelfBook(categoryId),
    onMutate: async (categoryId) => {
      await queryClient.cancelQueries({ queryKey: libraryKey });
      const previous =
        queryClient.getQueryData<ClipLibraryResponse>(libraryKey);
      if (previous)
        queryClient.setQueryData<ClipLibraryResponse>(libraryKey, {
          ...previous,
          categories: previous.categories.filter(
            (category) => category.id !== categoryId,
          ),
        });
      return { previous };
    },
    onError: (_error, _id, context) => {
      if (context?.previous)
        queryClient.setQueryData(libraryKey, context.previous);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: libraryKey });
      void queryClient.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}
