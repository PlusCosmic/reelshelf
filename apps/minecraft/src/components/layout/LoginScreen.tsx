import { Center, Stack, Title, Text, Paper } from '@mantine/core';
import { LoginButton } from '@repo/ui';
import { IconServer } from '@tabler/icons-react';

export function LoginScreen() {
  return (
    <Center h="100vh" style={{ background: 'var(--mantine-color-dark-8)' }}>
      <Paper
        p="xl"
        radius="md"
        style={{
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
        }}
      >
        <Stack align="center" gap="lg">
          <IconServer size={48} stroke={1.5} color="var(--mantine-color-blue-5)" />
          <Stack align="center" gap="xs">
            <Title order={2}>Minecraft Server</Title>
            <Text c="dimmed" size="sm">
              Sign in to manage your server
            </Text>
          </Stack>
          <LoginButton />
        </Stack>
      </Paper>
    </Center>
  );
}
