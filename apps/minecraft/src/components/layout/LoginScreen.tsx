import { Center, Stack, Title, Text, Paper, Box, ThemeIcon } from '@mantine/core';
import { LoginButton } from '@repo/ui';
import { IconServer2 } from '@tabler/icons-react';

export function LoginScreen() {
  return (
    <Center
      h="100vh"
      className="cyber-background"
      style={{ position: 'relative', overflow: 'hidden' }}
    >
      {/* Animated background elements */}
      <Box
        style={{
          position: 'absolute',
          top: '20%',
          left: '10%',
          width: 300,
          height: 300,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(0, 212, 255, 0.15) 0%, transparent 70%)',
          filter: 'blur(60px)',
          animation: 'pulse 4s ease-in-out infinite',
        }}
      />
      <Box
        style={{
          position: 'absolute',
          bottom: '30%',
          right: '15%',
          width: 250,
          height: 250,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(168, 85, 247, 0.15) 0%, transparent 70%)',
          filter: 'blur(60px)',
          animation: 'pulse 4s ease-in-out infinite 1s',
        }}
      />

      <Paper
        p="xl"
        radius="lg"
        style={{
          background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.85) 100%)',
          backdropFilter: 'blur(20px)',
          border: '1px solid rgba(0, 212, 255, 0.2)',
          position: 'relative',
          overflow: 'hidden',
          minWidth: 360,
        }}
      >
        {/* Top gradient line */}
        <Box
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            height: 3,
            background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 50%, #ec4899 100%)',
          }}
        />

        <Stack align="center" gap="xl" py="md">
          {/* Logo */}
          <Box style={{ position: 'relative' }}>
            <ThemeIcon
              size={80}
              radius="xl"
              variant="gradient"
              gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
              style={{
                boxShadow: '0 0 40px rgba(0, 212, 255, 0.4), 0 0 80px rgba(168, 85, 247, 0.2)',
              }}
            >
              <IconServer2 size={44} stroke={1.5} />
            </ThemeIcon>
            {/* Pulse ring */}
            <Box
              style={{
                position: 'absolute',
                inset: -8,
                border: '2px solid rgba(0, 212, 255, 0.3)',
                borderRadius: '50%',
                animation: 'pulse-ring 2s ease-out infinite',
              }}
            />
          </Box>

          {/* Title */}
          <Stack align="center" gap={4}>
            <Title
              order={2}
              style={{
                background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
                WebkitBackgroundClip: 'text',
                WebkitTextFillColor: 'transparent',
                fontWeight: 800,
                letterSpacing: '0.05em',
              }}
            >
              MINECRAFT SERVER
            </Title>
            <Text c="dimmed" size="sm" fw={500}>
              Control Panel Access
            </Text>
          </Stack>

          {/* Divider */}
          <Box
            style={{
              width: '60%',
              height: 1,
              background: 'linear-gradient(90deg, transparent 0%, rgba(0, 212, 255, 0.3) 50%, transparent 100%)',
            }}
          />

          {/* Login Section */}
          <Stack align="center" gap="md" w="100%">
            <Text size="sm" c="dimmed" ta="center">
              Sign in to manage your server
            </Text>
            <Box
              style={{
                padding: '2px',
                borderRadius: 8,
                background: 'linear-gradient(135deg, #00d4ff 0%, #a855f7 100%)',
              }}
            >
              <Box
                style={{
                  background: 'rgba(10, 10, 20, 0.9)',
                  borderRadius: 6,
                  padding: '2px',
                }}
              >
                <LoginButton />
              </Box>
            </Box>
          </Stack>
        </Stack>
      </Paper>

      {/* CSS for animations */}
      <style>
        {`
          @keyframes pulse {
            0%, 100% { opacity: 0.5; transform: scale(1); }
            50% { opacity: 0.8; transform: scale(1.1); }
          }
          @keyframes pulse-ring {
            0% { transform: scale(0.9); opacity: 1; }
            100% { transform: scale(1.4); opacity: 0; }
          }
        `}
      </style>
    </Center>
  );
}
