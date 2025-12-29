/**
 * Add to Playlist Button Component
 *
 * This button/menu appears on clip cards and allows users to:
 * 1. Add clip to existing playlist (quick select from recent)
 * 2. Create new playlist with this clip
 * 3. View all playlists
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Divider,
  Group,
  Menu,
  Stack,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  IconPlaylistAdd,
  IconPlus,
  IconVideo,
} from "@tabler/icons-react";
import { addClipsToPlaylist, fetchPlaylists } from "@repo/shared";
import { notifications } from "@mantine/notifications";
import { useDisclosure } from "@mantine/hooks";
import { CreatePlaylistModal } from "./CreatePlaylistModal";

type AddToPlaylistButtonProps = {
  clipId: string;
  /** Optional: Override click behavior instead of using default menu */
  onClick?: (e: React.MouseEvent) => void;
  /** Optional: Use compact styling for smaller displays */
  compact?: boolean;
};

export function AddToPlaylistButton({
  clipId,
  onClick,
  compact = false,
}: AddToPlaylistButtonProps) {
  const [opened, setOpened] = useState(false);
  const [createModalOpened, { open: openCreateModal, close: closeCreateModal }] = useDisclosure(false);
  const queryClient = useQueryClient();

  // Fetch all playlists
  const { data: playlists = [] } = useQuery({
    queryKey: ['playlists'],
    queryFn: fetchPlaylists,
    // Only fetch when menu is opened
    enabled: opened,
  });

  // Add clip to playlist mutation
  const addClipMutation = useMutation({
    mutationFn: (playlistId: string) =>
      addClipsToPlaylist(playlistId, { clipIds: [clipId] }),
    onSuccess: (_, playlistId) => {
      // Invalidate playlist queries to refresh data
      queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] });
      queryClient.invalidateQueries({ queryKey: ['playlists'] });

      notifications.show({
        title: 'Added to playlist',
        message: 'Clip added to playlist successfully',
        color: 'green',
      });
      setOpened(false);
    },
    onError: (error: any) => {
      // Check if it's a duplicate clip error
      if (error?.message?.includes('duplicate') || error?.status === 409) {
        notifications.show({
          title: 'Already in playlist',
          message: 'This clip is already in the selected playlist',
          color: 'yellow',
        });
      } else {
        notifications.show({
          title: 'Error',
          message: 'Failed to add clip to playlist',
          color: 'red',
        });
      }
    },
  });

  const handleAddToPlaylist = (playlistId: string, e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    addClipMutation.mutate(playlistId);
  };

  const handleCreateNewPlaylist = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setOpened(false);
    openCreateModal();
  };

  const handleViewAllPlaylists = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    window.location.href = '/playlists';
    setOpened(false);
  };

  // Sort playlists by most recently updated
  const sortedPlaylists = [...playlists].sort((a, b) => {
    return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
  });

  // Show max 5 recent playlists in quick menu
  const recentPlaylists = sortedPlaylists.slice(0, 5);

  const handleButtonClick = (e: React.MouseEvent) => {
    if (onClick) {
      onClick(e);
    } else {
      e.preventDefault();
      e.stopPropagation();
      setOpened(!opened);
    }
  };

  return (
    <>
      <Menu
        opened={opened}
        onChange={setOpened}
        position="bottom-end"
        withinPortal
        shadow="lg"
        width={320}
      >
        <Menu.Target>
          <Tooltip label="Add to playlist" position={compact ? "top" : "left"}>
            <ActionIcon
              variant="light"
              size={compact ? "sm" : "lg"}
              radius={compact ? "sm" : "md"}
              onClick={handleButtonClick}
              style={{
                transition: "all 0.2s ease",
                background: "rgba(168, 85, 247, 0.15)",
                border: "1px solid rgba(168, 85, 247, 0.3)",
                color: "#a855f7",
              }}
            >
              <IconPlaylistAdd size={compact ? 14 : 18} />
            </ActionIcon>
          </Tooltip>
        </Menu.Target>

        <Menu.Dropdown
          style={{
            background: "linear-gradient(135deg, rgba(15, 15, 25, 0.98) 0%, rgba(20, 20, 35, 0.95) 100%)",
            border: "1px solid rgba(168, 85, 247, 0.2)",
            boxShadow: "0 10px 40px rgba(0, 0, 0, 0.5), 0 0 30px rgba(168, 85, 247, 0.1)",
          }}
        >
          {/* Header */}
          <Menu.Label>
            <Text size="xs" fw={600} tt="uppercase" c="dimmed">
              Add to Playlist
            </Text>
          </Menu.Label>

          {/* Create New Playlist */}
          <Menu.Item
            leftSection={<IconPlus size={18} />}
            onClick={handleCreateNewPlaylist}
            style={{
              borderRadius: "6px",
            }}
          >
            <Text fw={600} size="sm">
              Create New Playlist
            </Text>
          </Menu.Item>

          <Divider my="xs" />

          {/* Recent/Favorite Playlists */}
          <Menu.Label>
            <Text size="xs" fw={600} tt="uppercase" c="dimmed">
              Recent Playlists
            </Text>
          </Menu.Label>

          {recentPlaylists.length === 0 ? (
            <Menu.Item disabled>
              <Stack gap={4} py="xs">
                <Text size="sm" c="dimmed">
                  No playlists yet
                </Text>
                <Text size="xs" c="dimmed">
                  Create your first playlist above
                </Text>
              </Stack>
            </Menu.Item>
          ) : (
            recentPlaylists.map((playlist) => {
              return (
                <Menu.Item
                  key={playlist.id}
                  onClick={(e) => handleAddToPlaylist(playlist.id, e)}
                  leftSection={<IconPlaylistAdd size={18} />}
                  style={{
                    borderRadius: "6px",
                  }}
                >
                  <Group justify="space-between" wrap="nowrap">
                    <Stack gap={2} style={{ flex: 1, minWidth: 0 }}>
                      <Text size="sm" fw={500} lineClamp={1}>
                        {playlist.name}
                      </Text>
                      <Group gap="xs">
                        <IconVideo size={12} style={{ opacity: 0.6 }} />
                        <Text size="xs" c="dimmed">
                          {playlist.clipCount} clips
                        </Text>
                      </Group>
                    </Stack>
                  </Group>
                </Menu.Item>
              );
            })
          )}

          {sortedPlaylists.length > 5 && (
            <>
              <Divider my="xs" />
              <Menu.Item
                leftSection={<IconVideo size={18} />}
                onClick={handleViewAllPlaylists}
                style={{
                  borderRadius: "6px",
                }}
              >
                <Text size="sm" c="dimmed">
                  View all playlists ({sortedPlaylists.length})
                </Text>
              </Menu.Item>
            </>
          )}
        </Menu.Dropdown>
      </Menu>

      {/* Create Playlist Modal */}
      <CreatePlaylistModal
        opened={createModalOpened}
        onClose={closeCreateModal}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ['playlists'] });
        }}
        initialClipIds={[clipId]}
      />
    </>
  );
}
