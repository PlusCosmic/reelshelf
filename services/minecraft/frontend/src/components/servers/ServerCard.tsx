import { Card, Group, Text, Stack, Badge, Box, ThemeIcon, ActionIcon } from '@mantine/core';
import { IconServer2, IconUsers, IconArrowRight, IconTrash, IconPower } from '@tabler/icons-react';
import { Link } from '@tanstack/react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { deleteServer, getServerStatus } from '@repo/shared/services/minecraft';
import type { MinecraftServer } from '@repo/nucleus-api-client';
import { modals } from '@mantine/modals';
import { notifications } from '@mantine/notifications';

interface ServerCardProps {
  server: MinecraftServer;
}

export function ServerCard({ server }: ServerCardProps) {
  const queryClient = useQueryClient();
  const { data: status } = useQuery({
    queryKey: ['minecraft', 'status', server.id],
    queryFn: () => getServerStatus(server.id!),
    enabled: !!server.id,
    refetchInterval: 10000, // Refresh every 10 seconds
  });

  const handleDelete = () => {
    modals.openConfirmModal({
      title: 'Delete Server',
      children: (
        <Text size="sm">
          Are you sure you want to delete <strong>{server.name}</strong>? This action cannot be undone.
        </Text>
      ),
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        try {
          await deleteServer(server.id!);
          await queryClient.invalidateQueries({ queryKey: ['minecraft', 'servers'] });
          notifications.show({
            title: 'Server deleted',
            message: `${server.name} has been deleted`,
            color: 'red',
          });
        } catch (error) {
          notifications.show({
            title: 'Delete failed',
            message: error instanceof Error ? error.message : 'Unable to delete server',
            color: 'red',
          });
        }
      },
    });
  };

  const isOnline = status?.isOnline ?? false;

  return (
    <Card
      shadow="sm"
      padding="lg"
      radius="md"
      style={{
        background: 'linear-gradient(135deg, rgba(0, 212, 255, 0.05) 0%, rgba(168, 85, 247, 0.08) 100%)',
        border: '1px solid rgba(0, 212, 255, 0.15)',
        transition: 'all 0.2s ease',
        cursor: 'pointer',
        position: 'relative',
        overflow: 'hidden',
      }}
      className="server-card"
    >
      {/* Glow effect when online */}
      {isOnline && (
        <Box
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            height: 2,
            background: 'linear-gradient(90deg, #00d4ff 0%, #00ff88 100%)',
            boxShadow: '0 0 10px rgba(0, 255, 136, 0.5)',
          }}
        />
      )}

      <Stack gap="md">
        {/* Header */}
        <Group justify="space-between">
          <Group gap="sm">
            <ThemeIcon
              size="xl"
              radius="md"
              variant="gradient"
              gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
              style={{
                boxShadow: isOnline ? '0 0 20px rgba(0, 212, 255, 0.4)' : undefined,
              }}
            >
              <IconServer2 size={24} />
            </ThemeIcon>
            <Stack gap={0}>
              <Text fw={600} size="lg">
                {server.name || 'Unnamed Server'}
              </Text>
              <Group gap="xs">
                <Box
                  style={{
                    width: 6,
                    height: 6,
                    borderRadius: '50%',
                    backgroundColor: isOnline ? '#00ff88' : '#666',
                    boxShadow: isOnline ? '0 0 8px rgba(0, 255, 136, 0.8)' : undefined,
                  }}
                />
                <Text size="xs" c="dimmed">
                  {isOnline ? 'Online' : 'Offline'}
                </Text>
              </Group>
            </Stack>
          </Group>

          <ActionIcon
            variant="subtle"
            color="red"
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();
              handleDelete();
            }}
          >
            <IconTrash size={18} />
          </ActionIcon>
        </Group>

        {/* Server Info */}
        <Stack gap="xs">
          {status?.version && (
            <Group justify="space-between">
              <Text size="sm" c="dimmed">Version</Text>
              <Badge size="sm" variant="light" color="cyberPurple">
                {status.version}
              </Badge>
            </Group>
          )}

          <Group justify="space-between">
            <Text size="sm" c="dimmed">Players</Text>
            <Group gap={6}>
              <IconUsers size={14} />
              <Text size="sm" fw={500}>
                {status?.onlinePlayers ?? 0} / {status?.maxPlayers ?? server.maxPlayers ?? 0}
              </Text>
            </Group>
          </Group>

          {server.motd && (
            <Text size="xs" c="dimmed" lineClamp={2} style={{ fontStyle: 'italic' }}>
              "{server.motd}"
            </Text>
          )}
        </Stack>

        {/* Manage Button */}
        <Link
          to="/servers/$serverId"
          params={{ serverId: server.id! }}
          style={{ textDecoration: 'none' }}
        >
          <Box
            p="sm"
            style={{
              background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.1) 0%, rgba(168, 85, 247, 0.1) 100%)',
              borderRadius: 8,
              border: '1px solid rgba(0, 212, 255, 0.2)',
              transition: 'all 0.2s ease',
              cursor: 'pointer',
            }}
          >
            <Group justify="space-between">
              <Group gap="xs">
                <IconPower size={16} />
                <Text size="sm" fw={500}>
                  Manage Server
                </Text>
              </Group>
              <IconArrowRight size={16} />
            </Group>
          </Box>
        </Link>
      </Stack>
    </Card>
  );
}
