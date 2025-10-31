import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useState } from "react";
import { apiConfig, fetchApexClips, fetchMe } from "@repo/shared";
import {
  Card,
  Center,
  Group,
  Image,
  Pagination,
  Text,
  Title,
  UnstyledButton,
} from "@mantine/core";
import classes from "../components/Categories.module.scss";
import type { Clip, DiscordUser } from "@pluscosmic/nucleus-api-client";

export const Route = createFileRoute('/apex-legends')({
  component: RouteComponent,
})

function RouteComponent() {
  const [user, setUser] = useState<DiscordUser | null>(null)
  const [loadingUser, setLoadingUser] = useState(true)
  const [clips, setClips] = useState<Array<Clip>>([])
  const [loadingClips, setLoadingClips] = useState(true)

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
        setClips([])
        return
      }
      setLoadingClips(true)
      try {
        const xs = await fetchApexClips(1)
        if (!xs) return
        setClips(xs)
      } catch (e) {
        console.error(e)
      } finally {
        setLoadingClips(false)
      }
    })()
  }, [user])
  const items = clips.map((clip) => (
    <Card className={classes.item}>
      <UnstyledButton
        key={clip.video.title}
      >
        <Center>
          <Image
            src={`${apiConfig.bunnyBaseUrl}/${clip.video.guid}/preview.webp`}
            w={320}
            h={180}
            radius="md"
          />
        </Center>
        <Center>
          <Title h={24} order={5} maw={180} mt={7}>
            {clip.video.title}
          </Title>
        </Center>
      </UnstyledButton>
    </Card>
  ))

  const isLoading = loadingUser || loadingClips;
  return (
    <div>
      {isLoading && <Text>Loading categories..</Text>}
      {!isLoading && clips.length > 0 && (
        <div>
          <Group mt="md">
            {items.map((item) => (
              <div>{item}</div>
            ))}
          </Group>
        </div>
      )}
      <Pagination total={10} />
    </div>
  )
}
