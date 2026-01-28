import { createFileRoute } from '@tanstack/react-router';
import { useState } from 'react';
import {
  Container,
  Title,
  Text,
  Stack,
  Box,
  Group,
  Button,
  Table,
  Badge,
  Loader,
  ThemeIcon,
  Paper,
  SimpleGrid,
  ActionIcon,
  Tooltip,
} from '@mantine/core';
import {
  IconCloudUpload,
  IconCloudCheck,
  IconCloudOff,
  IconRefresh,
  IconFileZip,
  IconCalendar,
  IconDatabase,
  IconAlertCircle,
} from '@tabler/icons-react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import {
  getBackupStatus,
  triggerBackupSync,
} from '@repo/shared/services/minecraft';
import type { BackupFileInfo } from '@repo/nucleus-api-client';

export const Route = createFileRoute('/servers/$serverId/backups')({
  component: BackupsPage,
});

function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatDate(date: Date): string {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(date));
}

function formatRelativeTime(date: Date): string {
  const now = new Date();
  const diff = now.getTime() - new Date(date).getTime();
  const minutes = Math.floor(diff / 60000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (days > 0) return `${days}d ago`;
  if (hours > 0) return `${hours}h ago`;
  if (minutes > 0) return `${minutes}m ago`;
  return 'Just now';
}

interface StatCardProps {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  color: string;
}

function StatCard({ icon, label, value, color }: StatCardProps) {
  return (
    <Paper
      p="md"
      style={{
        background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
        border: `1px solid ${color}30`,
        borderRadius: 12,
      }}
    >
      <Group gap="md">
        <ThemeIcon
          size={44}
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
        <Stack gap={2}>
          <Text size="xs" c="dimmed" tt="uppercase" fw={600}>
            {label}
          </Text>
          <Text size="xl" fw={700} style={{ color }}>
            {value}
          </Text>
        </Stack>
      </Group>
    </Paper>
  );
}

function BackupsPage() {
  const { serverId } = Route.useParams();
  const queryClient = useQueryClient();
  const [isSyncing, setIsSyncing] = useState(false);

  const {
    data: backupData,
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: ['minecraft', 'backups', serverId],
    queryFn: () => getBackupStatus(serverId),
    refetchInterval: 30000,
  });

  const syncMutation = useMutation({
    mutationFn: () => triggerBackupSync(serverId),
    onMutate: () => {
      setIsSyncing(true);
    },
    onSuccess: (result) => {
      if (result.success) {
        notifications.show({
          title: 'Backup Sync Complete',
          message: `${result.filesUploaded} file(s) uploaded, ${result.filesSkipped} skipped`,
          color: 'green',
          autoClose: 5000,
        });
      } else {
        notifications.show({
          title: 'Backup Sync Failed',
          message: result.message || 'Could not sync backups',
          color: 'red',
        });
      }
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'backups', serverId] });
    },
    onError: (error) => {
      notifications.show({
        title: 'Error',
        message: error instanceof Error ? error.message : 'Failed to sync backups',
        color: 'red',
      });
    },
    onSettled: () => {
      setIsSyncing(false);
    },
  });

  const isConfigured = backupData?.isConfigured ?? false;
  const localFiles = backupData?.localFiles ?? [];
  const pendingSync = backupData?.pendingSyncCount ?? 0;

  const totalSize = localFiles.reduce((sum, file) => sum + file.size, 0);
  const latestBackup = localFiles.length > 0
    ? localFiles.reduce((latest, file) =>
        new Date(file.lastModified) > new Date(latest.lastModified) ? file : latest
      )
    : null;

  if (isLoading) {
    return (
      <Container size="xl" py="md">
        <Stack align="center" justify="center" h={400}>
          <Loader size="xl" color="cyberBlue" />
          <Text c="dimmed">Loading backup information...</Text>
        </Stack>
      </Container>
    );
  }

  if (error) {
    return (
      <Container size="xl" py="md">
        <Stack align="center" justify="center" h={400}>
          <ThemeIcon size={60} radius="xl" color="red" variant="light">
            <IconAlertCircle size={30} />
          </ThemeIcon>
          <Text c="red" fw={500}>Failed to load backup information</Text>
          <Button onClick={() => refetch()} leftSection={<IconRefresh size={16} />}>
            Retry
          </Button>
        </Stack>
      </Container>
    );
  }

  return (
    <Container size="xl" py="md">
      <Stack gap="xl">
        {/* Header */}
        <Box>
          <Group gap="md" mb="xs" justify="space-between">
            <Group gap="md">
              <Box
                style={{
                  width: 4,
                  height: 32,
                  borderRadius: 2,
                  background: 'linear-gradient(180deg, #3b82f6 0%, #8b5cf6 100%)',
                  boxShadow: '0 0 10px rgba(59, 130, 246, 0.5)',
                }}
              />
              <Box>
                <Title
                  order={2}
                  style={{
                    background: 'linear-gradient(90deg, #ffffff 0%, #a0a0a0 100%)',
                    WebkitBackgroundClip: 'text',
                    WebkitTextFillColor: 'transparent',
                  }}
                >
                  Backups
                </Title>
                <Text size="sm" c="dimmed">
                  Manage and sync your server backups
                </Text>
              </Box>
            </Group>

            <Group gap="sm">
              <Tooltip label="Refresh backup list">
                <ActionIcon
                  variant="light"
                  size="lg"
                  onClick={() => refetch()}
                  style={{
                    background: 'rgba(0, 212, 255, 0.1)',
                    border: '1px solid rgba(0, 212, 255, 0.2)',
                  }}
                >
                  <IconRefresh size={18} />
                </ActionIcon>
              </Tooltip>
              <Button
                leftSection={isSyncing ? <Loader size={16} color="white" /> : <IconCloudUpload size={18} />}
                variant="gradient"
                gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
                onClick={() => syncMutation.mutate()}
                disabled={!isConfigured || isSyncing}
                style={{ boxShadow: '0 0 15px rgba(0, 212, 255, 0.3)' }}
              >
                {isSyncing ? 'Syncing...' : 'Sync Backups'}
              </Button>
            </Group>
          </Group>
        </Box>

        {/* Status Banner */}
        {!isConfigured && (
          <Paper
            p="md"
            style={{
              background: 'rgba(251, 191, 36, 0.1)',
              border: '1px solid rgba(251, 191, 36, 0.3)',
              borderRadius: 12,
            }}
          >
            <Group gap="md">
              <IconCloudOff size={24} style={{ color: '#fbbf24' }} />
              <Box>
                <Text fw={600} c="yellow">Backup Not Configured</Text>
                <Text size="sm" c="dimmed">
                  Cloud backup sync is not configured for this server. Backups are stored locally only.
                </Text>
              </Box>
            </Group>
          </Paper>
        )}

        {/* Stats Grid */}
        <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }} spacing="md">
          <StatCard
            icon={<IconFileZip size={22} />}
            label="Total Backups"
            value={localFiles.length}
            color="#00d4ff"
          />
          <StatCard
            icon={<IconDatabase size={22} />}
            label="Total Size"
            value={formatFileSize(totalSize)}
            color="#a855f7"
          />
          <StatCard
            icon={<IconCalendar size={22} />}
            label="Latest Backup"
            value={latestBackup ? formatRelativeTime(latestBackup.lastModified) : 'None'}
            color="#00ff88"
          />
          <StatCard
            icon={<IconCloudUpload size={22} />}
            label="Pending Sync"
            value={pendingSync}
            color={pendingSync > 0 ? '#f97316' : '#3b82f6'}
          />
        </SimpleGrid>

        {/* Backup List */}
        <Box
          p="lg"
          style={{
            background: 'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
            borderRadius: 14,
            border: '1px solid rgba(59, 130, 246, 0.15)',
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
              background: 'linear-gradient(90deg, #3b82f6 0%, #8b5cf6 100%)',
            }}
          />

          <Group gap="sm" mb="md">
            <IconFileZip
              size={22}
              style={{
                color: '#3b82f6',
                filter: 'drop-shadow(0 0 4px rgba(59, 130, 246, 0.5))',
              }}
            />
            <Text size="lg" fw={700}>Local Backups</Text>
            <Badge
              size="sm"
              variant="light"
              color="blue"
              style={{
                background: 'rgba(59, 130, 246, 0.1)',
                border: '1px solid rgba(59, 130, 246, 0.2)',
              }}
            >
              {localFiles.length} files
            </Badge>
          </Group>

          {localFiles.length === 0 ? (
            <Stack align="center" justify="center" py="xl">
              <ThemeIcon size={60} radius="xl" variant="light" color="gray">
                <IconFileZip size={30} />
              </ThemeIcon>
              <Text c="dimmed">No backups found</Text>
              <Text size="xs" c="dimmed">
                Backups will appear here once created
              </Text>
            </Stack>
          ) : (
            <Table.ScrollContainer minWidth={500}>
              <Table
                highlightOnHover
                styles={{
                  th: {
                    color: 'var(--mantine-color-dimmed)',
                    fontWeight: 600,
                    textTransform: 'uppercase',
                    fontSize: '0.75rem',
                    letterSpacing: '0.05em',
                  },
                  tr: {
                    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
                  },
                }}
              >
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Filename</Table.Th>
                    <Table.Th>Size</Table.Th>
                    <Table.Th>Modified</Table.Th>
                    <Table.Th>Status</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {localFiles
                    .sort((a, b) => new Date(b.lastModified).getTime() - new Date(a.lastModified).getTime())
                    .map((file: BackupFileInfo, index: number) => (
                      <Table.Tr key={index}>
                        <Table.Td>
                          <Group gap="sm">
                            <IconFileZip size={18} style={{ color: '#3b82f6' }} />
                            <Text size="sm" ff="monospace">
                              {file.path.split('/').pop()}
                            </Text>
                          </Group>
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" c="dimmed">
                            {formatFileSize(file.size)}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          <Stack gap={0}>
                            <Text size="sm">{formatDate(file.lastModified)}</Text>
                            <Text size="xs" c="dimmed">
                              {formatRelativeTime(file.lastModified)}
                            </Text>
                          </Stack>
                        </Table.Td>
                        <Table.Td>
                          <Badge
                            size="sm"
                            variant="light"
                            color="green"
                            leftSection={<IconCloudCheck size={12} />}
                          >
                            Synced
                          </Badge>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          )}
        </Box>
      </Stack>
    </Container>
  );
}
