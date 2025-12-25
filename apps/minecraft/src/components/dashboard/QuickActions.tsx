import { Box, Text, Stack, Group, ThemeIcon, UnstyledButton, Tooltip } from '@mantine/core';
import { useNavigate, useParams } from '@tanstack/react-router';
import {
  IconBolt,
  IconTerminal2,
  IconFolderCode,
  IconRefresh,
  IconDownload,
} from '@tabler/icons-react';
import { notifications } from '@mantine/notifications';

interface QuickActionProps {
  icon: React.ReactNode;
  label: string;
  description: string;
  color: string;
  onClick: () => void;
  disabled?: boolean;
}

function QuickAction({ icon, label, description, color, onClick, disabled }: QuickActionProps) {
  return (
    <Tooltip label={disabled ? 'Not available' : description} position="top">
      <UnstyledButton
        onClick={onClick}
        disabled={disabled}
        style={{
          width: '100%',
          padding: '12px',
          borderRadius: 10,
          background: 'rgba(0, 212, 255, 0.03)',
          border: '1px solid rgba(0, 212, 255, 0.1)',
          transition: 'all 0.2s ease',
          opacity: disabled ? 0.5 : 1,
          cursor: disabled ? 'not-allowed' : 'pointer',
        }}
        className={disabled ? '' : 'cyber-action-card'}
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
            {icon}
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

export function QuickActions() {
  const navigate = useNavigate();
  const { serverId } = useParams({ from: '/servers/$serverId' });

  const handleRestart = () => {
    notifications.show({
      title: 'Server Restart',
      message: 'Server restart initiated...',
      color: 'orange',
      autoClose: 3000,
    });
  };

  const handleBackup = () => {
    notifications.show({
      title: 'Backup Started',
      message: 'Creating server backup...',
      color: 'blue',
      autoClose: 3000,
    });
  };

  const actions = [
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
      icon: <IconRefresh size={20} />,
      label: 'Restart Server',
      description: 'Graceful server restart',
      color: '#ec4899',
      onClick: handleRestart,
    },
    {
      icon: <IconDownload size={20} />,
      label: 'Create Backup',
      description: 'Backup world data',
      color: '#00ff88',
      onClick: handleBackup,
    },
  ];

  return (
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
  );
}
