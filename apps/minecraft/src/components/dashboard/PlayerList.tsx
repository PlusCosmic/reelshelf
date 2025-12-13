import { Card, Text, Stack, Avatar, Group, Loader } from '@mantine/core';
import { IconUsers } from '@tabler/icons-react';
import { useOnlinePlayers } from '../../hooks/useServerStatus';

export function PlayerList() {
  const { data: players, isLoading, error } = useOnlinePlayers();

  if (isLoading) {
    return (
      <Card shadow="sm" padding="lg" radius="md" withBorder>
        <Stack align="center" justify="center" h={200}>
          <Loader size="lg" />
          <Text c="dimmed">Loading players...</Text>
        </Stack>
      </Card>
    );
  }

  if (error) {
    return (
      <Card shadow="sm" padding="lg" radius="md" withBorder>
        <Stack gap="md">
          <Group gap="sm">
            <IconUsers size={24} />
            <Text size="lg" fw={600}>Online Players</Text>
          </Group>
          <Text c="red" size="sm">Failed to load player list</Text>
        </Stack>
      </Card>
    );
  }

  const playerList = players ?? [];

  return (
    <Card shadow="sm" padding="lg" radius="md" withBorder>
      <Stack gap="md">
        <Group gap="sm" justify="space-between">
          <Group gap="sm">
            <IconUsers size={24} />
            <Text size="lg" fw={600}>Online Players</Text>
          </Group>
          <Text size="sm" c="dimmed">
            {playerList.length} {playerList.length === 1 ? 'player' : 'players'}
          </Text>
        </Group>

        {playerList.length === 0 ? (
          <Text c="dimmed" ta="center" py="md">No players online</Text>
        ) : (
          <Stack gap="xs">
            {playerList.map((player, index) => (
              <Group key={player.uuid || index} gap="sm" p="xs" style={{ borderRadius: '8px' }}>
                <Avatar
                  src={player.uuid ? `https://crafatar.com/avatars/${player.uuid}?size=40&overlay` : null}
                  alt={player.name}
                  radius="sm"
                  size="md"
                >
                  {player.name.charAt(0).toUpperCase()}
                </Avatar>
                <Text size="sm" fw={500}>{player.name}</Text>
              </Group>
            ))}
          </Stack>
        )}
      </Stack>
    </Card>
  );
}
