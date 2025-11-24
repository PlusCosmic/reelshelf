/**
 * Playlists List Page
 * Route: /playlists
 *
 * Shows all playlists the user has access to (created or collaborating on)
 */

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Card,
  Center,
  Container,
  Group,
  Image,
  Loader,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  IconClock,
  IconPlus,
  IconSearch,
  IconShare,
  IconTrash,
  IconUsers,
  IconVideo,
} from "@tabler/icons-react";
import { deletePlaylist, fetchPlaylists } from "@repo/shared";
import { useDisclosure } from "@mantine/hooks";
import { notifications } from "@mantine/notifications";
import { CreatePlaylistModal } from "./CreatePlaylistModal";

export function PlaylistsPage() {
  const [activeTab, setActiveTab] = useState<string | null>("all");
  const [searchQuery, setSearchQuery] = useState("");
  const [hoveredPlaylist, setHoveredPlaylist] = useState<string | null>(null);
  const [createModalOpened, { open: openCreateModal, close: closeCreateModal }] = useDisclosure(false);
  const navigate = useNavigate();

  // Fetch playlists
  const { data: playlists = [], isLoading, refetch } = useQuery({
    queryKey: ['playlists'],
    queryFn: fetchPlaylists,
  });

  // Filter playlists based on search and tab
  const filteredPlaylists = playlists.filter(playlist => {
    const matchesSearch = searchQuery === "" ||
      playlist.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      playlist.description?.toLowerCase().includes(searchQuery.toLowerCase());

    // TODO: Filter by tab (need current user ID to implement)
    // For now, show all playlists in all tabs
    return matchesSearch;
  });

  const handlePlaylistClick = (playlistId: string) => {
    navigate({ to: `/playlists/$playlistId`, params: { playlistId } });
  };

  const handleDeletePlaylist = async (playlistId: string, playlistName: string, e: React.MouseEvent) => {
    e.stopPropagation();

    if (!confirm(`Are you sure you want to delete "${playlistName}"? This action cannot be undone.`)) {
      return;
    }

    try {
      await deletePlaylist(playlistId);
      notifications.show({
        title: 'Playlist deleted',
        message: `"${playlistName}" has been deleted`,
        color: 'green',
      });
      refetch();
    } catch (error) {
      notifications.show({
        title: 'Error',
        message: 'Failed to delete playlist',
        color: 'red',
      });
    }
  };

  const handleSharePlaylist = (playlistId: string, e: React.MouseEvent) => {
    e.stopPropagation();

    const url = `${window.location.origin}/playlists/${playlistId}`;
    navigator.clipboard.writeText(url);

    notifications.show({
      title: 'Link copied',
      message: 'Playlist link copied to clipboard',
      color: 'blue',
    });
  };

  const formatDate = (date: Date) => {
    const now = new Date();
    const diffTime = Math.abs(now.getTime() - date.getTime());
    const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return "Today";
    if (diffDays === 1) return "Yesterday";
    if (diffDays < 7) return `${diffDays} days ago`;
    return date.toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
    });
  };

  return (
    <Container size="xl" py="xl">
      {/* Header */}
      <Group justify="space-between" mb="xl">
        <Stack gap="xs">
          <Title order={1}>Playlists</Title>
          <Text c="dimmed" size="sm">
            Collaborative clip collections from your gaming sessions
          </Text>
        </Stack>
        <Button
          leftSection={<IconPlus size={18} />}
          size="md"
          variant="gradient"
          gradient={{ from: "blue", to: "cyan", deg: 45 }}
          onClick={openCreateModal}
        >
          Create Playlist
        </Button>
      </Group>

      {/* Search and Tabs */}
      <Stack gap="md" mb="xl">
        <TextInput
          placeholder="Search playlists..."
          leftSection={<IconSearch size={18} />}
          size="md"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          styles={{
            input: {
              borderRadius: "12px",
            },
          }}
        />

        <Tabs value={activeTab} onChange={setActiveTab}>
          <Tabs.List>
            <Tabs.Tab value="all" leftSection={<IconVideo size={16} />}>
              All Playlists
            </Tabs.Tab>
            <Tabs.Tab value="created" leftSection={<IconPlus size={16} />}>
              Created by Me
            </Tabs.Tab>
            <Tabs.Tab value="shared" leftSection={<IconUsers size={16} />}>
              Shared with Me
            </Tabs.Tab>
          </Tabs.List>
        </Tabs>
      </Stack>

      {/* Loading State */}
      {isLoading && (
        <Center py="xl">
          <Loader size="lg" />
        </Center>
      )}

      {/* Playlists Grid */}
      {!isLoading && filteredPlaylists.length > 0 && (
        <Stack gap="md">
          {filteredPlaylists.map((playlist) => (
            <PlaylistCard
              key={playlist.id}
              playlist={playlist}
              isHovered={hoveredPlaylist === playlist.id}
              onHover={() => setHoveredPlaylist(playlist.id)}
              onLeave={() => setHoveredPlaylist(null)}
              onClick={() => handlePlaylistClick(playlist.id)}
              onDelete={(e) => handleDeletePlaylist(playlist.id, playlist.name, e)}
              onShare={(e) => handleSharePlaylist(playlist.id, e)}
              formatDate={formatDate}
            />
          ))}
        </Stack>
      )}

      {/* Empty State */}
      {!isLoading && filteredPlaylists.length === 0 && (
        <Card
          p="xl"
          radius="lg"
          style={{
            border: "2px dashed rgba(255, 255, 255, 0.1)",
            background: "rgba(255, 255, 255, 0.02)",
          }}
        >
          <Stack align="center" gap="md" py="xl">
            <IconVideo size={64} stroke={1} style={{ opacity: 0.3 }} />
            <Stack gap="xs" align="center">
              <Text size="lg" fw={600}>
                {searchQuery ? "No playlists found" : "No playlists yet"}
              </Text>
              <Text size="sm" c="dimmed" ta="center" maw={400}>
                {searchQuery
                  ? "Try adjusting your search"
                  : "Create your first playlist to start organizing clips from your gaming sessions"
                }
              </Text>
            </Stack>
            {!searchQuery && (
              <Button
                leftSection={<IconPlus size={18} />}
                variant="light"
                size="md"
                onClick={openCreateModal}
              >
                Create Your First Playlist
              </Button>
            )}
          </Stack>
        </Card>
      )}

      {/* Create Playlist Modal */}
      <CreatePlaylistModal
        opened={createModalOpened}
        onClose={closeCreateModal}
        onSuccess={refetch}
      />
    </Container>
  );
}

type PlaylistCardProps = {
  playlist: any; // PlaylistSummary type from API
  isHovered: boolean;
  onHover: () => void;
  onLeave: () => void;
  onClick: () => void;
  onDelete: (e: React.MouseEvent) => void;
  onShare: (e: React.MouseEvent) => void;
  formatDate: (date: Date) => string;
};

function PlaylistCard({
  playlist,
  isHovered,
  onHover,
  onLeave,
  onClick,
  onDelete,
  onShare,
  formatDate,
}: PlaylistCardProps) {
  return (
    <Card
      radius="lg"
      p="md"
      onMouseEnter={onHover}
      onMouseLeave={onLeave}
      style={{
        transition: "all 0.2s ease",
        transform: isHovered ? "translateY(-2px)" : "translateY(0)",
        boxShadow: isHovered
          ? "0 8px 24px rgba(0, 0, 0, 0.15), 0 0 0 1px rgba(255, 255, 255, 0.1)"
          : "0 2px 8px rgba(0, 0, 0, 0.08)",
        border: "1px solid rgba(255, 255, 255, 0.05)",
        background: isHovered
          ? "linear-gradient(135deg, rgba(255, 255, 255, 0.08) 0%, rgba(255, 255, 255, 0.04) 100%)"
          : "rgba(255, 255, 255, 0.02)",
        cursor: "pointer",
      }}
    >
      <UnstyledButton style={{ width: "100%" }} onClick={onClick}>
        <Group wrap="nowrap" gap="lg" align="flex-start">
          {/* Placeholder Thumbnail - would be first clip thumbnail in real implementation */}
          <Box pos="relative" style={{ flexShrink: 0 }}>
            <Image
              src="https://placehold.co/320x180/1a1b1e/white?text=Playlist"
              w={320}
              h={180}
              radius="md"
              style={{
                transition: "transform 0.2s ease",
                transform: isHovered ? "scale(1.02)" : "scale(1)",
              }}
            />

            {/* Clip Count Badge */}
            <Badge
              pos="absolute"
              bottom={8}
              right={8}
              size="lg"
              radius="sm"
              leftSection={<IconVideo size={14} />}
              style={{
                background: "rgba(0, 0, 0, 0.75)",
                backdropFilter: "blur(4px)",
                color: "white",
                fontWeight: 700,
              }}
            >
              {playlist.clipCount} {playlist.clipCount === 1 ? "clip" : "clips"}
            </Badge>
          </Box>

          {/* Playlist Info */}
          <Stack gap="sm" style={{ flex: 1, minWidth: 0 }}>
            <Stack gap="xs">
              <Text
                fw={600}
                size="lg"
                lineClamp={1}
                c="var(--mantine-color-nucleusColour-2)"
              >
                {playlist.name}
              </Text>
              {playlist.description && (
                <Text size="sm" c="dimmed" lineClamp={2}>
                  {playlist.description}
                </Text>
              )}
            </Stack>

            {/* Collaborators */}
            <Group gap="xs">
              <IconUsers size={16} style={{ opacity: 0.6 }} />
              <Text size="xs" c="dimmed">
                {playlist.collaboratorCount}{" "}
                {playlist.collaboratorCount === 1 ? "collaborator" : "collaborators"}
              </Text>
            </Group>

            {/* Metadata */}
            <Group gap="md">
              <Group gap="xs">
                <IconClock size={14} style={{ opacity: 0.6 }} />
                <Text size="xs" c="dimmed">
                  Updated {formatDate(playlist.updatedAt)}
                </Text>
              </Group>
            </Group>
          </Stack>

          {/* Action Buttons */}
          <Stack
            gap="xs"
            justify="center"
            style={{
              flexShrink: 0,
              opacity: isHovered ? 1 : 0,
              transition: "opacity 0.2s ease",
              pointerEvents: isHovered ? "auto" : "none",
            }}
          >
            <Tooltip label="Share playlist" position="left">
              <ActionIcon
                variant="light"
                color="blue"
                size="lg"
                radius="md"
                onClick={onShare}
              >
                <IconShare size={18} />
              </ActionIcon>
            </Tooltip>

            <Tooltip label="Delete playlist" position="left">
              <ActionIcon
                variant="light"
                color="red"
                size="lg"
                radius="md"
                onClick={onDelete}
              >
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          </Stack>
        </Group>
      </UnstyledButton>
    </Card>
  );
}
