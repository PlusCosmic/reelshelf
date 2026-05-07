/**
 * Playlists List Page
 * Route: /playlists
 *
 * Shows all playlists the user has access to (created or collaborating on)
 * Design pattern follows ApexClip.tsx layout with glass-morphic cards
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
  Group,

  Loader,
  ScrollArea,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  IconArrowLeft,
  IconClock,
  IconDeviceGamepad2,
  IconPlus,
  IconSearch,
  IconShare,
  IconTrash,
  IconUsers,
  IconVideo,
} from "@tabler/icons-react";
import { deletePlaylist, fetchPlaylists } from "@repo/shared";
import { formatDate } from "@/utils/format.ts";
import { useDisclosure } from "@mantine/hooks";
import { notifications } from "@mantine/notifications";
import { modals } from "@mantine/modals";
import { CreatePlaylistModal } from "./CreatePlaylistModal";
import { CreateGamingSessionModal } from "./CreateGamingSessionModal";
import { PlaylistCollaboratorsModal } from "./PlaylistCollaboratorsModal";
import type { PlaylistSummary } from "@repo/clips-api-client";

export function PlaylistsPage() {
  const [activeTab, setActiveTab] = useState<string | null>("all");
  const [searchQuery, setSearchQuery] = useState("");
  const [hoveredPlaylist, setHoveredPlaylist] = useState<string | null>(null);
  const [
    createModalOpened,
    { open: openCreateModal, close: closeCreateModal },
  ] = useDisclosure(false);
  const [
    gamingSessionModalOpened,
    { open: openGamingSessionModal, close: closeGamingSessionModal },
  ] = useDisclosure(false);
  const [collaboratorsPlaylist, setCollaboratorsPlaylist] =
    useState<PlaylistSummary | null>(null);
  const navigate = useNavigate();

  // Fetch playlists
  const {
    data: playlists = [],
    isLoading,
    refetch,
  } = useQuery({
    queryKey: ["playlists"],
    queryFn: fetchPlaylists,
  });

  // Filter playlists based on search and tab
  const filteredPlaylists = playlists.filter((playlist) => {
    return (
      searchQuery === "" ||
      playlist.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      playlist.description?.toLowerCase().includes(searchQuery.toLowerCase())
    );
  });

  const handlePlaylistClick = (playlistId: string) => {
    navigate({ to: `/playlists/$playlistId`, params: { playlistId } });
  };

  const handleDeletePlaylist = (
    playlistId: string,
    playlistName: string,
    e: React.MouseEvent,
  ) => {
    e.stopPropagation();
    modals.openConfirmModal({
      title: 'Delete Playlist',
      children: <Text size="sm">Are you sure you want to delete "{playlistName}"? This action cannot be undone.</Text>,
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        try {
          await deletePlaylist(playlistId);
          notifications.show({
            title: 'Playlist deleted',
            message: `"${playlistName}" has been deleted`,
            color: 'green',
          });
          refetch();
        } catch {
          notifications.show({
            title: 'Error',
            message: 'Failed to delete playlist',
            color: 'red',
          });
        }
      },
    });
  };

  const handleSharePlaylist = (playlistId: string, e: React.MouseEvent) => {
    e.stopPropagation();

    const url = `${window.location.origin}/playlists/${playlistId}`;
    navigator.clipboard.writeText(url);

    notifications.show({
      title: "Link copied",
      message: "Playlist link copied to clipboard",
      color: "blue",
    });
  };

  return (
    <div
      style={{ height: "calc(100vh - 138px)", display: "flex", gap: "1rem" }}
    >
      {/* Sidebar - Left Side */}
      <Card
        radius="xl"
        p="md"
        style={{
          width: "320px",
          background:
            "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
          backdropFilter: "blur(10px)",
          border: "1px solid rgba(0, 212, 255, 0.15)",
          boxShadow:
            "0 4px 30px rgba(0, 0, 0, 0.3), 0 0 20px rgba(0, 212, 255, 0.05)",
          flexShrink: 0,
          display: "flex",
          flexDirection: "column",
        }}
      >
        <Stack gap="md" h="100%">
          {/* Header */}
          <Group
            justify="center"
            align="center"
            style={{ position: "relative" }}
          >
            <ActionIcon
              variant="subtle"
              size="lg"
              onClick={() => navigate({ to: "/" })}
              aria-label="Back to clips"
              style={{
                position: "absolute",
                left: 0,
                color: "#00d4ff",
                transition: "all 0.2s ease",
              }}
            >
              <IconArrowLeft size={20} />
            </ActionIcon>
            <Title
              order={3}
              style={{
                letterSpacing: "-0.3px",
                background: "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
                WebkitBackgroundClip: "text",
                WebkitTextFillColor: "transparent",
              }}
            >
              Playlists
            </Title>
          </Group>
          <Text c="dimmed" size="sm" ta="center">
            Collaborative clip collections
          </Text>

          {/* Create Buttons */}
          <Stack gap="xs">
            <Button
              leftSection={<IconPlus size={18} />}
              size="md"
              variant="filled"
              onClick={openCreateModal}
              radius="md"
              fullWidth
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

            <Button
              leftSection={<IconDeviceGamepad2 size={18} />}
              size="md"
              variant="light"
              onClick={openGamingSessionModal}
              radius="md"
              fullWidth
              style={{
                background: "rgba(34, 197, 94, 0.1)",
                border: "1px solid rgba(34, 197, 94, 0.3)",
                color: "#22c55e",
              }}
            >
              Gaming Session
            </Button>
          </Stack>

          {/* Search */}
          <TextInput
            placeholder="Search playlists..."
            leftSection={<IconSearch size={16} style={{ color: "#00d4ff" }} />}
            size="sm"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
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
            }}
          />

          {/* Tabs */}
          <Tabs value={activeTab} onChange={setActiveTab} variant="pills">
            <Tabs.List grow>
              <Tabs.Tab
                value="all"
                leftSection={<IconVideo size={14} />}
                size="xs"
              >
                All
              </Tabs.Tab>
              <Tabs.Tab
                value="created"
                leftSection={<IconPlus size={14} />}
                size="xs"
              >
                Mine
              </Tabs.Tab>
              <Tabs.Tab
                value="shared"
                leftSection={<IconUsers size={14} />}
                size="xs"
              >
                Shared
              </Tabs.Tab>
            </Tabs.List>
          </Tabs>

          {/* Stats */}
          <Group gap="xs">
            <Badge
              variant="light"
              style={{
                background:
                  "linear-gradient(135deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.15) 100%)",
                border: "1px solid rgba(0, 212, 255, 0.3)",
                color: "#00d4ff",
              }}
            >
              {filteredPlaylists.length} playlists
            </Badge>
          </Group>
        </Stack>
      </Card>

      {/* Main Content - Right Side */}
      <Card
        radius="xl"
        p="lg"
        style={{
          flex: 1,
          background:
            "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
          backdropFilter: "blur(10px)",
          border: "1px solid rgba(0, 212, 255, 0.15)",
          boxShadow:
            "0 4px 30px rgba(0, 0, 0, 0.3), inset 0 0 60px rgba(0, 212, 255, 0.03)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Loading State */}
        {isLoading && (
          <Center style={{ flex: 1 }}>
            <Loader size="lg" />
          </Center>
        )}

        {/* Empty State */}
        {!isLoading && filteredPlaylists.length === 0 && (
          <Center style={{ flex: 1 }}>
            <Stack align="center" gap="md">
              <IconVideo
                size={64}
                stroke={1}
                style={{
                  color: "#00d4ff",
                  opacity: 0.5,
                  filter: "drop-shadow(0 0 20px rgba(0, 212, 255, 0.3))",
                }}
              />
              <Stack gap="xs" align="center">
                <Text size="lg" fw={600}>
                  {searchQuery ? "No playlists found" : "No playlists yet"}
                </Text>
                <Text size="sm" c="dimmed" ta="center" maw={400}>
                  {searchQuery
                    ? "Try adjusting your search"
                    : "Create your first playlist to start organizing clips"}
                </Text>
              </Stack>
              {!searchQuery && (
                <Button
                  leftSection={<IconPlus size={18} />}
                  variant="light"
                  size="md"
                  onClick={openCreateModal}
                  style={{
                    background: "rgba(0, 212, 255, 0.1)",
                    border: "1px solid rgba(0, 212, 255, 0.3)",
                    color: "#00d4ff",
                  }}
                >
                  Create Your First Playlist
                </Button>
              )}
            </Stack>
          </Center>
        )}

        {/* Playlists Grid */}
        {!isLoading && filteredPlaylists.length > 0 && (
          <ScrollArea
            h="100%"
            type="scroll"
            scrollbarSize={8}
            styles={{
              scrollbar: {
                "&:hover": {
                  backgroundColor: "rgba(0, 212, 255, 0.1)",
                },
              },
              thumb: {
                backgroundColor: "rgba(0, 212, 255, 0.3)",
                "&:hover": {
                  backgroundColor: "rgba(0, 212, 255, 0.5)",
                },
              },
            }}
          >
            <Stack gap="md">
              {filteredPlaylists.map((playlist) => (
                <PlaylistCard
                  key={playlist.id}
                  playlist={playlist}
                  isHovered={hoveredPlaylist === playlist.id}
                  onHover={() => setHoveredPlaylist(playlist.id)}
                  onLeave={() => setHoveredPlaylist(null)}
                  onClick={() => handlePlaylistClick(playlist.id)}
                  onDelete={(e) =>
                    handleDeletePlaylist(playlist.id, playlist.name, e)
                  }
                  onShare={(e) => handleSharePlaylist(playlist.id, e)}
                  onManageCollaborators={(e) => {
                    e.stopPropagation();
                    setCollaboratorsPlaylist(playlist);
                  }}
                  formatDate={(date) => formatDate(date.toString())}
                />
              ))}
            </Stack>
          </ScrollArea>
        )}
      </Card>

      {/* Create Playlist Modal */}
      <CreatePlaylistModal
        opened={createModalOpened}
        onClose={closeCreateModal}
        onSuccess={refetch}
      />

      {/* Collaborators Modal */}
      {collaboratorsPlaylist && (
        <PlaylistCollaboratorsModal
          opened={!!collaboratorsPlaylist}
          onClose={() => setCollaboratorsPlaylist(null)}
          playlistId={collaboratorsPlaylist.id}
          playlistName={collaboratorsPlaylist.name}
          creatorUserId={collaboratorsPlaylist.creatorUserId}
        />
      )}

      {/* Gaming Session Modal */}
      <CreateGamingSessionModal
        opened={gamingSessionModalOpened}
        onClose={closeGamingSessionModal}
        onSuccess={refetch}
      />
    </div>
  );
}

type PlaylistCardProps = {
  playlist: PlaylistSummary;
  isHovered: boolean;
  onHover: () => void;
  onLeave: () => void;
  onClick: () => void;
  onDelete: (e: React.MouseEvent) => void;
  onShare: (e: React.MouseEvent) => void;
  onManageCollaborators: (e: React.MouseEvent) => void;
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
  onManageCollaborators,
  formatDate,
}: PlaylistCardProps) {
  return (
    <Card
      radius="lg"
      p="md"
      onMouseEnter={onHover}
      onMouseLeave={onLeave}
      style={{
        transition: "all 0.3s cubic-bezier(0.4, 0, 0.2, 1)",
        transform: isHovered ? "translateY(-4px)" : "translateY(0)",
        boxShadow: isHovered
          ? "0 12px 30px rgba(0, 0, 0, 0.4), 0 0 30px rgba(0, 212, 255, 0.15)"
          : "0 4px 15px rgba(0, 0, 0, 0.2)",
        border: isHovered
          ? "1px solid rgba(0, 212, 255, 0.4)"
          : "1px solid rgba(0, 212, 255, 0.1)",
        background: isHovered
          ? "linear-gradient(135deg, rgba(15, 15, 25, 0.95) 0%, rgba(20, 20, 35, 0.9) 100%)"
          : "linear-gradient(135deg, rgba(15, 15, 25, 0.8) 0%, rgba(20, 20, 35, 0.7) 100%)",
        cursor: "pointer",
      }}
    >
      <UnstyledButton style={{ width: "100%" }} onClick={onClick}>
        <Group wrap="nowrap" gap="lg" align="flex-start">
          <Box pos="relative" style={{ flexShrink: 0 }}>
            <Box
              w={280}
              h={157}
              style={{
                borderRadius: "var(--mantine-radius-md)",
                background: "linear-gradient(135deg, rgba(0, 212, 255, 0.08) 0%, rgba(168, 85, 247, 0.08) 100%)",
                border: "1px solid rgba(0, 212, 255, 0.15)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                transition: "transform 0.2s ease",
                transform: isHovered ? "scale(1.02)" : "scale(1)",
              }}
            >
              <IconVideo size={40} style={{ color: "#00d4ff", opacity: 0.3 }} />
            </Box>

            {/* Clip Count Badge */}
            <Badge
              pos="absolute"
              bottom={8}
              right={8}
              size="lg"
              radius="sm"
              leftSection={<IconVideo size={14} />}
              style={{
                background: "rgba(10, 10, 20, 0.9)",
                backdropFilter: "blur(4px)",
                color: "#00d4ff",
                fontWeight: 700,
                border: "1px solid rgba(0, 212, 255, 0.3)",
                boxShadow: "0 0 15px rgba(0, 212, 255, 0.2)",
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
                style={{
                  background:
                    "linear-gradient(90deg, #f8fafc 0%, #00d4ff 100%)",
                  WebkitBackgroundClip: "text",
                  WebkitTextFillColor: "transparent",
                }}
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
              <IconUsers size={16} style={{ color: "#a855f7", opacity: 0.8 }} />
              <Text size="xs" c="dimmed">
                {playlist.collaboratorCount}{" "}
                {playlist.collaboratorCount === 1
                  ? "collaborator"
                  : "collaborators"}
              </Text>
            </Group>

            {/* Metadata */}
            <Group gap="md">
              <Group gap="xs">
                <IconClock
                  size={14}
                  style={{ color: "#00d4ff", opacity: 0.6 }}
                />
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
            <Tooltip label="Manage collaborators" position="left">
              <ActionIcon
                variant="light"
                size="lg"
                radius="md"
                onClick={onManageCollaborators}
                style={{
                  background: "rgba(168, 85, 247, 0.15)",
                  border: "1px solid rgba(168, 85, 247, 0.3)",
                  color: "#a855f7",
                  transition: "all 0.2s ease",
                }}
              >
                <IconUsers size={18} />
              </ActionIcon>
            </Tooltip>

            <Tooltip label="Share playlist" position="left">
              <ActionIcon
                variant="light"
                size="lg"
                radius="md"
                onClick={onShare}
                style={{
                  background: "rgba(0, 212, 255, 0.15)",
                  border: "1px solid rgba(0, 212, 255, 0.3)",
                  color: "#00d4ff",
                  transition: "all 0.2s ease",
                }}
              >
                <IconShare size={18} />
              </ActionIcon>
            </Tooltip>

            <Tooltip label="Delete playlist" position="left">
              <ActionIcon
                variant="light"
                size="lg"
                radius="md"
                onClick={onDelete}
                style={{
                  background: "rgba(236, 72, 153, 0.15)",
                  border: "1px solid rgba(236, 72, 153, 0.3)",
                  color: "#ec4899",
                  transition: "all 0.2s ease",
                }}
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
