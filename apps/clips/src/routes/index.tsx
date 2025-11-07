import { createFileRoute } from '@tanstack/react-router'
import { Group, Stack, Title } from '@mantine/core'
import { LoginButton } from '@repo/ui'
import logoDraft from '../assets/logo webp transparent.webp'
import Categories from '../components/Categories'
import { useCurrentUser } from '../hooks/queries'

export const Route = createFileRoute('/')({
  component: App,
})

function App() {
  const { data: user, isLoading } = useCurrentUser()

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
                src={logoDraft}
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
      {!isLoading && user && (
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
                src={logoDraft}
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
