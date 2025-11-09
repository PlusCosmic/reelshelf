import React, { useState } from "react";
import {
  ActionIcon,
  Badge,
  Box,
  Card,
  Group,
  Image,
  Stack,
  Text,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  IconClock,
  IconDownload,
  IconEdit,
  IconShare,
  IconTrash,
} from "@tabler/icons-react";
import { Link, useNavigate } from "@tanstack/react-router";
import { apiConfig, downloadVideo } from "@repo/shared";
import { notifications } from "@mantine/notifications";
import { modals } from "@mantine/modals";
import { useDeleteClip } from "../../hooks/queries.ts";
import type { Clip } from "@repo/nucleus-api-client";

type ClipCardProps = {
  clip: Clip;
};

export function ClipCard({ clip }: ClipCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const deleteClip = useDeleteClip();
  const navigate = useNavigate();

  const formatDuration = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, "0")}`;
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diffTime = Math.abs(now.getTime() - date.getTime());
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return "Today";
    if (diffDays === 1) return "Yesterday";
    if (diffDays < 7) return `${diffDays} days ago`;
    if (diffDays < 30) return `${Math.floor(diffDays / 7)} weeks ago`;
    return date.toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
    });
  };

  const handleShare = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();

    const clipUrl = `${window.location.origin}/apex-legends/${clip.clipId}`;

    try {
      await navigator.clipboard.writeText(clipUrl);
      notifications.show({
        title: "Link Copied",
        message: "Clip link has been copied to clipboard",
        color: "green",
      });
    } catch (error) {
      notifications.show({
        title: "Copy Failed",
        message: "Failed to copy link to clipboard",
        color: "red",
      });
    }
  };

  const handleDownload = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();

    try {
      await downloadVideo(clip.video.guid);
      notifications.show({
        title: "Download Started",
        message: "Your video download has started",
        color: "green",
      });
    } catch (error) {
      notifications.show({
        title: "Download Failed",
        message:
          error instanceof Error ? error.message : "Failed to download video",
        color: "red",
      });
    }
  };

  const handleDelete = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();

    modals.openConfirmModal({
      title: "Delete Video",
      children: (
        <Text size="sm">
          Are you sure you want to delete "{clip.video.title}"? This action
          cannot be undone.
        </Text>
      ),
      labels: { confirm: "Delete", cancel: "Cancel" },
      confirmProps: { color: "red" },
      onConfirm: () => {
        deleteClip.mutate(clip.clipId, {
          onSuccess: () => {
            notifications.show({
              title: "Video Deleted",
              message: "The video has been successfully deleted",
              color: "green",
            });
          },
          onError: () => {
            notifications.show({
              title: "Delete Failed",
              message: "Failed to delete video",
              color: "red",
            });
          },
        });
      },
    });
  };

  const handleEdit = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();

    await navigate({ to: "/apex-legends/$clipId", params: { clipId: clip.clipId } });
  };

  const date = formatDate(clip.createdAt.toString());

  return (
    <Card
      w="100%"
      mt="xs"
      mb="xs"
      radius="lg"
      p="md"
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
      }}
    >
      <Link style={{ textDecoration: 'none', color: 'inherit' }}
        to="/apex-legends/$clipId"
        params={{
          clipId: clip.clipId,
        }}
      >
        <UnstyledButton
          onMouseEnter={() => setIsHovered(true)}
          onMouseLeave={() => setIsHovered(false)}
          style={{ width: "100%" }}
        >
          <Group wrap="nowrap" gap="lg" align="center">
            {/* Thumbnail with overlay */}
            <Box pos="relative" style={{ flexShrink: 0 }}>
              <Image
                src={
                  isHovered
                    ? `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/preview.webp`
                    : `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`
                }
                style={{
                  aspectRatio: "16/9",
                  width: "240px",
                  transition: "transform 0.2s ease",
                  transform: isHovered ? "scale(1.02)" : "scale(1)",
                }}
                radius="md"
              />
              {!clip.isViewed && (
                <Badge
                  pos="absolute"
                  top={8}
                  left={8}
                  size="md"
                  radius="sm"
                  variant="filled"
                  color="blue"
                  style={{
                    background:
                      "linear-gradient(135deg, #3b82f6 0%, #2563eb 100%)",
                    backdropFilter: "blur(4px)",
                    boxShadow: "0 2px 8px rgba(59, 130, 246, 0.4)",
                    fontWeight: 700,
                    letterSpacing: "0.5px",
                  }}
                >
                  NEW
                </Badge>
              )}
              {clip.video.length && (
                <Badge
                  pos="absolute"
                  bottom={8}
                  right={8}
                  size="sm"
                  radius="sm"
                  leftSection={<IconClock size={12} />}
                  style={{
                    background: "rgba(0, 0, 0, 0.75)",
                    backdropFilter: "blur(4px)",
                    color: "white",
                  }}
                >
                  {formatDuration(clip.video.length)}
                </Badge>
              )}
            </Box>

            {/* Content */}
            <Stack gap="xs" style={{ flex: 1, minWidth: 0 }}>
              <Text
                fw={600}
                size="md"
                lineClamp={2}
                c={"var(--mantine-color-nucleusColour-2)"}
                style={{
                  lineHeight: 1.4,
                  letterSpacing: "-0.2px",
                }}
              >
                {clip.video.title}
              </Text>

              {/* Tags */}
              {clip.tags.length > 0 && (
                <Group gap="xs">
                  {clip.tags.slice(0, 4).map((tag, index) => (
                    <Badge
                      key={index}
                      size="sm"
                      radius="md"
                      variant="light"
                      color="blue"
                      style={{ textTransform: "none" }}
                    >
                      {tag}
                    </Badge>
                  ))}
                  {clip.tags.length > 4 && (
                    <Text size="xs" c="dimmed">
                      +{clip.tags.length - 4} more
                    </Text>
                  )}
                </Group>
              )}
              <Group gap="md">
                <Group gap="xs">
                  <IconClock size={14} style={{ opacity: 0.6 }} />
                  <Text size="xs" c="dimmed">
                    {date}
                  </Text>
                </Group>
              </Group>
            </Stack>

            <Stack gap="xs" style={{ flex: 1, minWidth: 0 }}>
              <Image src={`${apiConfig.baseUrl}${clip.detectedLegendCard}`} h={150} w={134}/>
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
              <Tooltip label="Share clip" position="left">
                <ActionIcon
                  variant="light"
                  color="blue"
                  size="lg"
                  radius="md"
                  onClick={handleShare}
                  style={{
                    transition: "all 0.2s ease",
                  }}
                >
                  <IconShare size={18} />
                </ActionIcon>
              </Tooltip>

              <Tooltip label="Edit clip" position="left">
                <ActionIcon
                  variant="light"
                  color="violet"
                  size="lg"
                  radius="md"
                  onClick={handleEdit}
                  style={{
                    transition: "all 0.2s ease",
                  }}
                >
                  <IconEdit size={18} />
                </ActionIcon>
              </Tooltip>

              <Tooltip label="Download clip" position="left">
                <ActionIcon
                  variant="light"
                  color="green"
                  size="lg"
                  radius="md"
                  onClick={handleDownload}
                  style={{
                    transition: "all 0.2s ease",
                  }}
                >
                  <IconDownload size={18} />
                </ActionIcon>
              </Tooltip>

              <Tooltip label="Delete clip" position="left">
                <ActionIcon
                  variant="light"
                  color="red"
                  size="lg"
                  radius="md"
                  onClick={handleDelete}
                  loading={deleteClip.isPending}
                  disabled={deleteClip.isPending}
                  style={{
                    transition: "all 0.2s ease",
                  }}
                >
                  <IconTrash size={18} />
                </ActionIcon>
              </Tooltip>
            </Stack>
          </Group>
        </UnstyledButton>
      </Link>
    </Card>
  );
}
