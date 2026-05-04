import { createFileRoute } from '@tanstack/react-router';
import { useEffect } from 'react';
import {
  Container,
  Title,
  Text,
  Stack,
  Box,
  Group,
  Button,
  TextInput,
  NumberInput,
  Select,
  Loader,
  ThemeIcon,
  Paper,
  Divider,
  Switch,
  Textarea,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import {
  IconSettings,
  IconServer2,
  IconCpu,
  IconAlertCircle,
  IconDeviceFloppy,
  IconRefresh,
} from '@tabler/icons-react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import { getServer, updateServer } from '@repo/shared/services/minecraft';
import type { UpdateMinecraftServerRequest } from '@repo/nucleus-api-client';

export const Route = createFileRoute('/servers/$serverId/settings')({
  component: SettingsPage,
});

const SERVER_TYPES = [
  { value: '0', label: 'Vanilla' },
  { value: '1', label: 'Paper' },
  { value: '2', label: 'Fabric' },
  { value: '3', label: 'Forge' },
];

interface SettingsForm {
  name: string;
  minecraftVersion: string;
  serverType: string;
  motd: string;
  maxPlayers: number;
  ramReservation: number;
  ramLimit: number;
  cpuReservation: number;
  cpuLimit: number;
  modloaderVersion: string;
  curseforgePageUrl: string;
  isActive: boolean;
}

function SettingsPage() {
  const { serverId } = Route.useParams();
  const queryClient = useQueryClient();

  const {
    data: server,
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: ['minecraft', 'server', serverId],
    queryFn: () => getServer(serverId),
  });

  const form = useForm<SettingsForm>({
    initialValues: {
      name: '',
      minecraftVersion: '',
      serverType: '0',
      motd: '',
      maxPlayers: 20,
      ramReservation: 2048,
      ramLimit: 4096,
      cpuReservation: 1,
      cpuLimit: 2,
      modloaderVersion: '',
      curseforgePageUrl: '',
      isActive: true,
    },
    validate: {
      name: (value) => (!value ? 'Server name is required' : null),
      minecraftVersion: (value) => (!value ? 'Minecraft version is required' : null),
      maxPlayers: (value) =>
        value < 1 || value > 1000 ? 'Must be between 1 and 1000' : null,
      ramReservation: (value) => (value < 512 ? 'Minimum 512 MB' : null),
      ramLimit: (value, values) =>
        value < values.ramReservation
          ? 'Limit must be greater than reservation'
          : null,
      cpuReservation: (value) => (value < 0.5 ? 'Minimum 0.5 cores' : null),
      cpuLimit: (value, values) =>
        value < values.cpuReservation
          ? 'Limit must be greater than reservation'
          : null,
    },
  });

  // Populate form when server data loads
  useEffect(() => {
    if (server) {
      form.setValues({
        name: server.name || '',
        minecraftVersion: server.minecraftVersion || '',
        serverType: String(server.serverType ?? 0),
        motd: server.motd || '',
        maxPlayers: server.maxPlayers ?? 20,
        ramReservation: server.ramReservation ?? 2048,
        ramLimit: server.ramLimit ?? 4096,
        cpuReservation: server.cpuReservation ?? 1,
        cpuLimit: server.cpuLimit ?? 2,
        modloaderVersion: server.modloaderVersion || '',
        curseforgePageUrl: server.curseforgePageUrl || '',
        isActive: server.isActive ?? true,
      });
      form.resetDirty();
    }
  }, [server]);

  const updateMutation = useMutation({
    mutationFn: (values: SettingsForm) => {
      const updates: UpdateMinecraftServerRequest = {
        name: values.name,
        minecraftVersion: values.minecraftVersion,
        serverType: parseInt(values.serverType),
        ramReservation: values.ramReservation,
        ramLimit: values.ramLimit,
        cpuReservation: values.cpuReservation,
        cpuLimit: values.cpuLimit,
        modloaderVersion: values.modloaderVersion || null,
        curseforgePageUrl: values.curseforgePageUrl || null,
        isActive: values.isActive,
      };
      return updateServer(serverId, updates);
    },
    onSuccess: () => {
      notifications.show({
        title: 'Settings Saved',
        message: 'Server settings have been updated successfully',
        color: 'green',
        autoClose: 5000,
      });
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'server', serverId] });
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'servers'] });
      form.resetDirty();
    },
    onError: (error) => {
      notifications.show({
        title: 'Failed to Save',
        message: error instanceof Error ? error.message : 'Could not save settings',
        color: 'red',
      });
    },
  });

  const handleSubmit = (values: SettingsForm) => {
    updateMutation.mutate(values);
  };

  const handleReset = () => {
    if (server) {
      form.setValues({
        name: server.name || '',
        minecraftVersion: server.minecraftVersion || '',
        serverType: String(server.serverType ?? 0),
        motd: server.motd || '',
        maxPlayers: server.maxPlayers ?? 20,
        ramReservation: server.ramReservation ?? 2048,
        ramLimit: server.ramLimit ?? 4096,
        cpuReservation: server.cpuReservation ?? 1,
        cpuLimit: server.cpuLimit ?? 2,
        modloaderVersion: server.modloaderVersion || '',
        curseforgePageUrl: server.curseforgePageUrl || '',
        isActive: server.isActive ?? true,
      });
      form.resetDirty();
    }
  };

  if (isLoading) {
    return (
      <Container size="xl" py="md">
        <Stack align="center" justify="center" h={400}>
          <Loader size="xl" color="cyberBlue" />
          <Text c="dimmed">Loading server settings...</Text>
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
          <Text c="red" fw={500}>Failed to load server settings</Text>
          <Button onClick={() => refetch()} leftSection={<IconRefresh size={16} />}>
            Retry
          </Button>
        </Stack>
      </Container>
    );
  }

  return (
    <Container size="xl" py="md">
      <form onSubmit={form.onSubmit(handleSubmit)}>
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
                    background: 'linear-gradient(180deg, #8b5cf6 0%, #ec4899 100%)',
                    boxShadow: '0 0 10px rgba(139, 92, 246, 0.5)',
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
                    Server Settings
                  </Title>
                  <Text size="sm" c="dimmed">
                    Configure your Minecraft server
                  </Text>
                </Box>
              </Group>

              <Group gap="sm">
                <Button
                  variant="subtle"
                  onClick={handleReset}
                  disabled={!form.isDirty()}
                >
                  Reset
                </Button>
                <Button
                  type="submit"
                  leftSection={
                    updateMutation.isPending ? (
                      <Loader size={16} color="white" />
                    ) : (
                      <IconDeviceFloppy size={18} />
                    )
                  }
                  variant="gradient"
                  gradient={{ from: 'cyberPurple', to: 'cyberPink', deg: 135 }}
                  disabled={!form.isDirty() || updateMutation.isPending}
                  style={{
                    boxShadow: form.isDirty()
                      ? '0 0 15px rgba(168, 85, 247, 0.3)'
                      : 'none',
                  }}
                >
                  {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
                </Button>
              </Group>
            </Group>
          </Box>

          {/* General Settings */}
          <Paper
            p="lg"
            style={{
              background:
                'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
              borderRadius: 14,
              border: '1px solid rgba(139, 92, 246, 0.15)',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
            <Box
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                right: 0,
                height: 2,
                background: 'linear-gradient(90deg, #8b5cf6 0%, #ec4899 100%)',
              }}
            />

            <Group gap="sm" mb="lg">
              <IconServer2
                size={22}
                style={{
                  color: '#8b5cf6',
                  filter: 'drop-shadow(0 0 4px rgba(139, 92, 246, 0.5))',
                }}
              />
              <Text size="lg" fw={700}>
                General Settings
              </Text>
            </Group>

            <Stack gap="md">
              <TextInput
                label="Server Name"
                placeholder="My Minecraft Server"
                {...form.getInputProps('name')}
              />

              <Group grow>
                <Select
                  label="Server Type"
                  data={SERVER_TYPES}
                  {...form.getInputProps('serverType')}
                />
                <TextInput
                  label="Minecraft Version"
                  placeholder="1.21"
                  {...form.getInputProps('minecraftVersion')}
                />
              </Group>

              <Textarea
                label="MOTD (Message of the Day)"
                placeholder="A Minecraft Server"
                description="Displayed in the server list"
                autosize
                minRows={2}
                maxRows={4}
                {...form.getInputProps('motd')}
              />

              <NumberInput
                label="Max Players"
                placeholder="20"
                min={1}
                max={1000}
                {...form.getInputProps('maxPlayers')}
              />

              <Switch
                label="Server Active"
                description="Inactive servers won't be displayed in the server list"
                {...form.getInputProps('isActive', { type: 'checkbox' })}
              />
            </Stack>
          </Paper>

          {/* Resource Limits */}
          <Paper
            p="lg"
            style={{
              background:
                'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
              borderRadius: 14,
              border: '1px solid rgba(0, 212, 255, 0.15)',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
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

            <Group gap="sm" mb="lg">
              <IconCpu
                size={22}
                style={{
                  color: '#00d4ff',
                  filter: 'drop-shadow(0 0 4px rgba(0, 212, 255, 0.5))',
                }}
              />
              <Text size="lg" fw={700}>
                Resource Limits
              </Text>
            </Group>

            <Stack gap="md">
              <Text size="sm" c="dimmed">
                Configure the CPU and memory limits for the server container.
                Changes may require a server restart to take effect.
              </Text>

              <Divider label="Memory (RAM)" labelPosition="left" />

              <Group grow>
                <NumberInput
                  label="RAM Reservation (MB)"
                  description="Guaranteed memory allocation"
                  placeholder="2048"
                  min={512}
                  step={256}
                  {...form.getInputProps('ramReservation')}
                />
                <NumberInput
                  label="RAM Limit (MB)"
                  description="Maximum memory allowed"
                  placeholder="4096"
                  min={512}
                  step={256}
                  {...form.getInputProps('ramLimit')}
                />
              </Group>

              <Divider label="CPU" labelPosition="left" />

              <Group grow>
                <NumberInput
                  label="CPU Reservation (cores)"
                  description="Guaranteed CPU allocation"
                  placeholder="1"
                  min={0.5}
                  step={0.5}
                  decimalScale={1}
                  {...form.getInputProps('cpuReservation')}
                />
                <NumberInput
                  label="CPU Limit (cores)"
                  description="Maximum CPU allowed"
                  placeholder="2"
                  min={0.5}
                  step={0.5}
                  decimalScale={1}
                  {...form.getInputProps('cpuLimit')}
                />
              </Group>
            </Stack>
          </Paper>

          {/* Modloader Settings */}
          <Paper
            p="lg"
            style={{
              background:
                'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
              borderRadius: 14,
              border: '1px solid rgba(0, 255, 136, 0.15)',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
            <Box
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                right: 0,
                height: 2,
                background: 'linear-gradient(90deg, #00ff88 0%, #00d4ff 100%)',
              }}
            />

            <Group gap="sm" mb="lg">
              <IconSettings
                size={22}
                style={{
                  color: '#00ff88',
                  filter: 'drop-shadow(0 0 4px rgba(0, 255, 136, 0.5))',
                }}
              />
              <Text size="lg" fw={700}>
                Modloader Settings
              </Text>
            </Group>

            <Stack gap="md">
              <Text size="sm" c="dimmed">
                Configure modloader-specific settings for Fabric, Forge, or Paper servers.
              </Text>

              <TextInput
                label="Modloader Version"
                placeholder="e.g., 0.15.11 for Fabric"
                description="Leave empty for latest version"
                {...form.getInputProps('modloaderVersion')}
              />

              <TextInput
                label="CurseForge Page URL"
                placeholder="https://www.curseforge.com/minecraft/modpacks/..."
                description="Link to the modpack on CurseForge (optional)"
                {...form.getInputProps('curseforgePageUrl')}
              />
            </Stack>
          </Paper>

          {/* Server Info (Read-only) */}
          <Paper
            p="lg"
            style={{
              background:
                'linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)',
              borderRadius: 14,
              border: '1px solid rgba(255, 255, 255, 0.1)',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
            <Group gap="sm" mb="lg">
              <Text size="lg" fw={700} c="dimmed">
                Server Information
              </Text>
            </Group>

            <Stack gap="xs">
              <Group justify="space-between">
                <Text size="sm" c="dimmed">Server ID</Text>
                <Text size="sm" ff="monospace">{server?.id}</Text>
              </Group>
              <Group justify="space-between">
                <Text size="sm" c="dimmed">Container Name</Text>
                <Text size="sm" ff="monospace">{server?.containerName}</Text>
              </Group>
              <Group justify="space-between">
                <Text size="sm" c="dimmed">Data Location</Text>
                <Text size="sm" ff="monospace">{server?.persistenceLocation}</Text>
              </Group>
              <Group justify="space-between">
                <Text size="sm" c="dimmed">Created</Text>
                <Text size="sm">
                  {server?.createdAt
                    ? new Date(server.createdAt).toLocaleDateString()
                    : 'Unknown'}
                </Text>
              </Group>
            </Stack>
          </Paper>
        </Stack>
      </form>
    </Container>
  );
}
