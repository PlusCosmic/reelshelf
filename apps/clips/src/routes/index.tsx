import { createFileRoute } from '@tanstack/react-router'
import { Box, Center, Group, Stack, Text } from '@mantine/core'
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
    <Box
      style={{
        height: 'calc(100vh - 138px)',
      }}
    >
      {!user && (
        <Stack justify="center" h="100%" align="center" gap="xl">
          <Stack align="center" gap="md">
            <Group gap="sm">
              <img
                src={logoDraft}
                width={60}
                height={60}
                referrerPolicy="no-referrer"
                alt="Clips Logo"
                style={{
                  filter: 'drop-shadow(0 4px 12px rgba(0, 0, 0, 0.3))',
                }}
              />
              <Text
                size="3rem"
                fw={700}
                style={{
                  letterSpacing: '-1.5px',
                  background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.9) 0%, rgba(255, 255, 255, 0.6) 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  backgroundClip: 'text',
                }}
              >
                Clips
              </Text>
            </Group>
            <Text c="dimmed" size="lg" style={{ letterSpacing: '0.5px' }}>
              Your personal video clip collection
            </Text>
          </Stack>
          <Center>
            <LoginButton />
          </Center>
        </Stack>
      )}
      {!isLoading && user && (
        <Stack justify="center" h="100%" align="center" gap="xl">
          <Stack align="center" gap="md" style={{ marginTop: '2rem' }}>
            <Group gap="sm">
              <img
                src={logoDraft}
                width={60}
                height={60}
                referrerPolicy="no-referrer"
                alt="Clips Logo"
                style={{
                  filter: 'drop-shadow(0 4px 12px rgba(0, 0, 0, 0.3))',
                }}
              />
              <Text
                size="3rem"
                fw={700}
                style={{
                  letterSpacing: '-1.5px',
                  background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.9) 0%, rgba(255, 255, 255, 0.6) 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  backgroundClip: 'text',
                }}
              >
                Clips
              </Text>
            </Group>
            <Text c="dimmed" size="lg" style={{ letterSpacing: '0.5px' }}>
              Choose a category to get started
            </Text>
          </Stack>
          <Center>
            <Categories />
          </Center>
        </Stack>
      )}
    </Box>
  )
}
