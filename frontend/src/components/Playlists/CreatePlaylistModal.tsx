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
import { addClipsToPlaylist, createPlaylist } from "@/shared";
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
  const [selectedClipIds, setSelectedClipIds] =
    useState<Array<string>>(initialClipIds);

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
        title: "Playlist created",
        message: `"${name}" has been created successfully`,
        color: "green",
      });
      handleClose();
      onSuccess?.();
    },
    onError: () => {
      notifications.show({
        title: "Error",
        message: "Failed to create playlist",
        color: "red",
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
          background:
            "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
          border: "1px solid rgba(0, 212, 255, 0.2)",
          boxShadow:
            "0 20px 60px rgba(0, 0, 0, 0.5), 0 0 40px rgba(0, 212, 255, 0.1)",
        },
        header: {
          background: "transparent",
          borderBottom: "1px solid rgba(0, 212, 255, 0.15)",
        },
        title: {
          color: "#00d4ff",
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
                backgroundColor: "rgba(0, 212, 255, 0.03)",
                border: "1px solid rgba(0, 212, 255, 0.15)",
                "&:focus": {
                  borderColor: "rgba(0, 212, 255, 0.5)",
                  boxShadow: "0 0 15px rgba(0, 212, 255, 0.15)",
                },
              },
              label: {
                color: "#f8fafc",
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
                backgroundColor: "rgba(0, 212, 255, 0.03)",
                border: "1px solid rgba(0, 212, 255, 0.15)",
                "&:focus": {
                  borderColor: "rgba(0, 212, 255, 0.5)",
                  boxShadow: "0 0 15px rgba(0, 212, 255, 0.15)",
                },
              },
              label: {
                color: "#f8fafc",
              },
            }}
          />
        </Stack>

        {/* Initial Clips Display */}
        {selectedClipIds.length > 0 && (
          <Stack gap="sm">
            <Group gap="xs">
              <IconVideo size={18} />
              <Text fw={600} size="sm">
                Initial Clips
              </Text>
              <Badge
                size="sm"
                variant="light"
                style={{
                  background:
                    "linear-gradient(135deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.15) 100%)",
                  border: "1px solid rgba(0, 212, 255, 0.3)",
                  color: "#00d4ff",
                }}
              >
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
                    background:
                      "linear-gradient(135deg, rgba(15, 15, 25, 0.6) 0%, rgba(20, 20, 35, 0.5) 100%)",
                    border: "1px solid rgba(0, 212, 255, 0.1)",
                  }}
                >
                  <Group justify="space-between" wrap="nowrap">
                    <Text size="sm" fw={500}>
                      Clip {clipId.slice(0, 8)}...
                    </Text>

                    <Tooltip label="Remove clip">
                      <ActionIcon
                        variant="subtle"
                        size="sm"
                        onClick={() => handleRemoveClip(clipId)}
                        style={{
                          color: "#ec4899",
                          transition: "all 0.2s ease",
                        }}
                      >
                        <IconX size={14} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                </Card>
              ))}
            </Stack>

            <Text size="xs" c="dimmed">
              You and collaborators can add more clips after creating the
              playlist
            </Text>
          </Stack>
        )}

        {/* Actions */}
        <Group justify="flex-end" mt="md">
          <Button
            variant="subtle"
            onClick={handleClose}
            disabled={createMutation.isPending}
            style={{
              color: "#94a3b8",
            }}
          >
            Cancel
          </Button>
          <Button
            leftSection={
              createMutation.isPending ? null : <IconCheck size={18} />
            }
            onClick={handleCreate}
            disabled={!isValid}
            loading={createMutation.isPending}
            variant="filled"
            style={{
              background: "linear-gradient(135deg, #00d4ff 0%, #0ea5e9 100%)",
              border: "1px solid rgba(0, 212, 255, 0.5)",
              boxShadow: "0 0 20px rgba(0, 212, 255, 0.3)",
              color: "#0a0a14",
              fontWeight: 600,
            }}
          >
            Create Playlist
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
