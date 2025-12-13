import { Card, Group, Text, Stack, Badge, ThemeIcon, Loader } from '@mantine/core';
import { IconServer, IconUsers, IconCircleCheck, IconCircleX } from '@tabler/icons-react';
import { useServerStatus } from '../../hooks/useServerStatus';

export function ServerStatusCard() {
  const { data: status, isLoading, error } = useServerStatus();

  if (isLoading) {
    return (
      <Card shadow="sm" padding="lg" radius="md" withBorder>
        <Stack align="center" justify="center" h={200}>
          <Loader size="lg" />
          <Text c="dimmed">Loading server status...</Text>
        </Stack>
      </Card>
    );
  }

  if (error) {
    return (
      <Card shadow="sm" padding="lg" radius="md" withBorder>
        <Stack align="center" justify="center" h={200}>
          <ThemeIcon size="xl" radius="xl" color="red" variant="light">
            <IconCircleX size={28} />
          </ThemeIcon>
          <Text c="red">Failed to load server status</Text>
        </Stack>
      </Card>
    );
  }

  return (
    <Card shadow="sm" padding="lg" radius="md" withBorder>
      <Stack gap="md">
        <Group justify="space-between">
          <Group gap="sm">
            <ThemeIcon size="lg" radius="md" variant="light">
              <IconServer size={24} />
            </ThemeIcon>
            <Text size="lg" fw={600}>Server Status</Text>
          </Group>
          <Badge
            color={status?.isOnline ? 'green' : 'red'}
            variant="light"
            leftSection={
              status?.isOnline ? (
                <IconCircleCheck size={16} />
              ) : (
                <IconCircleX size={16} />
              )
            }
          >
            {status?.isOnline ? 'Online' : 'Offline'}
          </Badge>
        </Group>

        {status?.motd && (
          <Text c="dimmed" size="sm">{status.motd}</Text>
        )}

        {status?.version && (
          <Group gap="xs">
            <Text size="sm" c="dimmed">Version:</Text>
            <Text size="sm" fw={500}>{status.version}</Text>
          </Group>
        )}

        <Group gap="xl">
          <Group gap="xs">
            <ThemeIcon size="md" radius="md" variant="light" color="blue">
              <IconUsers size={18} />
            </ThemeIcon>
            <Stack gap={0}>
              <Text size="xs" c="dimmed">Players Online</Text>
              <Text size="lg" fw={600}>
                {status?.onlinePlayers ?? 0} / {status?.maxPlayers ?? 0}
              </Text>
            </Stack>
          </Group>
        </Group>
      </Stack>
    </Card>
  );
}
