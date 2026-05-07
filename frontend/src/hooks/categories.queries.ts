import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { GameCategoryResponse } from "@/api-client";
import {
  addCustomCategory,
  addGameCategoryFromIgdb,
  fetchCategories,
  fetchCategoryById,
  removeCategory,
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

export function useCategoryById(categoryId: string | undefined | null) {
  return useQuery({
    queryKey: ["categories", categoryId],
    queryFn: () => {
      if (!categoryId) throw new Error("Category ID is required");
      return fetchCategoryById(categoryId);
    },
    enabled: !!categoryId,
    staleTime: 10 * 60_000,
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
    },
  });
}

export function useRemoveCategory() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (categoryId: string) => removeCategory(categoryId),
    onMutate: async (categoryId) => {
      await queryClient.cancelQueries({ queryKey: ["categories"] });

      const previousCategories = queryClient.getQueryData<
        GameCategoryResponse[]
      >(["categories"]);

      if (previousCategories) {
        queryClient.setQueryData<GameCategoryResponse[]>(
          ["categories"],
          previousCategories.filter((category) => category.id !== categoryId),
        );
      }

      return { previousCategories };
    },
    onError: (_err, _categoryId, context) => {
      if (context?.previousCategories) {
        queryClient.setQueryData(["categories"], context.previousCategories);
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}
