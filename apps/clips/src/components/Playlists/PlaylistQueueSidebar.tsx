import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Card,
  Center,
  Divider,
  Group,
  Loader,
  Progress,
  ScrollArea,
  Stack,
  Text,
  Title,
  Tooltip,
} from "@mantine/core";
import { IconArrowLeft, IconPlayerPlay, IconUsers } from "@tabler/icons-react";
import { useDisclosure } from "@mantine/hooks";
import { useNavigate } from "@tanstack/react-router";
import { fetchPlaylistById, removeClipFromPlaylist } from "@repo/shared";
import { notifications } from "@mantine/notifications";
import { PlaylistCollaboratorsModal } from "./PlaylistCollaboratorsModal";
import { PlaylistQueueCard } from "./PlaylistQueueCard";
import { useClip } from "@/hooks/queries.ts";

interface PlaylistQueueSidebarProps {
  playlistId: string;
  currentClipIndex: number;
  onClipSelect: (clipId: string, index: number) => void;
}

export function PlaylistQueueSidebar({
  playlistId,
  currentClipIndex,
  onClipSelect,
}: PlaylistQueueSidebarProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [hoveredClipIndex, setHoveredClipIndex] = useState<number | null>(null);
  const [collaboratorsOpened, { open: openCollaborators, close: closeCollaborators }] = useDisclosure(false);

  // Fetch playlist details
  const { data: playlist, isLoading: loadingPlaylist } = useQuery({
    queryKey: ["playlist", playlistId],
    queryFn: () => fetchPlaylistById(playlistId),
  });

  // Remove clip mutation
  const removeClipMutation = useMutation({
    mutationFn: ({ playlistId, clipId }: { playlistId: string; clipId: string }) =>
      removeClipFromPlaylist(playlistId, clipId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["playlist", playlistId] });
      notifications.show({
        title: "Clip removed",
        message: "Clip removed from playlist",
        color: "green",
      });
    },
    onError: () => {
      notifications.show({
        title: "Error",
        message: "Failed to remove clip",
        color: "red",
      });
    },
  });

  const handleRemoveClip = (clipId: string, e: React.MouseEvent) => {
    e.stopPropagation();

    if (!confirm("Remove this clip from the playlist?")) {
      return;
    }

    removeClipMutation.mutate({ playlistId, clipId });
  };

  const progressPercentage = playlist?.clips.length
    ? ((currentClipIndex + 1) / playlist.clips.length) * 100
    : 0;

  return (
    <Card
      radius="xl"
      p="md"
      style={{
        width: "380px",
        background:
          "linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)",
        backdropFilter: "blur(10px)",
        border: "1px solid rgba(255, 255, 255, 0.1)",
        flexShrink: 0,
      }}
    >
      <Stack gap="md" h="100%">
        {/* Header */}
        <Group justify="center" align="center" style={{ position: "relative" }}>
          <ActionIcon
            variant="subtle"
            size="lg"
            onClick={() => navigate({ to: "/playlists" })}
            aria-label="Back to playlists"
            style={{ position: "absolute", left: 0 }}
          >
            <IconArrowLeft size={20} />
          </ActionIcon>
          <Text size="lg" fw={600} style={{ letterSpacing: "-0.3px" }}>
            Queue
          </Text>
        </Group>

        {/* Loading State */}
        {loadingPlaylist && (
          <Center style={{ flex: 1 }}>
            <Loader size="md" />
          </Center>
        )}

        {/* Playlist Info */}
        {!loadingPlaylist && playlist && (
          <>
            <Stack gap="xs">
              <Group justify="space-between" align="flex-start" wrap="nowrap">
                <Title order={4} lineClamp={1} style={{ flex: 1 }}>
                  {playlist.name}
                </Title>
                <Tooltip label="Manage collaborators">
                  <ActionIcon
                    variant="subtle"
                    size="sm"
                    onClick={openCollaborators}
                    aria-label="Manage collaborators"
                  >
                    <IconUsers size={16} />
                  </ActionIcon>
                </Tooltip>
              </Group>
              {playlist.description && (
                <Text c="dimmed" size="sm" lineClamp={2}>
                  {playlist.description}
                </Text>
              )}
            </Stack>

            {/* Progress Indicator */}
            {playlist.clips.length > 0 && (
              <Stack gap="xs">
                <Group justify="space-between">
                  <Text size="xs" c="dimmed">
                    {currentClipIndex + 1} / {playlist.clips.length}
                  </Text>
                  <Badge variant="light" color="green" size="sm">
                    {playlist.clips.length} clips
                  </Badge>
                </Group>
                <Progress
                  value={progressPercentage}
                  size="sm"
                  radius="xl"
                  color="green"
                  style={{
                    background: "rgba(255, 255, 255, 0.05)",
                  }}
                />
              </Stack>
            )}

            <Divider />

            {/* Scrollable Clip List */}
            <ScrollArea
              h="100%"
              type="scroll"
              scrollbarSize={8}
              styles={{
                scrollbar: {
                  "&:hover": {
                    backgroundColor: "rgba(255, 255, 255, 0.05)",
                  },
                },
                thumb: {
                  backgroundColor: "rgba(255, 255, 255, 0.2)",
                  "&:hover": {
                    backgroundColor: "rgba(255, 255, 255, 0.3)",
                  },
                },
              }}
            >
              <Stack gap="xs">
                {playlist.clips.length === 0 && (
                  <Center py="xl">
                    <Stack align="center" gap="md">
                      <IconPlayerPlay size={48} stroke={1} style={{ opacity: 0.3 }} />
                      <Text size="sm" c="dimmed" ta="center">
                        No clips in this playlist yet
                      </Text>
                    </Stack>
                  </Center>
                )}
                {playlist.clips.map((playlistClip: any, index: number) => (
                  <PlaylistQueueCardWrapper
                    key={playlistClip.id || playlistClip.clipId}
                    clipId={playlistClip.clipId}
                    index={index}
                    isCurrentClip={index === currentClipIndex}
                    isHovered={hoveredClipIndex === index}
                    onHover={() => setHoveredClipIndex(index)}
                    onLeave={() => setHoveredClipIndex(null)}
                    onJumpTo={() => onClipSelect(playlistClip.clipId, index)}
                    onRemove={(e) => handleRemoveClip(playlistClip.clipId, e)}
                  />
                ))}
              </Stack>
            </ScrollArea>
          </>
        )}

        {/* Not Found State */}
        {!loadingPlaylist && !playlist && (
          <Center style={{ flex: 1 }}>
            <Stack align="center" gap="md">
              <Text size="lg" fw={600}>
                Playlist not found
              </Text>
              <ActionIcon
                variant="light"
                size="lg"
                onClick={() => navigate({ to: "/playlists" })}
              >
                <IconArrowLeft size={20} />
              </ActionIcon>
            </Stack>
          </Center>
        )}
      </Stack>

      {/* Collaborators Modal */}
      {playlist && (
        <PlaylistCollaboratorsModal
          opened={collaboratorsOpened}
          onClose={closeCollaborators}
          playlistId={playlistId}
          playlistName={playlist.name}
          creatorUserId={playlist.creatorUserId}
        />
      )}
    </Card>
  );
}

/**
 * Wrapper component that fetches full clip data for queue cards
 */
function PlaylistQueueCardWrapper({
  clipId,
  index,
  isCurrentClip,
  isHovered,
  onHover,
  onLeave,
  onJumpTo,
  onRemove,
}: {
  clipId: string;
  index: number;
  isCurrentClip: boolean;
  isHovered: boolean;
  onHover: () => void;
  onLeave: () => void;
  onJumpTo: () => void;
  onRemove: (e: React.MouseEvent) => void;
}) {
  const { data: clip, isLoading } = useClip(clipId);

  if (isLoading || !clip) {
    return (
      <Card
        p="sm"
        radius="md"
        style={{
          background: "rgba(255, 255, 255, 0.02)",
          border: "1px solid rgba(255, 255, 255, 0.05)",
        }}
      >
        <Group wrap="nowrap" gap="sm">
          <div style={{ width: 24 }} />
          <div style={{ width: 24 }}>
            <Text size="sm" c="dimmed" fw={600}>
              {index + 1}
            </Text>
          </div>
          <div
            style={{
              width: 80,
              aspectRatio: "16/9",
              background: "rgba(255, 255, 255, 0.05)",
              borderRadius: "4px",
            }}
          />
          <Stack gap={4} style={{ flex: 1 }}>
            <div
              style={{
                height: 14,
                width: "80%",
                background: "rgba(255, 255, 255, 0.1)",
                borderRadius: "4px",
              }}
            />
            <div
              style={{
                height: 10,
                width: "50%",
                background: "rgba(255, 255, 255, 0.05)",
                borderRadius: "4px",
              }}
            />
          </Stack>
        </Group>
      </Card>
    );
  }

  return (
    <PlaylistQueueCard
      clip={clip}
      index={index}
      isCurrentClip={isCurrentClip}
      isHovered={isHovered}
      onHover={onHover}
      onLeave={onLeave}
      onJumpTo={onJumpTo}
      onRemove={onRemove}
    />
  );
}
