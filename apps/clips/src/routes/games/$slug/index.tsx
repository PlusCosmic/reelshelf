import { createFileRoute } from '@tanstack/react-router';
import { ClipsPage } from '@/components/Clips/ClipsPage';
import { useCategories } from '@/hooks/queries';
import { Center, Loader, Text } from '@mantine/core';

export const Route = createFileRoute('/games/$slug/')({
  component: GameCategoryRoute,
});

function GameCategoryRoute() {
  const { slug } = Route.useParams();
  const { data: categories, isLoading } = useCategories();

  if (isLoading) {
    return (
      <Center h="100%">
        <Loader color="cyan" size="lg" />
      </Center>
    );
  }

  const category = categories?.find((c) => c.slug === slug);

  if (!category) {
    return (
      <Center h="100%">
        <Text c="dimmed">Category not found</Text>
      </Center>
    );
  }

  return (
    <ClipsPage
      categoryId={category.id}
      categorySlug={category.slug}
      categoryName={category.name}
    />
  );
}
