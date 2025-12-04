/**
 * Create Playlist Modal Component
 *
 * Modal for creating a new playlist with options to:
 * 1. Set name and description
 * 2. Optionally add initial clips
 * 3. Invite collaborators (TODO: needs user search)
 */

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Group,
  Modal,
  Stack,
  Text,
  TextInput,
  Textarea,
  Tooltip,
} from "@mantine/core";
import {
  IconCheck,
  IconPlaylistAdd,
  IconVideo,
  IconX,
} from "@tabler/icons-react";
import { addClipsToPlaylist, createPlaylist } from "@repo/shared";
import { notifications } from "@mantine/notifications";

type CreatePlaylistModalProps = {
  opened: boolean;
  onClose: () => void;
  /** Callback when playlist is successfully created */
  onSuccess?: () => void;
  /** Optional: pre-selected clips to add to playlist */
  initialClipIds?: Array<string>;
};

export function CreatePlaylistModal({
  opened,
  onClose,
  onSuccess,
  initialClipIds = [],
}: CreatePlaylistModalProps) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [selectedClipIds, setSelectedClipIds] = useState<Array<string>>(initialClipIds);

  const createMutation = useMutation({
    mutationFn: async (request: { name: string; description?: string }) => {
      const playlist = await createPlaylist(request);

      // If we have initial clips, add them to the playlist
      if (playlist && selectedClipIds.length > 0) {
        await addClipsToPlaylist(playlist.id, {
          clipIds: selectedClipIds,
        });
      }

      return playlist;
    },
    onSuccess: () => {
      notifications.show({
        title: 'Playlist created',
        message: `"${name}" has been created successfully`,
        color: 'green',
      });
      handleClose();
      onSuccess?.();
    },
    onError: () => {
      notifications.show({
        title: 'Error',
        message: 'Failed to create playlist',
        color: 'red',
      });
    },
  });

  const handleCreate = () => {
    if (!name.trim()) return;

    createMutation.mutate({
      name: name.trim(),
      description: description.trim() || undefined,
    });
  };

  const handleClose = () => {
    setName("");
    setDescription("");
    setSelectedClipIds(initialClipIds);
    onClose();
  };

  const handleRemoveClip = (clipId: string) => {
    setSelectedClipIds(selectedClipIds.filter((id) => id !== clipId));
  };

  const isValid = name.trim().length > 0;

  return (
    <Modal
      opened={opened}
      onClose={handleClose}
      title={
        <Group gap="xs">
          <IconPlaylistAdd size={24} />
          <Text fw={600} size="lg">
            Create Playlist
          </Text>
        </Group>
      }
      size="lg"
      styles={{
        content: {
          background: "var(--mantine-color-dark-7)",
        },
        header: {
          background: "var(--mantine-color-dark-7)",
          borderBottom: "1px solid rgba(255, 255, 255, 0.1)",
        },
        body: {
          padding: "24px",
        },
      }}
    >
      <Stack gap="lg">
        {/* Name & Description */}
        <Stack gap="md">
          <TextInput
            label="Playlist Name"
            placeholder="e.g., Apex Session - Jan 17"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            size="md"
            styles={{
              input: {
                borderRadius: "8px",
              },
            }}
          />

          <Textarea
            label="Description"
            placeholder="What's this playlist about?"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            minRows={3}
            maxRows={5}
            size="md"
            styles={{
              input: {
                borderRadius: "8px",
              },
            }}
          />
        </Stack>

        {/* TODO: Collaborators section - needs user search API */}
        {/* <Stack gap="sm">
          <Group gap="xs">
            <IconUsers size={18} />
            <Text fw={600} size="sm">
              Invite Collaborators
            </Text>
            <Badge size="sm" variant="light" color="blue">
              Optional
            </Badge>
          </Group>
          <Text size="xs" c="dimmed">
            Collaborators can add clips, invite others, and manage the playlist
          </Text>
        </Stack> */}

        {/* Initial Clips Display */}
        {selectedClipIds.length > 0 && (
          <Stack gap="sm">
            <Group gap="xs">
              <IconVideo size={18} />
              <Text fw={600} size="sm">
                Initial Clips
              </Text>
              <Badge size="sm" variant="light" color="green">
                {selectedClipIds.length}
              </Badge>
            </Group>

            <Stack gap="xs">
              {selectedClipIds.map((clipId) => (
                <Card
                  key={clipId}
                  p="sm"
                  radius="md"
                  style={{
                    background: "rgba(255, 255, 255, 0.02)",
                    border: "1px solid rgba(255, 255, 255, 0.05)",
                  }}
                >
                  <Group justify="space-between" wrap="nowrap">
                    <Text size="sm" fw={500}>
                      Clip {clipId.slice(0, 8)}...
                    </Text>

                    <Tooltip label="Remove clip">
                      <ActionIcon
                        variant="subtle"
                        color="red"
                        size="sm"
                        onClick={() => handleRemoveClip(clipId)}
                      >
                        <IconX size={14} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </Card>
              ))}
            </Stack>

            <Text size="xs" c="dimmed">
              You and collaborators can add more clips after creating the playlist
            </Text>
          </Stack>
        )}

        {/* Actions */}
        <Group justify="flex-end" mt="md">
          <Button
            variant="subtle"
            onClick={handleClose}
            disabled={createMutation.isPending}
          >
            Cancel
          </Button>
          <Button
            leftSection={createMutation.isPending ? null : <IconCheck size={18} />}
            onClick={handleCreate}
            disabled={!isValid}
            loading={createMutation.isPending}
            variant="filled"
            color="green"
          >
            Create Playlist
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
