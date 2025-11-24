/**
 * Playlist Player Page
 * Route: /playlists/:playlistId
 *
 * Main viewing experience - plays clips sequentially with queue sidebar
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Avatar,
  Badge,
  Box,
  Button,
  Card,
  Center,
  Container,
  Divider,
  Group,
  Loader,
  Progress,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  IconCheck,
  IconGripVertical,
  IconPlayerPause,
  IconPlayerPlay,
  IconPlayerSkipBack,
  IconPlayerSkipForward,
  IconShare,
  IconTrash,
} from "@tabler/icons-react";
import { fetchPlaylistById, removeClipFromPlaylist } from "@repo/shared";
import { notifications } from "@mantine/notifications";

type PlaylistPlayerPageProps = {
  playlistId: string;
};

export function PlaylistPlayerPage({ playlistId }: PlaylistPlayerPageProps) {
  const [currentClipIndex, setCurrentClipIndex] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [hoveredClipIndex, setHoveredClipIndex] = useState<number | null>(null);
  const queryClient = useQueryClient();

  // Fetch playlist details
  const { data: playlist, isLoading } = useQuery({
    queryKey: ['playlist', playlistId],
    queryFn: () => fetchPlaylistById(playlistId),
  });

  // Remove clip mutation
  const removeClipMutation = useMutation({
    mutationFn: ({ playlistId, clipId }: { playlistId: string; clipId: string }) =>
      removeClipFromPlaylist(playlistId, clipId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['playlist', playlistId] });
      notifications.show({
        title: 'Clip removed',
        message: 'Clip removed from playlist',
        color: 'green',
      });
    },
    onError: () => {
      notifications.show({
        title: 'Error',
        message: 'Failed to remove clip',
        color: 'red',
      });
    },
  });

  const currentClip = playlist?.clips[currentClipIndex];
  const hasNext = playlist && currentClipIndex < playlist.clips.length - 1;
  const hasPrevious = currentClipIndex > 0;

  const handleNext = () => {
    if (hasNext) {
      setCurrentClipIndex(currentClipIndex + 1);
      setIsPlaying(true);
    }
  };

  const handlePrevious = () => {
    if (hasPrevious) {
      setCurrentClipIndex(currentClipIndex - 1);
      setIsPlaying(true);
    }
  };

  const handleJumpToClip = (index: number) => {
    setCurrentClipIndex(index);
    setIsPlaying(true);
  };

  const handleRemoveClip = (clipId: string, e: React.MouseEvent) => {
    e.stopPropagation();

    if (!confirm('Remove this clip from the playlist?')) {
      return;
    }

    removeClipMutation.mutate({ playlistId, clipId });

    // Adjust current index if needed
    if (playlist && currentClipIndex >= playlist.clips.length - 1) {
      setCurrentClipIndex(Math.max(0, currentClipIndex - 1));
    }
  };

  const handleSharePlaylist = () => {
    const url = `${window.location.origin}/playlists/${playlistId}`;
    navigator.clipboard.writeText(url);

    notifications.show({
      title: 'Link copied',
      message: 'Playlist link copied to clipboard',
      color: 'blue',
    });
  };

  const progressPercentage = playlist ? ((currentClipIndex + 1) / playlist.clips.length) * 100 : 0;

  if (isLoading) {
    return (
      <Container size="xl" py="xl">
        <Center py="xl">
          <Loader size="lg" />
        </Center>
      </Container>
    );
  }

  if (!playlist) {
    return (
      <Container size="xl" py="xl">
        <Center py="xl">
          <Stack align="center" gap="md">
            <Text size="lg" fw={600}>
              Playlist not found
            </Text>
            <Button component="a" href="/playlists">
              Back to Playlists
            </Button>
          </Stack>
        </Center>
      </Container>
    );
  }

  return (
    <Container size="xl" py="xl">
      {/* Playlist Header */}
      <Group justify="space-between" mb="lg">
        <Stack gap="xs">
          <Title order={2}>{playlist.name}</Title>
          {playlist.description && (
            <Text c="dimmed" size="sm">
              {playlist.description}
            </Text>
          )}
        </Stack>
        <Group>
          <Avatar.Group spacing="sm">
            {/* TODO: Show actual collaborator avatars when we have user data */}
            <Badge variant="light" color="blue">
              {playlist.collaborators.length} collaborators
            </Badge>
          </Avatar.Group>
          <Tooltip label="Share playlist">
            <ActionIcon
              variant="light"
              color="blue"
              size="lg"
              radius="md"
              onClick={handleSharePlaylist}
            >
              <IconShare size={18} />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      {/* Progress Indicator */}
      {playlist.clips.length > 0 && (
        <Stack gap="xs" mb="xl">
          <Group justify="space-between">
            <Text size="sm" c="dimmed">
              {currentClipIndex + 1} / {playlist.clips.length} clips
            </Text>
            <Text size="sm" c="dimmed">
              {progressPercentage.toFixed(0)}% complete
            </Text>
          </Group>
          <Progress
            value={progressPercentage}
            size="md"
            radius="xl"
            color="blue"
            style={{
              background: "rgba(255, 255, 255, 0.05)",
            }}
          />
        </Stack>
      )}

      {/* Empty Playlist State */}
      {playlist.clips.length === 0 && (
        <Card
          p="xl"
          radius="lg"
          style={{
            border: "2px dashed rgba(255, 255, 255, 0.1)",
            background: "rgba(255, 255, 255, 0.02)",
          }}
        >
          <Stack align="center" gap="md" py="xl">
            <IconPlayerPlay size={64} stroke={1} style={{ opacity: 0.3 }} />
            <Stack gap="xs" align="center">
              <Text size="lg" fw={600}>
                No clips in this playlist
              </Text>
              <Text size="sm" c="dimmed" ta="center" maw={400}>
                Add some clips to get started
              </Text>
            </Stack>
          </Stack>
        </Card>
      )}

      {/* Main Layout: Video Player + Queue */}
      {playlist.clips.length > 0 && currentClip && (
        <Group align="flex-start" gap="lg" wrap="nowrap">
          {/* Video Player Area */}
          <Stack style={{ flex: 1 }} gap="md">
            {/* Video Player Placeholder - TODO: Integrate real video player */}
            <Box
              style={{
                aspectRatio: "16/9",
                background: "linear-gradient(135deg, #1a1b1e 0%, #25262b 100%)",
                borderRadius: "12px",
                position: "relative",
                overflow: "hidden",
                border: "1px solid rgba(255, 255, 255, 0.1)",
              }}
            >
              <Box
                style={{
                  position: "absolute",
                  inset: 0,
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                <Stack align="center" gap="md">
                  <IconPlayerPlay size={64} stroke={1} style={{ opacity: 0.3 }} />
                  <Text c="dimmed" size="sm">
                    Video player will go here
                  </Text>
                  <Text c="dimmed" size="xs">
                    Clip ID: {currentClip.clipId}
                  </Text>
                </Stack>
              </Box>

              {/* Current Clip Title Overlay */}
              <Box
                style={{
                  position: "absolute",
                  top: 0,
                  left: 0,
                  right: 0,
                  padding: "16px",
                  background: "linear-gradient(180deg, rgba(0,0,0,0.7) 0%, transparent 100%)",
                }}
              >
                <Text fw={600} c="white" size="lg">
                  Clip {currentClipIndex + 1} of {playlist.clips.length}
                </Text>
                <Text size="sm" c="rgba(255,255,255,0.6)">
                  Position: {currentClip.position}
                </Text>
              </Box>
            </Box>

            {/* Playback Controls */}
            <Card radius="lg" p="md" style={{ background: "rgba(255, 255, 255, 0.02)" }}>
              <Group justify="center" gap="lg">
                <Tooltip label="Previous clip">
                  <ActionIcon
                    variant="light"
                    size="xl"
                    radius="xl"
                    disabled={!hasPrevious}
                    onClick={handlePrevious}
                  >
                    <IconPlayerSkipBack size={24} />
                  </ActionIcon>
                </Tooltip>

                <ActionIcon
                  variant="gradient"
                  gradient={{ from: "blue", to: "cyan", deg: 45 }}
                  size={64}
                  radius="xl"
                  onClick={() => setIsPlaying(!isPlaying)}
                >
                  {isPlaying ? (
                    <IconPlayerPause size={32} />
                  ) : (
                    <IconPlayerPlay size={32} />
                  )}
                </ActionIcon>

                <Tooltip label="Next clip">
                  <ActionIcon
                    variant="light"
                    size="xl"
                    radius="xl"
                    disabled={!hasNext}
                    onClick={handleNext}
                  >
                    <IconPlayerSkipForward size={24} />
                  </ActionIcon>
                </Tooltip>
              </Group>
            </Card>
          </Stack>

          {/* Queue Sidebar */}
          <Card
            radius="lg"
            p="md"
            w={380}
            style={{
              background: "rgba(255, 255, 255, 0.02)",
              border: "1px solid rgba(255, 255, 255, 0.05)",
              maxHeight: "800px",
              display: "flex",
              flexDirection: "column",
            }}
          >
            <Stack gap="md" style={{ flex: 1, overflow: "hidden" }}>
              <Group justify="space-between">
                <Text fw={600} size="lg">
                  Queue
                </Text>
                <Badge variant="light" color="blue">
                  {playlist.clips.length} clips
                </Badge>
              </Group>

              <Divider />

              {/* Scrollable Clip List */}
              <Stack gap="xs" style={{ flex: 1, overflowY: "auto", overflowX: "hidden" }}>
                {playlist.clips.map((clip: any, index: number) => (
                  <QueueClipCard
                    key={clip.id}
                    clip={clip}
                    index={index}
                    isCurrentClip={index === currentClipIndex}
                    isHovered={hoveredClipIndex === index}
                    onHover={() => setHoveredClipIndex(index)}
                    onLeave={() => setHoveredClipIndex(null)}
                    onJumpTo={() => handleJumpToClip(index)}
                    onRemove={(e) => handleRemoveClip(clip.clipId, e)}
                  />
                ))}
              </Stack>
            </Stack>
          </Card>
        </Group>
      )}
    </Container>
  );
}

type QueueClipCardProps = {
  clip: any; // PlaylistClip type from API
  index: number;
  isCurrentClip: boolean;
  isHovered: boolean;
  onHover: () => void;
  onLeave: () => void;
  onJumpTo: () => void;
  onRemove: (e: React.MouseEvent) => void;
};

function QueueClipCard({
  clip,
  index,
  isCurrentClip,
  isHovered,
  onHover,
  onLeave,
  onJumpTo,
  onRemove,
}: QueueClipCardProps) {
  return (
    <UnstyledButton
      onClick={onJumpTo}
      onMouseEnter={onHover}
      onMouseLeave={onLeave}
      style={{ width: "100%" }}
    >
      <Card
        p="sm"
        radius="md"
        style={{
          background: isCurrentClip
            ? "linear-gradient(135deg, rgba(59, 130, 246, 0.15) 0%, rgba(37, 99, 235, 0.1) 100%)"
            : isHovered
              ? "rgba(255, 255, 255, 0.05)"
              : "transparent",
          border: isCurrentClip
            ? "1px solid rgba(59, 130, 246, 0.3)"
            : "1px solid transparent",
          transition: "all 0.2s ease",
          cursor: "pointer",
        }}
      >
        <Group wrap="nowrap" gap="sm">
          {/* Drag Handle (visual only for now) */}
          <Box
            style={{
              opacity: isHovered ? 1 : 0,
              transition: "opacity 0.2s ease",
              cursor: "grab",
            }}
          >
            <IconGripVertical size={16} style={{ opacity: 0.4 }} />
          </Box>

          {/* Position Number / Playing Indicator */}
          <Box w={24} ta="center">
            {isCurrentClip ? (
              <IconCheck size={18} color="var(--mantine-color-blue-4)" />
            ) : (
              <Text size="sm" c="dimmed" fw={600}>
                {index + 1}
              </Text>
            )}
          </Box>

          {/* Thumbnail Placeholder */}
          <Box
            style={{
              width: "80px",
              aspectRatio: "16/9",
              background: "linear-gradient(135deg, #1a1b1e 0%, #25262b 100%)",
              borderRadius: "6px",
              border: "1px solid rgba(255, 255, 255, 0.1)",
              flexShrink: 0,
            }}
          />

          {/* Clip Info */}
          <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
            <Text
              size="sm"
              fw={isCurrentClip ? 600 : 500}
              lineClamp={2}
              c={isCurrentClip ? "var(--mantine-color-blue-4)" : "inherit"}
            >
              Clip {clip.clipId.slice(0, 12)}...
            </Text>
            <Text size="xs" c="dimmed">
              Position: {clip.position}
            </Text>
          </Stack>

          {/* Remove Button */}
          {isHovered && !isCurrentClip && (
            <Tooltip label="Remove from playlist">
              <ActionIcon
                variant="subtle"
                color="red"
                size="sm"
                onClick={onRemove}
              >
                <IconTrash size={14} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
      </Card>
    </UnstyledButton>
  );
}
