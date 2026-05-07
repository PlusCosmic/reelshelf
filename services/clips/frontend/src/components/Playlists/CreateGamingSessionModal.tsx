/**
 * Create Gaming Session Modal Component
 *
 * Modal for creating a gaming session playlist that compiles
 * the last 24 hours of clips from selected participants for a chosen game.
 */

import { useMemo, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import {
  Avatar,
  Badge,
  Button,
  Group,
  Modal,
  MultiSelect,
  Select,
  Stack,
  Text,
} from "@mantine/core";
import { IconCheck, IconDeviceGamepad2, IconUsers } from "@tabler/icons-react";
import {
  createGamingSessionPlaylist,
  fetchCategories,
  fetchUserSuggestions,
} from "@repo/shared";
import { notifications } from "@mantine/notifications";
import type {
  GameCategoryResponse,
  DiscordUser,
} from "@repo/clips-api-client";

type CreateGamingSessionModalProps = {
  opened: boolean;
  onClose: () => void;
  onSuccess?: () => void;
};

export function CreateGamingSessionModal({
  opened,
  onClose,
  onSuccess,
}: CreateGamingSessionModalProps) {
  const [selectedCategory, setSelectedCategory] = useState<string | null>(null);
  const [selectedParticipants, setSelectedParticipants] = useState<
    Array<string>
  >([]);

  // Fetch categories (games)
  const { data: categories = [], isLoading: categoriesLoading } = useQuery({
    queryKey: ["categories"],
    queryFn: fetchCategories,
    staleTime: 5 * 60 * 1000,
  });

  // Fetch user suggestions (Discord friends)
  const { data: userSuggestions = [], isLoading: suggestionsLoading } =
    useQuery({
      queryKey: ["userSuggestions"],
      queryFn: fetchUserSuggestions,
      enabled: opened,
      staleTime: 60 * 1000,
    });

  // Transform categories into Select options
  const categoryOptions = useMemo(() => {
    if (!categories) return [];
    return categories.map((cat: GameCategoryResponse) => ({
      value: cat.id,
      label: cat.name,
    }));
  }, [categories]);

  // Transform user suggestions into MultiSelect options
  const participantOptions = useMemo(() => {
    return userSuggestions.map((user: DiscordUser) => ({
      value: user.id,
      label: user.globalName || user.username,
      avatar: user.avatar,
      username: user.username,
    }));
  }, [userSuggestions]);

  const createMutation = useMutation({
    mutationFn: async () => {
      if (!selectedCategory) throw new Error("No category selected");

      return createGamingSessionPlaylist({
        categoryId: selectedCategory,
        participants: selectedParticipants,
      });
    },
    onSuccess: (playlist) => {
      const clipCount = playlist?.clips.length ?? 0;
      notifications.show({
        title: "Gaming session created",
        message:
          clipCount > 0
            ? `Found ${clipCount} clips from the last 24 hours`
            : "Session created - no clips found from the last 24 hours",
        color: "green",
      });
      handleClose();
      onSuccess?.();
    },
    onError: () => {
      notifications.show({
        title: "Error",
        message: "Failed to create gaming session",
        color: "red",
      });
    },
  });

  const handleCreate = () => {
    if (!selectedCategory) return;
    createMutation.mutate();
  };

  const handleClose = () => {
    setSelectedCategory(null);
    setSelectedParticipants([]);
    onClose();
  };

  const isValid = selectedCategory !== null;

  return (
    <Modal
      opened={opened}
      onClose={handleClose}
      title={
        <Group gap="xs">
          <IconDeviceGamepad2 size={24} />
          <Text fw={600} size="lg">
            Create Gaming Session
          </Text>
        </Group>
      }
      size="lg"
      styles={{
        content: {
          background:
            "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
          border: "1px solid rgba(34, 197, 94, 0.2)",
          boxShadow:
            "0 20px 60px rgba(0, 0, 0, 0.5), 0 0 40px rgba(34, 197, 94, 0.1)",
        },
        header: {
          background: "transparent",
          borderBottom: "1px solid rgba(34, 197, 94, 0.15)",
        },
        title: {
          color: "#22c55e",
        },
        body: {
          padding: "24px",
        },
      }}
    >
      <Stack gap="lg">
        {/* Description */}
        <Text size="sm" c="dimmed">
          Compile clips from the last 24 hours into a collaborative playlist.
          Select a game and optionally add friends who were in the session.
        </Text>

        {/* Game Selection */}
        <Stack gap="xs">
          <Group gap="xs">
            <IconDeviceGamepad2 size={18} style={{ color: "#22c55e" }} />
            <Text fw={600} size="sm">
              Game
            </Text>
            <Badge
              size="sm"
              variant="light"
              style={{
                background: "rgba(239, 68, 68, 0.15)",
                border: "1px solid rgba(239, 68, 68, 0.3)",
                color: "#ef4444",
              }}
            >
              Required
            </Badge>
          </Group>
          <Select
            placeholder="Select a game..."
            data={categoryOptions}
            value={selectedCategory}
            onChange={setSelectedCategory}
            searchable
            clearable
            disabled={categoriesLoading}
            nothingFoundMessage="No games found"
            styles={{
              input: {
                borderRadius: "8px",
                backgroundColor: "rgba(34, 197, 94, 0.03)",
                border: "1px solid rgba(34, 197, 94, 0.15)",
                "&:focus": {
                  borderColor: "rgba(34, 197, 94, 0.5)",
                  boxShadow: "0 0 15px rgba(34, 197, 94, 0.15)",
                },
              },
              dropdown: {
                background: "rgba(15, 15, 25, 0.98)",
                border: "1px solid rgba(34, 197, 94, 0.2)",
              },
              option: {
                "&[data-selected]": {
                  backgroundColor: "rgba(34, 197, 94, 0.2)",
                },
                "&[data-hovered]": {
                  backgroundColor: "rgba(34, 197, 94, 0.1)",
                },
              },
            }}
          />
        </Stack>

        {/* Participants Selection */}
        <Stack gap="xs">
          <Group gap="xs">
            <IconUsers size={18} style={{ color: "#a855f7" }} />
            <Text fw={600} size="sm">
              Session Participants
            </Text>
            <Badge
              size="sm"
              variant="light"
              style={{
                background: "rgba(59, 130, 246, 0.15)",
                border: "1px solid rgba(59, 130, 246, 0.3)",
                color: "#3b82f6",
              }}
            >
              Optional
            </Badge>
          </Group>
          <MultiSelect
            placeholder={
              suggestionsLoading
                ? "Loading friends..."
                : "Select friends from the session..."
            }
            data={participantOptions}
            value={selectedParticipants}
            onChange={setSelectedParticipants}
            searchable
            clearable
            disabled={suggestionsLoading}
            nothingFoundMessage="No friends found"
            maxDropdownHeight={200}
            renderOption={({ option }) => {
              const participant = participantOptions.find(
                (p) => p.value === option.value,
              );
              return (
                <Group gap="sm" wrap="nowrap">
                  <Avatar
                    src={
                      participant?.avatar
                        ? `https://cdn.discordapp.com/avatars/${participant.value}/${participant.avatar}.png`
                        : undefined
                    }
                    size="sm"
                    radius="xl"
                  >
                    {option.label.slice(0, 2).toUpperCase()}
                  </Avatar>
                  <Stack gap={0}>
                    <Text size="sm">{option.label}</Text>
                    {participant?.username !== option.label && (
                      <Text size="xs" c="dimmed">
                        @{participant?.username}
                      </Text>
                    )}
                  </Stack>
                </Group>
              );
            }}
            styles={{
              input: {
                borderRadius: "8px",
                backgroundColor: "rgba(168, 85, 247, 0.03)",
                border: "1px solid rgba(168, 85, 247, 0.15)",
                "&:focus-within": {
                  borderColor: "rgba(168, 85, 247, 0.5)",
                  boxShadow: "0 0 15px rgba(168, 85, 247, 0.15)",
                },
              },
              dropdown: {
                background: "rgba(15, 15, 25, 0.98)",
                border: "1px solid rgba(168, 85, 247, 0.2)",
              },
              option: {
                "&[data-selected]": {
                  backgroundColor: "rgba(168, 85, 247, 0.2)",
                },
                "&[data-hovered]": {
                  backgroundColor: "rgba(168, 85, 247, 0.1)",
                },
              },
              pill: {
                background:
                  "linear-gradient(135deg, rgba(168, 85, 247, 0.2) 0%, rgba(236, 72, 153, 0.2) 100%)",
                border: "1px solid rgba(168, 85, 247, 0.3)",
              },
            }}
          />
          <Text size="xs" c="dimmed">
            Adding participants will include their clips and add them as
            collaborators. Leave empty to only include your own clips.
          </Text>
        </Stack>

        {/* Summary */}
        {selectedCategory && (
          <Stack
            gap="xs"
            p="md"
            style={{
              background:
                "linear-gradient(135deg, rgba(34, 197, 94, 0.05) 0%, rgba(168, 85, 247, 0.05) 100%)",
              borderRadius: "8px",
              border: "1px solid rgba(34, 197, 94, 0.15)",
            }}
          >
            <Text size="sm" fw={500}>
              Session Summary
            </Text>
            <Text size="xs" c="dimmed">
              This will create a playlist with clips from the last 24 hours
              {selectedParticipants.length > 0 && (
                <>
                  {" "}
                  from you and {selectedParticipants.length} friend
                  {selectedParticipants.length > 1 ? "s" : ""}
                </>
              )}
              {selectedParticipants.length === 0 && <> (only your clips)</>} in{" "}
              <Text span fw={600} c="white">
                {
                  categoryOptions.find((c) => c.value === selectedCategory)
                    ?.label
                }
              </Text>
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
              background: "linear-gradient(135deg, #22c55e 0%, #16a34a 100%)",
              border: "1px solid rgba(34, 197, 94, 0.5)",
              boxShadow: "0 0 20px rgba(34, 197, 94, 0.3)",
              color: "#0a0a14",
              fontWeight: 600,
            }}
          >
            Create Session
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
