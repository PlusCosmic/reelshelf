import { useState } from 'react';
import {
  Box,
  Text,
  Stack,
  Group,
  ThemeIcon,
  UnstyledButton,
  Tooltip,
  Modal,
  Button,
  Checkbox,
  Loader,
} from '@mantine/core';
import { useNavigate, useParams } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import {
  IconBolt,
  IconTerminal2,
  IconFolderCode,
  IconPlayerPlay,
  IconPlayerStop,
  IconRefresh,
  IconSettings,
  IconCloudUpload,
  IconAlertTriangle,
} from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';
import { useServerStatus } from '../../hooks/useServerStatus';
import {
  startContainer,
  stopContainer,
  restartContainer,
} from '@repo/shared/services/minecraft';

interface QuickActionProps {
  icon: React.ReactNode;
  label: string;
  description: string;
  color: string;
  onClick: () => void;
  disabled?: boolean;
  loading?: boolean;
}

function QuickAction({ icon, label, description, color, onClick, disabled, loading }: QuickActionProps) {
  return (
    <Tooltip label={disabled ? 'Not available' : description} position="top">
      <UnstyledButton
        onClick={onClick}
        disabled={disabled || loading}
        style={{
          width: '100%',
          padding: '12px',
          borderRadius: 10,
          background: 'rgba(0, 212, 255, 0.03)',
          border: '1px solid rgba(0, 212, 255, 0.1)',
          transition: 'all 0.2s ease',
          opacity: disabled ? 0.5 : 1,
          cursor: disabled || loading ? 'not-allowed' : 'pointer',
        }}
        className={disabled || loading ? '' : 'cyber-action-card'}
      >
        <Group gap="sm">
          <ThemeIcon
            size={36}
            radius="md"
            variant="light"
            style={{
              background: `${color}15`,
              border: `1px solid ${color}30`,
              color,
            }}
          >
            {loading ? <Loader size={18} color={color} /> : icon}
          </ThemeIcon>
          <Stack gap={0} flex={1}>
            <Text size="sm" fw={600}>{label}</Text>
            <Text size="xs" c="dimmed" lineClamp={1}>{description}</Text>
          </Stack>
        </Group>
      </UnstyledButton>
    </Tooltip>
  );
}

type ConfirmAction = 'start' | 'stop' | 'restart' | null;

export function QuickActions() {
  const navigate = useNavigate();
  const { serverId } = useParams({ from: '/servers/$serverId' });
  const queryClient = useQueryClient();
  const { data: status } = useServerStatus(serverId);

  const [confirmAction, setConfirmAction] = useState<ConfirmAction>(null);
  const [announceToPlayers, setAnnounceToPlayers] = useState(true);
  const [isLoading, setIsLoading] = useState<ConfirmAction>(null);

  const isOnline = status?.isOnline ?? false;
  const hasPlayers = (status?.onlinePlayers ?? 0) > 0;

  const invalidateQueries = () => {
    queryClient.invalidateQueries({ queryKey: ['minecraft', 'status', serverId] });
    queryClient.invalidateQueries({ queryKey: ['minecraft', 'container', serverId] });
  };

  const handleStartServer = async () => {
    setIsLoading('start');
    setConfirmAction(null);
    try {
      const result = await startContainer(serverId);
      if (result.success) {
        notifications.show({
          title: 'Server Starting',
          message: result.message || 'Server is starting up...',
          color: 'green',
          autoClose: 5000,
        });
        invalidateQueries();
      } else {
        notifications.show({
          title: 'Failed to Start',
          message: result.message || 'Could not start the server',
          color: 'red',
        });
      }
    } catch (error) {
      notifications.show({
        title: 'Error',
        message: error instanceof Error ? error.message : 'Failed to start server',
        color: 'red',
      });
    } finally {
      setIsLoading(null);
    }
  };

  const handleStopServer = async () => {
    setIsLoading('stop');
    setConfirmAction(null);
    try {
      const result = await stopContainer(serverId, 30, announceToPlayers);
      if (result.success) {
        notifications.show({
          title: 'Server Stopping',
          message: result.message || 'Server is shutting down...',
          color: 'orange',
          autoClose: 5000,
        });
        invalidateQueries();
      } else {
        notifications.show({
          title: 'Failed to Stop',
          message: result.message || 'Could not stop the server',
          color: 'red',
        });
      }
    } catch (error) {
      notifications.show({
        title: 'Error',
        message: error instanceof Error ? error.message : 'Failed to stop server',
        color: 'red',
      });
    } finally {
      setIsLoading(null);
    }
  };

  const handleRestartServer = async () => {
    setIsLoading('restart');
    setConfirmAction(null);
    try {
      const result = await restartContainer(serverId, announceToPlayers);
      if (result.success) {
        notifications.show({
          title: 'Server Restarting',
          message: result.message || 'Server is restarting...',
          color: 'blue',
          autoClose: 5000,
        });
        invalidateQueries();
      } else {
        notifications.show({
          title: 'Failed to Restart',
          message: result.message || 'Could not restart the server',
          color: 'red',
        });
      }
    } catch (error) {
      notifications.show({
        title: 'Error',
        message: error instanceof Error ? error.message : 'Failed to restart server',
        color: 'red',
      });
    } finally {
      setIsLoading(null);
    }
  };

  const getModalContent = () => {
    switch (confirmAction) {
      case 'start':
        return {
          title: 'Start Server',
          message: 'Are you sure you want to start the server?',
          color: 'green',
          onConfirm: handleStartServer,
          showAnnounce: false,
        };
      case 'stop':
        return {
          title: 'Stop Server',
          message: hasPlayers
            ? `There are ${status?.onlinePlayers} player(s) online. Are you sure you want to stop the server?`
            : 'Are you sure you want to stop the server?',
          color: 'red',
          onConfirm: handleStopServer,
          showAnnounce: hasPlayers,
        };
      case 'restart':
        return {
          title: 'Restart Server',
          message: hasPlayers
            ? `There are ${status?.onlinePlayers} player(s) online. The server will restart and players will be disconnected.`
            : 'Are you sure you want to restart the server?',
          color: 'orange',
          onConfirm: handleRestartServer,
          showAnnounce: hasPlayers,
        };
      default:
        return null;
    }
  };

  const modalContent = getModalContent();

  const actions = [
    // Power controls based on server state
    ...(isOnline
      ? [
          {
            icon: <IconPlayerStop size={20} />,
            label: 'Stop Server',
            description: 'Gracefully stop the server',
            color: '#ff4444',
            onClick: () => setConfirmAction('stop'),
            loading: isLoading === 'stop',
          },
          {
            icon: <IconRefresh size={20} />,
            label: 'Restart Server',
            description: 'Restart the server',
            color: '#f97316',
            onClick: () => setConfirmAction('restart'),
            loading: isLoading === 'restart',
          },
        ]
      : [
          {
            icon: <IconPlayerPlay size={20} />,
            label: 'Start Server',
            description: 'Start the server',
            color: '#00ff88',
            onClick: () => setConfirmAction('start'),
            loading: isLoading === 'start',
          },
        ]),
    // Navigation actions
    {
      icon: <IconTerminal2 size={20} />,
      label: 'Open Console',
      description: 'Access server terminal',
      color: '#00d4ff',
      onClick: () => navigate({ to: '/servers/$serverId/console', params: { serverId } }),
    },
    {
      icon: <IconFolderCode size={20} />,
      label: 'File Manager',
      description: 'Browse server files',
      color: '#a855f7',
      onClick: () => navigate({ to: '/servers/$serverId/files', params: { serverId } }),
    },
    {
      icon: <IconCloudUpload size={20} />,
      label: 'Backups',
      description: 'Manage server backups',
      color: '#3b82f6',
      onClick: () => navigate({ to: '/servers/$serverId/backups', params: { serverId } }),
    },
    {
      icon: <IconSettings size={20} />,
      label: 'Settings',
      description: 'Server configuration',
      color: '#8b5cf6',
      onClick: () => navigate({ to: '/servers/$serverId/settings', params: { serverId } }),
    },
  ];

  return (
    <>
      <Box
        p="lg"
        style={{
          background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
          borderRadius: 14,
          border: '1px solid rgba(168, 85, 247, 0.15)',
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
            background: 'linear-gradient(90deg, #a855f7 0%, #ec4899 100%)',
          }}
        />

        <Stack gap="md">
          {/* Header */}
          <Group gap="sm">
            <IconBolt
              size={22}
              style={{
                color: '#a855f7',
                filter: 'drop-shadow(0 0 4px rgba(168, 85, 247, 0.5))',
              }}
            />
            <Text size="lg" fw={700}>Quick Actions</Text>
          </Group>

          {/* Actions Grid */}
          <Stack gap="xs">
            {actions.map((action, index) => (
              <QuickAction key={index} {...action} />
            ))}
          </Stack>
        </Stack>
      </Box>

      {/* Confirmation Modal */}
      <Modal
        opened={confirmAction !== null}
        onClose={() => setConfirmAction(null)}
        title={
          <Group gap="sm">
            <IconAlertTriangle size={20} style={{ color: modalContent?.color }} />
            <Text fw={600}>{modalContent?.title}</Text>
          </Group>
        }
        centered
        styles={{
          header: {
            background: 'linear-gradient(90deg, rgba(0, 212, 255, 0.05) 0%, rgba(168, 85, 247, 0.08) 100%)',
            borderBottom: '1px solid rgba(0, 212, 255, 0.2)',
          },
          content: {
            background: 'var(--mantine-color-dark-7)',
          },
        }}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {modalContent?.message}
          </Text>

          {modalContent?.showAnnounce && (
            <Checkbox
              checked={announceToPlayers}
              onChange={(e) => setAnnounceToPlayers(e.currentTarget.checked)}
              label="Announce to players before action"
              description="Players will receive a warning message"
            />
          )}

          <Group justify="flex-end" mt="md">
            <Button variant="subtle" onClick={() => setConfirmAction(null)}>
              Cancel
            </Button>
            <Button
              color={modalContent?.color}
              onClick={modalContent?.onConfirm}
              leftSection={
                confirmAction === 'start' ? <IconPlayerPlay size={16} /> :
                confirmAction === 'stop' ? <IconPlayerStop size={16} /> :
                <IconRefresh size={16} />
              }
            >
              {modalContent?.title}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
