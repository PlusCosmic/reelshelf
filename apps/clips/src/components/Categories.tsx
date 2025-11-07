import {
  Card,
  Center,
  Group,
  Image,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { apiConfig } from "@repo/shared"
import { useNavigate } from "@tanstack/react-router";
import { useCategories, useCurrentUser } from '../hooks/queries'
import { LoadingIndicator } from "./LoadingIndicator.tsx";
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

  return (
    <div>
      {isLoading && <LoadingIndicator message="Loading categories..." />}
      {!isLoading && categories && categories.length > 0 && (
        <div>
          <Group mt="md">
            {categories.map((category) => (
              <div key={category.name}>
                <Card className={classes.item} w="200" h="220">
                  <UnstyledButton onClick={() => handleClick(category.categoryEnum)}>
                    <Center>
                      <Image
                        src={apiConfig.baseUrl + category.artUrl}
                        w={160}
                        h={160}
                        radius="md"
                        fallbackSrc="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' stroke='currentColor' fill='none' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M10 14a3.5 3.5 0 0 0 5 0l4 -4a3.5 3.5 0 0 0 -5 -5l-.5 .5'%3E%3C/path%3E%3Cpath d='M14 10a3.5 3.5 0 0 0 -5 0l-4 4a3.5 3.5 0 0 0 5 5l.5 -.5'%3E%3C/path%3E%3C/svg%3E"
                      />
                    </Center>
                    <Center>
                      <Title h={24} order={5} maw={180} mt={7}>
                        {category.name}
                      </Title>
                    </Center>
                  </UnstyledButton>
                </Card>
              </div>
            ))}
          </Group>
        </div>
      )}
    </div>
  )
}
