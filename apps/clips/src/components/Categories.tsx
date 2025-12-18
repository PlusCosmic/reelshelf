import {
  Box,
  Card,
  Group,
  Image,
  Loader,
  Stack,
  Text,
  ThemeIcon,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { IconCategory } from '@tabler/icons-react'
import { apiConfig } from "@repo/shared"
import { useNavigate } from "@tanstack/react-router";
import { useCategories, useCurrentUser } from '../hooks/queries'
import classes from './Categories.module.scss'

export default function Categories() {
  const navigate = useNavigate()
  const { isLoading: isLoadingUser } = useCurrentUser()
  const { data: categories, isLoading: isLoadingCategories } = useCategories()

  const isLoading = isLoadingUser || isLoadingCategories

  function handleClick(categoryEnum: number) {
    switch (categoryEnum) {
      case 0:
        navigate({ to: `/apex-legends` });
        break;
      case 1:
        navigate({ to: `/warzone` });
        break;
      case 2:
        navigate({ to: `/snowboarding` });
        break;
    }
  }

  if (isLoading) {
    return (
      <Stack align="center" gap="md" py="xl">
        <Loader size="lg" color="cyberBlue" />
        <Text c="dimmed" size="sm">Loading categories...</Text>
      </Stack>
    )
  }

  if (!categories || categories.length === 0) {
    return (
      <Stack align="center" gap="md" py="xl">
        <ThemeIcon
          size={60}
          radius="xl"
          variant="light"
          color="gray"
          style={{
            background: 'rgba(0, 212, 255, 0.05)',
            border: '1px solid rgba(0, 212, 255, 0.1)',
          }}
        >
          <IconCategory size={30} style={{ color: 'rgba(255, 255, 255, 0.3)' }} />
        </ThemeIcon>
        <Text c="dimmed" size="sm">No categories available</Text>
      </Stack>
    )
  }

  return (
    <Group mt="md" gap="lg">
      {categories.map((category) => (
        <Card
          key={category.name}
          className={classes.item}
          w={220}
          h={240}
          p="lg"
        >
          <UnstyledButton
            onClick={() => handleClick(category.categoryEnum)}
            style={{ width: '100%', height: '100%' }}
          >
            <Stack align="center" gap="md" h="100%" justify="center">
              <Box className={classes.imageWrapper}>
                <Image
                  src={apiConfig.baseUrl + category.artUrl}
                  w={160}
                  h={160}
                  radius="md"
                  fallbackSrc="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' stroke='%2300d4ff' fill='none' stroke-width='1.5'%3E%3Cpath d='M15 10l4.553 -2.276a1 1 0 0 1 1.447 .894v6.764a1 1 0 0 1 -1.447 .894l-4.553 -2.276v-4z'%3E%3C/path%3E%3Crect x='3' y='6' width='12' height='12' rx='2'%3E%3C/rect%3E%3C/svg%3E"
                  style={{
                    border: '1px solid rgba(0, 212, 255, 0.1)',
                    borderRadius: 'var(--mantine-radius-md)',
                  }}
                />
              </Box>
              <Title
                className={classes.title}
                order={5}
                maw={180}
                ta="center"
                lineClamp={2}
              >
                {category.name}
              </Title>
            </Stack>
          </UnstyledButton>
        </Card>
      ))}
    </Group>
  )
}
