import React, { useState } from "react";
import {
  ActionIcon,
  Avatar,
  Badge,
  Box,
  Card,
  Group,
  Image,
  Skeleton,
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
import { useNavigate } from "@tanstack/react-router";
import { apiConfig, downloadVideo } from "@repo/shared";
import { notifications } from "@mantine/notifications";
import { modals } from "@mantine/modals";
import { AddToPlaylistButton } from "../Playlists/AddToPlaylistButton";
import { ApexMetadata } from "./ApexMetadata";
import type { Clip } from "@repo/clips-api-client";
import { useDeleteClip, useUserById } from "@/hooks/queries.ts";
import {
  formatDate,
  formatDuration,
  getProcessingStatusMessage,
  isClipProcessing,
} from "@/utils/format.ts";

type ClipCardProps = {
  clip: Clip;
  categorySlug: string;
};

export function ClipCard({ clip, categorySlug }: ClipCardProps) {
  const [isHovered, setIsHovered] = useState(false);
  const deleteClip = useDeleteClip();
  const navigate = useNavigate();
  const { data: clipOwner } = useUserById(clip.ownerId);

  const handleShare = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();

    const clipUrl = `${window.location.origin}/games/${categorySlug}/${clip.clipId}`;

    try {
      await navigator.clipboard.writeText(clipUrl);
      notifications.show({
        title: "Link Copied",
        message: "Clip link has been copied to clipboard",
        color: "green",
      });
    } catch {
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

    await navigate({
      to: "/games/$slug/$clipId",
      params: { slug: categorySlug, clipId: clip.clipId },
    });
  };

  const date = formatDate(clip.createdAt.toString());
  const processing = isClipProcessing(clip.video.status);
  const processingMessage = getProcessingStatusMessage(
    clip.video.status,
    clip.video.encodeProgress,
  );

  return (
    <Card
      w="100%"
      radius="md"
      p="xs"
      style={{
        transition: "all 0.2s cubic-bezier(0.4, 0, 0.2, 1)",
        transform: isHovered ? "translateY(-2px)" : "translateY(0)",
        boxShadow: isHovered
          ? "0 8px 24px rgba(0, 0, 0, 0.3), 0 0 20px rgba(0, 212, 255, 0.1)"
          : "0 1px 4px rgba(0, 0, 0, 0.15)",
        border: isHovered
          ? "1px solid rgba(0, 212, 255, 0.4)"
          : "1px solid rgba(0, 212, 255, 0.1)",
        background: isHovered
          ? "linear-gradient(135deg, rgba(15, 15, 25, 0.95) 0%, rgba(20, 20, 35, 0.9) 100%)"
          : "linear-gradient(135deg, rgba(15, 15, 25, 0.8) 0%, rgba(20, 20, 35, 0.7) 100%)",
      }}
    >
      <UnstyledButton
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => setIsHovered(false)}
        onClick={() => navigate({ to: '/games/$slug/$clipId', params: { slug: categorySlug, clipId: clip.clipId } })}
        style={{ width: "100%" }}
      >
          <Group wrap="nowrap" gap="sm" align="center">
            {/* Thumbnail with overlay */}
            <Box pos="relative" style={{ flexShrink: 0 }}>
              {processing ? (
                <Skeleton
                  style={{
                    aspectRatio: "16/9",
                    width: "160px",
                  }}
                  radius="sm"
                />
              ) : (
                <Image
                  src={
                    isHovered
                      ? `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/preview.webp`
                      : `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`
                  }
                  style={{
                    aspectRatio: "16/9",
                    width: "160px",
                    transition: "transform 0.2s ease",
                    transform: isHovered ? "scale(1.02)" : "scale(1)",
                  }}
                  radius="sm"
                />
              )}
              {processing && (
                <Badge
                  pos="absolute"
                  top={4}
                  left={4}
                  size="xs"
                  radius="sm"
                  variant="filled"
                  style={{
                    background:
                      "linear-gradient(135deg, #f59e0b 0%, #d97706 100%)",
                    backdropFilter: "blur(4px)",
                    boxShadow: "0 2px 8px rgba(245, 158, 11, 0.4)",
                    fontWeight: 600,
                    fontSize: "9px",
                    border: "1px solid rgba(245, 158, 11, 0.5)",
                  }}
                >
                  PROCESSING
                </Badge>
              )}
              {!clip.isViewed && !processing && (
                <Badge
                  pos="absolute"
                  top={4}
                  left={4}
                  size="xs"
                  radius="sm"
                  variant="filled"
                  style={{
                    background:
                      "linear-gradient(135deg, #00d4ff 0%, #0ea5e9 100%)",
                    backdropFilter: "blur(4px)",
                    boxShadow: "0 2px 8px rgba(0, 212, 255, 0.4)",
                    fontWeight: 600,
                    fontSize: "9px",
                    border: "1px solid rgba(0, 212, 255, 0.5)",
                    color: "#0a0a14",
                  }}
                >
                  NEW
                </Badge>
              )}
              {clip.video.length > 0 && !processing && (
                <Badge
                  pos="absolute"
                  bottom={4}
                  right={4}
                  size="xs"
                  radius="sm"
                  leftSection={<IconClock size={10} />}
                  style={{
                    background: "rgba(10, 10, 20, 0.85)",
                    backdropFilter: "blur(4px)",
                    color: "#00d4ff",
                    border: "1px solid rgba(0, 212, 255, 0.3)",
                    fontSize: "10px",
                  }}
                >
                  {formatDuration(clip.video.length)}
                </Badge>
              )}
              {processing && (
                <Badge
                  pos="absolute"
                  bottom={4}
                  right={4}
                  size="xs"
                  radius="sm"
                  style={{
                    background: "rgba(245, 158, 11, 0.9)",
                    backdropFilter: "blur(4px)",
                    color: "white",
                    border: "1px solid rgba(245, 158, 11, 0.5)",
                    fontSize: "9px",
                  }}
                >
                  {processingMessage}
                </Badge>
              )}
            </Box>

            {/* Content */}
            <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
              <Text
                fw={600}
                size="sm"
                lineClamp={1}
                style={{
                  lineHeight: 1.3,
                  letterSpacing: "-0.2px",
                  color: isHovered ? "#00d4ff" : "#f8fafc",
                  transition: "color 0.2s ease",
                }}
              >
                {clip.video.title}
              </Text>

              {/* Tags */}
              {clip.tags.length > 0 && (
                <Group gap={4}>
                  {clip.tags.slice(0, 3).map((tag, index) => (
                    <Badge
                      key={index}
                      size="xs"
                      radius="sm"
                      variant="light"
                      style={{
                        textTransform: "none",
                        background: "rgba(168, 85, 247, 0.15)",
                        border: "1px solid rgba(168, 85, 247, 0.3)",
                        color: "#a855f7",
                        fontSize: "10px",
                      }}
                    >
                      {tag}
                    </Badge>
                  ))}
                  {clip.tags.length > 3 && (
                    <Text size="xs" c="dimmed" style={{ fontSize: "10px" }}>
                      +{clip.tags.length - 3}
                    </Text>
                  )}
                </Group>
              )}
              <Group gap="sm">
                {clipOwner && (
                  <Group gap={4}>
                    <Avatar
                      src={clipOwner.avatar}
                      alt={clipOwner.username}
                      size={14}
                      radius="xl"
                    />
                    <Text
                      size="xs"
                      c="dimmed"
                      fw={500}
                      style={{ fontSize: "11px" }}
                    >
                      {clipOwner.globalName || clipOwner.username}
                    </Text>
                  </Group>
                )}
                <Text size="xs" c="dimmed" style={{ fontSize: "11px" }}>
                  {date}
                </Text>
              </Group>
            </Stack>

            {/* Game-specific metadata (e.g., Apex legend detection) */}
            <ApexMetadata
              detectedLegend={clip.gameMetadata?.detected_legend}
              detectedLegendCard={clip.gameMetadata?.detected_legend_card}
            />

            {/* Action Buttons - Horizontal layout */}
            <Group
              gap={4}
              style={{
                flexShrink: 0,
                opacity: isHovered ? 1 : 0,
                transition: "opacity 0.2s ease",
                pointerEvents: isHovered ? "auto" : "none",
              }}
            >
              <Tooltip label="Share" position="top">
                <ActionIcon
                  variant="light"
                  size="sm"
                  radius="sm"
                  onClick={handleShare}
                  style={{
                    transition: "all 0.2s ease",
                    background: "rgba(0, 212, 255, 0.1)",
                    border: "1px solid rgba(0, 212, 255, 0.3)",
                    color: "#00d4ff",
                  }}
                >
                  <IconShare size={14} />
                </ActionIcon>
              </Tooltip>

              <AddToPlaylistButton clipId={clip.clipId} compact />

              <Tooltip label="Edit" position="top">
                <ActionIcon
                  variant="light"
                  size="sm"
                  radius="sm"
                  onClick={handleEdit}
                  style={{
                    transition: "all 0.2s ease",
                    background: "rgba(168, 85, 247, 0.1)",
                    border: "1px solid rgba(168, 85, 247, 0.3)",
                    color: "#a855f7",
                  }}
                >
                  <IconEdit size={14} />
                </ActionIcon>
              </Tooltip>

              <Tooltip label="Download" position="top">
                <ActionIcon
                  variant="light"
                  size="sm"
                  radius="sm"
                  onClick={handleDownload}
                  style={{
                    transition: "all 0.2s ease",
                    background: "rgba(34, 197, 94, 0.1)",
                    border: "1px solid rgba(34, 197, 94, 0.3)",
                    color: "#22c55e",
                  }}
                >
                  <IconDownload size={14} />
                </ActionIcon>
              </Tooltip>

              <Tooltip label="Delete" position="top">
                <ActionIcon
                  variant="light"
                  size="sm"
                  radius="sm"
                  onClick={handleDelete}
                  loading={deleteClip.isPending}
                  disabled={deleteClip.isPending}
                  style={{
                    transition: "all 0.2s ease",
                    background: "rgba(236, 72, 153, 0.1)",
                    border: "1px solid rgba(236, 72, 153, 0.3)",
                    color: "#ec4899",
                  }}
                >
                  <IconTrash size={14} />
                </ActionIcon>
              </Tooltip>
            </Group>
          </Group>
      </UnstyledButton>
    </Card>
  );
}
