import { Box, Group, Text, Stack, Badge, ThemeIcon, Loader, Tooltip } from '@mantine/core';
import {
  IconServer2,
  IconCircleCheck,
  IconCircleX,
  IconWorldWww,
  IconVersions,
} from '@tabler/icons-react';
import { useServerStatus } from '../../hooks/useServerStatus';

export function ServerStatusCard() {
  const { data: status, isLoading, error } = useServerStatus();

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
          <Text c="dimmed" size="sm">Loading server status...</Text>
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
        <Stack align="center" justify="center" h={240}>
          <ThemeIcon
            size={60}
            radius="xl"
            variant="light"
            color="red"
            style={{ boxShadow: '0 0 20px rgba(255, 68, 68, 0.3)' }}
          >
            <IconCircleX size={32} />
          </ThemeIcon>
          <Text c="red" size="sm" fw={500}>Failed to load server status</Text>
        </Stack>
      </Box>
    );
  }

  const isOnline = status?.isOnline;

  return (
    <Box
      p="lg"
      style={{
        background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
        borderRadius: 14,
        border: `1px solid ${isOnline ? 'rgba(0, 255, 136, 0.2)' : 'rgba(255, 68, 68, 0.2)'}`,
        position: 'relative',
        overflow: 'hidden',
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
          background: isOnline
            ? 'linear-gradient(90deg, #00ff88 0%, #00d4ff 100%)'
            : 'linear-gradient(90deg, #ff4444 0%, #ff6b6b 100%)',
        }}
      />

      <Stack gap="lg">
        {/* Header */}
        <Group justify="space-between" align="flex-start">
          <Group gap="md">
            <ThemeIcon
              size={48}
              radius="md"
              variant="gradient"
              gradient={
                isOnline
                  ? { from: '#00ff88', to: '#00d4ff', deg: 135 }
                  : { from: '#ff4444', to: '#ff6b6b', deg: 135 }
              }
              style={{
                boxShadow: isOnline
                  ? '0 0 25px rgba(0, 255, 136, 0.4)'
                  : '0 0 25px rgba(255, 68, 68, 0.4)',
              }}
            >
              <IconServer2 size={26} />
            </ThemeIcon>
            <Stack gap={2}>
              <Text size="lg" fw={700}>Server Status</Text>
              <Text size="xs" c="dimmed">Real-time monitoring</Text>
            </Stack>
          </Group>

          <Badge
            size="lg"
            variant="light"
            color={isOnline ? 'green' : 'red'}
            leftSection={
              isOnline ? <IconCircleCheck size={14} /> : <IconCircleX size={14} />
            }
            style={{
              background: isOnline
                ? 'rgba(0, 255, 136, 0.1)'
                : 'rgba(255, 68, 68, 0.1)',
              border: `1px solid ${isOnline ? 'rgba(0, 255, 136, 0.3)' : 'rgba(255, 68, 68, 0.3)'}`,
            }}
          >
            {isOnline ? 'Online' : 'Offline'}
          </Badge>
        </Group>

        {/* Status Details */}
        <Stack gap="md">
          {/* MOTD */}
          {status?.motd && (
            <Box
              p="sm"
              style={{
                background: 'rgba(0, 212, 255, 0.05)',
                borderRadius: 8,
                borderLeft: '3px solid #00d4ff',
              }}
            >
              <Text size="sm" c="dimmed" style={{ fontStyle: 'italic' }}>
                "{status.motd}"
              </Text>
            </Box>
          )}

          {/* Info Grid */}
          <Group gap="xl">
            <Tooltip label="Server version">
              <Group gap="xs">
                <IconVersions
                  size={18}
                  style={{ color: '#a855f7', filter: 'drop-shadow(0 0 4px rgba(168, 85, 247, 0.5))' }}
                />
                <Stack gap={0}>
                  <Text size="xs" c="dimmed">Version</Text>
                  <Text size="sm" fw={600}>{status?.version || 'Unknown'}</Text>
                </Stack>
              </Group>
            </Tooltip>

            <Tooltip label="Server address">
              <Group gap="xs">
                <IconWorldWww
                  size={18}
                  style={{ color: '#00d4ff', filter: 'drop-shadow(0 0 4px rgba(0, 212, 255, 0.5))' }}
                />
                <Stack gap={0}>
                  <Text size="xs" c="dimmed">Address</Text>
                  <Text size="sm" fw={600} ff="monospace">mc.server.net</Text>
                </Stack>
              </Group>
            </Tooltip>
          </Group>
        </Stack>

        {/* Capacity Bar */}
        <Box>
          <Group justify="space-between" mb="xs">
            <Text size="xs" c="dimmed" tt="uppercase" fw={600}>
              Server Capacity
            </Text>
            <Text size="xs" c="dimmed">
              {status?.onlinePlayers ?? 0} / {status?.maxPlayers ?? 0} players
            </Text>
          </Group>
          <Box
            style={{
              height: 6,
              borderRadius: 3,
              background: 'rgba(255, 255, 255, 0.1)',
              overflow: 'hidden',
            }}
          >
            <Box
              style={{
                height: '100%',
                width: `${status?.maxPlayers ? (status.onlinePlayers / status.maxPlayers) * 100 : 0}%`,
                background: 'linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)',
                borderRadius: 3,
                boxShadow: '0 0 10px rgba(0, 212, 255, 0.5)',
                transition: 'width 0.5s ease',
              }}
            />
          </Box>
        </Box>
      </Stack>
    </Box>
  );
}
