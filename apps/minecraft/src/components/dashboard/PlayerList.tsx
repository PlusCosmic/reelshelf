import { Box, Text, Stack, Avatar, Group, Loader, ScrollArea, Badge } from '@mantine/core';
import { IconUsers, IconUserCircle } from '@tabler/icons-react';
import { useOnlinePlayers } from '../../hooks/useServerStatus';

export function PlayerList() {
  const { data: players, isLoading, error } = useOnlinePlayers();

  if (isLoading) {
    return (
      <Box
        p="lg"
        style={{
          background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
          borderRadius: 14,
          border: '1px solid rgba(0, 212, 255, 0.15)',
          minHeight: 280,
        }}
      >
        <Stack align="center" justify="center" h={240}>
          <Loader size="lg" color="cyberBlue" />
          <Text c="dimmed" size="sm">Loading players...</Text>
        </Stack>
      </Box>
    );
  }

  if (error) {
    return (
      <Box
        p="lg"
        style={{
          background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
          borderRadius: 14,
          border: '1px solid rgba(255, 68, 68, 0.3)',
          minHeight: 280,
        }}
      >
        <Group gap="sm" mb="md">
          <IconUsers size={24} style={{ color: '#00d4ff' }} />
          <Text size="lg" fw={700}>Online Players</Text>
        </Group>
        <Text c="red" size="sm">Failed to load player list</Text>
      </Box>
    );
  }

  const playerList = players ?? [];

  return (
    <Box
      p="lg"
      style={{
        background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
        borderRadius: 14,
        border: '1px solid rgba(0, 212, 255, 0.15)',
        position: 'relative',
        overflow: 'hidden',
        minHeight: 280,
      }}
    >
      {/* Top accent */}
      <Box
        style={{
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          height: 2,
          background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
        }}
      />

      <Stack gap="md" h="100%">
        {/* Header */}
        <Group justify="space-between">
          <Group gap="sm">
            <IconUsers
              size={22}
              style={{
                color: '#00d4ff',
                filter: 'drop-shadow(0 0 4px rgba(0, 212, 255, 0.5))',
              }}
            />
            <Text size="lg" fw={700}>Online Players</Text>
          </Group>
          <Badge
            size="lg"
            variant="light"
            color="cyberBlue"
            style={{
              background: 'rgba(0, 212, 255, 0.1)',
              border: '1px solid rgba(0, 212, 255, 0.2)',
            }}
          >
            {playerList.length} {playerList.length === 1 ? 'player' : 'players'}
          </Badge>
        </Group>

        {/* Player List */}
        {playerList.length === 0 ? (
          <Stack align="center" justify="center" flex={1} py="xl">
            <Box
              style={{
                width: 60,
                height: 60,
                borderRadius: '50%',
                background: 'rgba(0, 212, 255, 0.1)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                border: '1px solid rgba(0, 212, 255, 0.2)',
              }}
            >
              <IconUserCircle size={30} style={{ color: '#00d4ff', opacity: 0.5 }} />
            </Box>
            <Text c="dimmed" size="sm">No players online</Text>
          </Stack>
        ) : (
          <ScrollArea h={180} offsetScrollbars scrollbarSize={6}>
            <Stack gap="xs">
              {playerList.map((player, index) => (
                <Group
                  key={player.uuid || index}
                  gap="sm"
                  p="sm"
                  style={{
                    borderRadius: 10,
                    background: 'rgba(0, 212, 255, 0.03)',
                    border: '1px solid rgba(0, 212, 255, 0.1)',
                    transition: 'all 0.2s ease',
                    cursor: 'default',
                  }}
                  className="cyber-action-card"
                >
                  <Avatar
                    src={
                      player.uuid
                        ? `https://crafatar.com/avatars/${player.uuid}?size=40&overlay`
                        : null
                    }
                    alt={player.name}
                    radius="md"
                    size={38}
                    style={{
                      border: '2px solid rgba(0, 212, 255, 0.3)',
                      boxShadow: '0 0 10px rgba(0, 212, 255, 0.2)',
                    }}
                  >
                    {player.name.charAt(0).toUpperCase()}
                  </Avatar>
                  <Stack gap={0} flex={1}>
                    <Text size="sm" fw={600}>{player.name}</Text>
                    <Text size="xs" c="dimmed" ff="monospace">
                      {player.uuid?.substring(0, 8) || 'Connecting...'}
                    </Text>
                  </Stack>
                  <Box
                    style={{
                      width: 8,
                      height: 8,
                      borderRadius: '50%',
                      background: '#00ff88',
                      boxShadow: '0 0 8px rgba(0, 255, 136, 0.6)',
                    }}
                  />
                </Group>
              ))}
            </Stack>
          </ScrollArea>
        )}
      </Stack>
    </Box>
  );
}
