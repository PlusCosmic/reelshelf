import { createFileRoute } from '@tanstack/react-router'
import { Box, Center, Group, Stack, Text, ThemeIcon } from '@mantine/core'
import { LoginButton } from '@repo/ui'
import { IconVideo } from '@tabler/icons-react'
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
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      {/* Background glow effects */}
      <Box
        style={{
          position: 'absolute',
          top: '10%',
          left: '20%',
          width: 400,
          height: 400,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(0, 212, 255, 0.1) 0%, transparent 70%)',
          filter: 'blur(80px)',
          pointerEvents: 'none',
        }}
      />
      <Box
        style={{
          position: 'absolute',
          bottom: '20%',
          right: '15%',
          width: 350,
          height: 350,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(168, 85, 247, 0.1) 0%, transparent 70%)',
          filter: 'blur(80px)',
          pointerEvents: 'none',
        }}
      />

      {!user && (
        <Stack justify="center" h="100%" align="center" gap="xl" style={{ position: 'relative', zIndex: 1 }}>
          <Stack align="center" gap="lg">
            {/* Logo with glow */}
            <Box style={{ position: 'relative' }}>
              <ThemeIcon
                size={100}
                radius="xl"
                variant="gradient"
                gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
                style={{
                  boxShadow: '0 0 40px rgba(0, 212, 255, 0.4), 0 0 80px rgba(168, 85, 247, 0.2)',
                }}
                className="cyber-glow-pulse"
              >
                <IconVideo size={50} stroke={1.5} />
              </ThemeIcon>
              {/* Pulse ring */}
              <Box
                style={{
                  position: 'absolute',
                  inset: -10,
                  border: '2px solid rgba(0, 212, 255, 0.3)',
                  borderRadius: '50%',
                  animation: 'pulse-ring 2s ease-out infinite',
                }}
              />
            </Box>

            {/* Title */}
            <Group gap="md" align="center">
              <img
                src={logoDraft}
                width={70}
                height={70}
                referrerPolicy="no-referrer"
                alt="Clips Logo"
                style={{
                  filter: 'drop-shadow(0 0 12px rgba(0, 212, 255, 0.5))',
                }}
              />
              <Text
                size="4rem"
                fw={800}
                style={{
                  letterSpacing: '-2px',
                  background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 50%, #ec4899 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  backgroundClip: 'text',
                  textShadow: '0 0 60px rgba(0, 212, 255, 0.3)',
                }}
              >
                Clips
              </Text>
            </Group>

            {/* Tagline */}
            <Text c="dimmed" size="xl" style={{ letterSpacing: '1px' }}>
              Your personal video clip collection
            </Text>

            {/* Divider */}
            <Box
              style={{
                width: 200,
                height: 1,
                background: 'linear-gradient(90deg, transparent 0%, rgba(0, 212, 255, 0.4) 50%, transparent 100%)',
                margin: '1rem 0',
              }}
            />
          </Stack>

          {/* Login Button */}
          <Center>
            <Box
              style={{
                padding: '2px',
                borderRadius: 10,
                background: 'linear-gradient(135deg, #00d4ff 0%, #a855f7 100%)',
              }}
            >
              <Box
                style={{
                  background: 'rgba(10, 10, 20, 0.9)',
                  borderRadius: 8,
                  padding: '2px',
                }}
              >
                <LoginButton />
              </Box>
            </Box>
          </Center>
        </Stack>
      )}

      {!isLoading && user && (
        <Stack justify="center" h="100%" align="center" gap="xl" style={{ position: 'relative', zIndex: 1 }}>
          <Stack align="center" gap="lg">
            {/* Logo and Title */}
            <Group gap="md" align="center">
              <img
                src={logoDraft}
                width={60}
                height={60}
                referrerPolicy="no-referrer"
                alt="Clips Logo"
                style={{
                  filter: 'drop-shadow(0 0 12px rgba(0, 212, 255, 0.5))',
                }}
              />
              <Text
                size="3.5rem"
                fw={800}
                style={{
                  letterSpacing: '-2px',
                  background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 50%, #ec4899 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  backgroundClip: 'text',
                }}
              >
                Clips
              </Text>
            </Group>

            {/* Tagline */}
            <Text c="dimmed" size="lg" style={{ letterSpacing: '0.5px' }}>
              Choose a category to get started
            </Text>

            {/* Divider */}
            <Box
              style={{
                width: 150,
                height: 1,
                background: 'linear-gradient(90deg, transparent 0%, rgba(0, 212, 255, 0.3) 50%, transparent 100%)',
              }}
            />
          </Stack>

          {/* Categories */}
          <Center>
            <Categories />
          </Center>
        </Stack>
      )}

      {/* CSS Animations */}
      <style>
        {`
          @keyframes pulse-ring {
            0% { transform: scale(0.9); opacity: 1; }
            100% { transform: scale(1.5); opacity: 0; }
          }
        `}
      </style>
    </Box>
  )
}
