import { Modal, TextInput, NumberInput, Stack, Button, Group, Select } from '@mantine/core';
import { useForm } from '@mantine/form';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { notifications } from '@mantine/notifications';
import { IconServer2 } from '@tabler/icons-react';
import { createServer } from '@repo/shared/services/minecraft';

interface CreateServerModalProps {
  opened: boolean;
  onClose: () => void;
}

interface CreateServerForm {
  name: string;
  minecraftVersion: string;
  maxPlayers: number;
  motd: string;
  serverType: string;
  ramReservation: number;
  ramLimit: number;
}

export function CreateServerModal({ opened, onClose }: CreateServerModalProps) {
  const queryClient = useQueryClient();

  const form = useForm<CreateServerForm>({
    initialValues: {
      name: '',
      minecraftVersion: '1.21',
      maxPlayers: 20,
      motd: 'A Minecraft Server',
      serverType: '0', // Vanilla
      ramReservation: 2048,
      ramLimit: 4096,
    },
    validate: {
      name: (value) => (!value ? 'Server name is required' : null),
      minecraftVersion: (value) => (!value ? 'Minecraft version is required' : null),
      maxPlayers: (value) => (value < 1 || value > 100 ? 'Must be between 1 and 100' : null),
      ramReservation: (value) => (value < 512 ? 'Minimum 512 MB' : null),
      ramLimit: (value) => (value < 512 ? 'Minimum 512 MB' : null),
    },
  });

  const createServerMutation = useMutation({
    mutationFn: async (values: CreateServerForm) => {
      // Generate container name from server name (lowercase, alphanumeric with dashes)
      const containerName = values.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'minecraft-server';
      return createServer({
        name: values.name,
        minecraftVersion: values.minecraftVersion,
        serverType: parseInt(values.serverType),
        ramReservation: values.ramReservation,
        ramLimit: values.ramLimit,
        cpuReservation: 1,
        cpuLimit: 2,
        containerName,
        persistenceLocation: `/data/minecraft/${containerName}`
      });
    },
    onSuccess: (data) => {
      notifications.show({
        title: 'Server Created',
        message: `${data.name} has been created successfully`,
        color: 'green',
      });
      queryClient.invalidateQueries({ queryKey: ['minecraft', 'servers'] });
      form.reset();
      onClose();
    },
    onError: (error: Error) => {
      notifications.show({
        title: 'Failed to Create Server',
        message: error.message,
        color: 'red',
      });
    },
  });

  const handleSubmit = (values: CreateServerForm) => {
    createServerMutation.mutate(values);
  };

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title="Create New Server"
      size="lg"
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
      <form onSubmit={form.onSubmit(handleSubmit)}>
        <Stack gap="md">
          <TextInput
            label="Server Name"
            placeholder="My Minecraft Server"
            required
            {...form.getInputProps('name')}
          />

          <Select
            label="Server Type"
            required
            data={[
              { value: '0', label: 'Vanilla' },
              { value: '1', label: 'Paper' },
              { value: '2', label: 'Fabric' },
              { value: '3', label: 'Forge' },
            ]}
            {...form.getInputProps('serverType')}
          />

          <TextInput
            label="Minecraft Version"
            placeholder="1.21"
            required
            {...form.getInputProps('minecraftVersion')}
          />

          <NumberInput
            label="Max Players"
            placeholder="20"
            min={1}
            max={100}
            required
            {...form.getInputProps('maxPlayers')}
          />

          <TextInput
            label="MOTD (Message of the Day)"
            placeholder="A Minecraft Server"
            {...form.getInputProps('motd')}
          />

          <Group grow>
            <NumberInput
              label="RAM Reservation (MB)"
              placeholder="2048"
              min={512}
              step={512}
              required
              {...form.getInputProps('ramReservation')}
            />

            <NumberInput
              label="RAM Limit (MB)"
              placeholder="4096"
              min={512}
              step={512}
              required
              {...form.getInputProps('ramLimit')}
            />
          </Group>

          <Group justify="flex-end" mt="md">
            <Button variant="subtle" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              leftSection={<IconServer2 size={18} />}
              variant="gradient"
              gradient={{ from: 'cyberBlue', to: 'cyberPurple', deg: 135 }}
              loading={createServerMutation.isPending}
            >
              Create Server
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
