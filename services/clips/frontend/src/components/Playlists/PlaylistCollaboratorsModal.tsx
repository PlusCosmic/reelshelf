/**
 * Playlist Collaborators Modal
 *
 * Modal for managing playlist collaborators:
 * - View current collaborators with avatars
 * - Add new collaborators by username
 * - Remove collaborators (if you're the owner)
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Card,
  Group,
  Loader,
  Modal,
  Stack,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import {
  IconUserPlus,
  IconUsers,
  IconX,
} from "@tabler/icons-react";
import {
  addCollaborator,
  fetchPlaylistCollaborators,
  removeCollaborator,
} from "@repo/shared";
import { notifications } from "@mantine/notifications";
import { modals } from "@mantine/modals";
import type { PlaylistCollaborator } from "@repo/nucleus-api-client";
import { useCurrentUser } from "@/hooks/queries.ts";

type PlaylistCollaboratorsModalProps = {
  opened: boolean;
  onClose: () => void;
  playlistId: string;
  playlistName: string;
  /** The user ID of the playlist creator */
  creatorUserId: string;
};

export function PlaylistCollaboratorsModal({
  opened,
  onClose,
  playlistId,
  playlistName,
  creatorUserId,
}: PlaylistCollaboratorsModalProps) {
  const queryClient = useQueryClient();
  const [usernameInput, setUsernameInput] = useState("");

  // Get current user to check permissions
  const { data: currentUser } = useCurrentUser();
  const isOwner = currentUser?.id === creatorUserId;

  // Fetch collaborators
  const {
    data: collaborators,
    isLoading,
  } = useQuery({
    queryKey: ["playlist", playlistId, "collaborators"],
    queryFn: () => fetchPlaylistCollaborators(playlistId),
    enabled: opened,
    staleTime: 30_000,
  });

  // Add collaborator mutation
  const addMutation = useMutation({
    mutationFn: (username: string) =>
      addCollaborator(playlistId, { username }),
    onSuccess: () => {
      notifications.show({
        title: "Collaborator added",
        message: `${usernameInput} can now add clips to this playlist`,
        color: "green",
      });
      setUsernameInput("");
      queryClient.invalidateQueries({
        queryKey: ["playlist", playlistId, "collaborators"],
      });
      queryClient.invalidateQueries({
        queryKey: ["playlist", playlistId],
      });
      queryClient.invalidateQueries({ queryKey: ["playlists"] });
    },
    onError: (error: unknown) => {
      const apiError = error as { response?: { status?: number } };
      const message =
        apiError.response?.status === 404
          ? "User not found. Check the username and try again."
          : apiError.response?.status === 409
            ? "This user is already a collaborator"
            : "Failed to add collaborator";
      notifications.show({
        title: "Error",
        message,
        color: "red",
      });
    },
  });

  // Remove collaborator mutation
  const removeMutation = useMutation({
    mutationFn: (userId: string) => removeCollaborator(playlistId, userId),
    onSuccess: (_data, userId) => {
      const removed = collaborators?.find(
        (c: PlaylistCollaborator) => c.userId === userId
      );
      notifications.show({
        title: "Collaborator removed",
        message: removed
          ? `${removed.username} has been removed`
          : "Collaborator removed",
        color: "green",
      });
      queryClient.invalidateQueries({
        queryKey: ["playlist", playlistId, "collaborators"],
      });
      queryClient.invalidateQueries({
        queryKey: ["playlist", playlistId],
      });
      queryClient.invalidateQueries({ queryKey: ["playlists"] });
    },
    onError: () => {
      notifications.show({
        title: "Error",
        message: "Failed to remove collaborator",
        color: "red",
      });
    },
  });

  const handleAddCollaborator = () => {
    const username = usernameInput.trim();
    if (!username) return;
    addMutation.mutate(username);
  };

  const handleRemoveCollaborator = (userId: string) => {
    modals.openConfirmModal({
      title: 'Remove Collaborator',
      children: <Text size="sm">Remove this collaborator from the playlist?</Text>,
      labels: { confirm: 'Remove', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => { removeMutation.mutate(userId); },
    });
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && usernameInput.trim()) {
      handleAddCollaborator();
    }
  };

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title={
        <Group gap="xs">
          <IconUsers size={24} />
          <Text fw={600} size="lg">
            Collaborators
          </Text>
        </Group>
      }
      size="md"
      styles={{
        content: {
          background: "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
          border: "1px solid rgba(168, 85, 247, 0.2)",
          boxShadow: "0 20px 60px rgba(0, 0, 0, 0.5), 0 0 40px rgba(168, 85, 247, 0.1)",
        },
        header: {
          background: "transparent",
          borderBottom: "1px solid rgba(168, 85, 247, 0.15)",
        },
        title: {
          color: "#a855f7",
        },
        body: {
          padding: "24px",
        },
      }}
    >
      <Stack gap="lg">
        {/* Playlist Name */}
        <Text size="sm" c="dimmed">
          Managing collaborators for <Text span fw={600} c="white">"{playlistName}"</Text>
        </Text>

        {/* Add Collaborator Input */}
        <Stack gap="xs">
          <Text size="sm" fw={500}>
            Add Collaborator
          </Text>
          <Group gap="xs">
            <TextInput
              placeholder="Enter Discord username..."
              value={usernameInput}
              onChange={(e) => setUsernameInput(e.target.value)}
              onKeyDown={handleKeyDown}
              disabled={addMutation.isPending}
              style={{ flex: 1 }}
              styles={{
                input: {
                  borderRadius: "8px",
                  backgroundColor: "rgba(168, 85, 247, 0.03)",
                  border: "1px solid rgba(168, 85, 247, 0.15)",
                  "&:focus": {
                    borderColor: "rgba(168, 85, 247, 0.5)",
                    boxShadow: "0 0 15px rgba(168, 85, 247, 0.15)",
                  },
                },
              }}
            />
            <Button
              leftSection={
                addMutation.isPending ? null : <IconUserPlus size={18} />
              }
              onClick={handleAddCollaborator}
              disabled={!usernameInput.trim()}
              loading={addMutation.isPending}
              variant="filled"
              style={{
                background: "linear-gradient(135deg, #a855f7 0%, #7c3aed 100%)",
                border: "1px solid rgba(168, 85, 247, 0.5)",
                boxShadow: "0 0 20px rgba(168, 85, 247, 0.3)",
                color: "#fff",
                fontWeight: 600,
              }}
            >
              Add
            </Button>
          </Group>
          <Text size="xs" c="dimmed">
            Collaborators can add clips to this playlist. They'll receive a Discord DM notification if enabled.
          </Text>
        </Stack>

        {/* Current Collaborators List */}
        <Stack gap="xs">
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              Current Collaborators
            </Text>
            {collaborators && collaborators.length > 0 && (
              <Badge
                size="sm"
                variant="light"
                style={{
                  background: "linear-gradient(135deg, rgba(168, 85, 247, 0.15) 0%, rgba(236, 72, 153, 0.15) 100%)",
                  border: "1px solid rgba(168, 85, 247, 0.3)",
                  color: "#a855f7",
                }}
              >
                {collaborators.length}
              </Badge>
            )}
          </Group>

          {isLoading ? (
            <Card
              p="lg"
              radius="md"
              style={{
                background: "linear-gradient(135deg, rgba(15, 15, 25, 0.6) 0%, rgba(20, 20, 35, 0.5) 100%)",
                border: "1px solid rgba(168, 85, 247, 0.1)",
              }}
            >
              <Group justify="center">
                <Loader size="sm" color="violet" />
              </Group>
            </Card>
          ) : !collaborators || collaborators.length === 0 ? (
            <Card
              p="lg"
              radius="md"
              style={{
                background: "linear-gradient(135deg, rgba(15, 15, 25, 0.6) 0%, rgba(20, 20, 35, 0.5) 100%)",
                border: "1px solid rgba(168, 85, 247, 0.1)",
              }}
            >
              <Text size="sm" c="dimmed" ta="center">
                No collaborators yet. Add someone to share this playlist!
              </Text>
            </Card>
          ) : (
            <Stack gap="xs">
              {collaborators.map((collaborator: PlaylistCollaborator) => (
                <CollaboratorCard
                  key={collaborator.userId}
                  collaborator={collaborator}
                  isOwner={isOwner}
                  isRemoving={removeMutation.isPending}
                  onRemove={() => handleRemoveCollaborator(collaborator.userId)}
                />
              ))}
            </Stack>
          )}
        </Stack>

        {/* Close Button */}
        <Group justify="flex-end" mt="md">
          <Button
            variant="subtle"
            onClick={onClose}
            style={{
              color: "#a855f7",
              transition: "all 0.2s ease",
            }}
          >
            Done
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/**
 * Individual collaborator card with avatar and remove action
 */
function CollaboratorCard({
  collaborator,
  isOwner,
  isRemoving,
  onRemove,
}: {
  collaborator: PlaylistCollaborator;
  isOwner: boolean;
  isRemoving: boolean;
  onRemove: () => void;
}) {
  const addedDate = new Date(collaborator.addedAt);
  const formattedDate = addedDate.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });

  return (
    <Card
      p="sm"
      radius="md"
      style={{
        background: "linear-gradient(135deg, rgba(15, 15, 25, 0.6) 0%, rgba(20, 20, 35, 0.5) 100%)",
        border: "1px solid rgba(168, 85, 247, 0.1)",
        transition: "all 0.2s ease",
      }}
    >
      <Group justify="space-between" wrap="nowrap">
        <Group gap="sm" wrap="nowrap">
          <Avatar
            src={collaborator.avatarUrl}
            alt={collaborator.username}
            size="md"
            radius="xl"
            style={{
              border: "2px solid rgba(168, 85, 247, 0.4)",
              boxShadow: "0 0 15px rgba(168, 85, 247, 0.2)",
            }}
          >
            {collaborator.username.slice(0, 2).toUpperCase()}
          </Avatar>
          <Stack gap={2}>
            <Text size="sm" fw={500}>
              {collaborator.username}
            </Text>
            <Text size="xs" c="dimmed">
              Added {formattedDate}
            </Text>
          </Stack>
        </Group>

        {isOwner && (
          <Tooltip label="Remove collaborator">
            <ActionIcon
              variant="subtle"
              size="sm"
              onClick={onRemove}
              disabled={isRemoving}
              loading={isRemoving}
              style={{
                color: "#ec4899",
                transition: "all 0.2s ease",
              }}
            >
              <IconX size={14} />
            </ActionIcon>
          </Tooltip>
        )}
      </Group>
    </Card>
  );
}
