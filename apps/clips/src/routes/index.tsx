import { createFileRoute } from '@tanstack/react-router'
import { Group, Stack, Title } from '@mantine/core'
import { useEffect, useState } from 'react'
import { LoginButton } from '@repo/ui'
import { fetchMe } from "@repo/shared"
import tempLogo from '../assets/transparent plus.png'
import Categories from '../components/Categories'
import type { DiscordUser } from '@pluscosmic/nucleus-api-client'

export const Route = createFileRoute('/')({
  component: App,
})

function App() {
  const [user, setUser] = useState<DiscordUser | null>(null)
  const [loadingUser, setLoadingUser] = useState(true)

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

  return (
    <div style={{ height: 'calc(100vh - 122px)' }}>
      {!user && (
        <Stack justify="center" h="100%" align="stretch">
          <div
            style={{
              width: '100%',
              justifyContent: 'center',
              display: 'flex',
            }}
          >
            <Group justify="center" gap={'xs'}>
              <img
                src={tempLogo}
                width={40}
                height={40}
                referrerPolicy="no-referrer"
                alt=""
              />
              <Title>Clips</Title>
            </Group>
          </div>
          <div
            style={{
              width: '100%',
              justifyContent: 'center',
              display: 'flex',
            }}
          >
            <LoginButton />
          </div>
        </Stack>
      )}
      {!loadingUser && user && (
        <Stack justify="center" h="100%" align="stretch">
          <div
            style={{
              width: '100%',
              justifyContent: 'center',
              display: 'flex',
            }}
          >
            <Group justify="center" gap={'xs'}>
              <img
                src={tempLogo}
                width={40}
                height={40}
                referrerPolicy="no-referrer"
                alt=""
              />
              <Title>Clips</Title>
            </Group>
          </div>
          <div
            style={{
              width: '100%',
              justifyContent: 'center',
              display: 'flex',
            }}
          >
            <Categories />
          </div>
        </Stack>
      )}
    </div>
  )
}
