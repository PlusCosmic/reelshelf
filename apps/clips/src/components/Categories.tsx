import { useEffect, useState } from 'react'
import {
  Card,
  Center,
  Group,
  Image,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { fetchCategories } from '../services/categories'
import { apiConfig } from '../config/apiConfig'
import { fetchMe } from '../services/user'
import classes from './Categories.module.scss'
import type { ClipCategory, DiscordUser } from '@pluscosmic/nucleus-api-client'

export default function Categories() {
  const [user, setUser] = useState<DiscordUser | null>(null)
  const [loadingUser, setLoadingUser] = useState(true)
  const [loadingCategories, setLoadingCategories] = useState(true)
  const [categories, setCategories] = useState<Array<ClipCategory>>([])

  useEffect(() => {
    setLoadingUser(true)
    ;(async () => {
      try {
        const me = await fetchMe()
        setUser(me)
      } catch (e) {
        console.error(e)
      } finally {
        setLoadingUser(false)
      }
    })()
  }, [])

  useEffect(() => {
    ;(async () => {
      if (!user) {
        setCategories([])
        return
      }
      setLoadingCategories(true)
      try {
        const xs = await fetchCategories()
        if (!xs) return
        setCategories(xs)
      } catch (e) {
        console.error(e)
      } finally {
        setLoadingCategories(false)
      }
    })()
  }, [user])

  const isLoading = loadingUser || (user && loadingCategories)

  const items = categories.map((category) => (
    <Card className={classes.item} w="200" h="220">
      <UnstyledButton
        key={category.name}
/*        onClick={() => handleClick(category.categoryEnum)}*/
      >
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
  ))

  return (
    <div>
      {isLoading && <Text>Loading categories..</Text>}
      {!isLoading && categories.length > 0 && (
        <div>
          <Group mt="md">
            {items.map((item) => (
              <div>{item}</div>
            ))}
          </Group>
        </div>
      )}
    </div>
  )
}
