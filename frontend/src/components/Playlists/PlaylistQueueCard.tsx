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
  IconCheck,
  IconClock,
  IconGripVertical,
  IconTrash,
} from "@tabler/icons-react";
import { apiConfig } from "@/shared";
import type { Clip } from "@/api-client";
import {
  formatDate,
  formatDuration,
  getProcessingStatusMessage,
  isClipProcessing,
} from "@/utils/format.ts";
import { useUserById } from "@/hooks/queries.ts";

interface PlaylistQueueCardProps {
  clip: Clip;
  index: number;
  isCurrentClip: boolean;
  isHovered: boolean;
  onHover: () => void;
  onLeave: () => void;
  onJumpTo: () => void;
  onRemove: (e: React.MouseEvent) => void;
}

export function PlaylistQueueCard({
  clip,
  index,
  isCurrentClip,
  isHovered,
  onHover,
  onLeave,
  onJumpTo,
  onRemove,
}: PlaylistQueueCardProps) {
  const { data: clipOwner } = useUserById(clip.ownerId);
  const processing = isClipProcessing(clip.video.status);
  const processingMessage = getProcessingStatusMessage(
    clip.video.status,
    clip.video.encodeProgress,
  );

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
            ? "linear-gradient(135deg, rgba(0, 212, 255, 0.15) 0%, rgba(168, 85, 247, 0.1) 100%)"
            : isHovered
              ? "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)"
              : "linear-gradient(135deg, rgba(15, 15, 25, 0.6) 0%, rgba(20, 20, 35, 0.5) 100%)",
          border: isCurrentClip
            ? "1px solid rgba(0, 212, 255, 0.4)"
            : isHovered
              ? "1px solid rgba(0, 212, 255, 0.2)"
              : "1px solid rgba(0, 212, 255, 0.08)",
          boxShadow: isCurrentClip
            ? "0 0 20px rgba(0, 212, 255, 0.15)"
            : "none",
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
              <IconCheck
                size={18}
                color="#00d4ff"
                style={{
                  filter: "drop-shadow(0 0 8px rgba(0, 212, 255, 0.5))",
                }}
              />
            ) : (
              <Text size="sm" c="dimmed" fw={600}>
                {index + 1}
              </Text>
            )}
          </Box>

          {/* Thumbnail */}
          <Box pos="relative" style={{ flexShrink: 0 }}>
            {processing ? (
              <Skeleton w={80} style={{ aspectRatio: "16/9" }} radius="sm" />
            ) : (
              <Image
                src={`${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`}
                w={80}
                style={{ aspectRatio: "16/9" }}
                radius="sm"
              />
            )}
            {processing && (
              <Badge
                pos="absolute"
                top={2}
                left={2}
                size="xs"
                radius="sm"
                style={{
                  background: "rgba(245, 158, 11, 0.9)",
                  backdropFilter: "blur(4px)",
                  color: "white",
                  fontSize: "8px",
                  fontWeight: 700,
                  padding: "2px 4px",
                }}
              >
                {processingMessage}
              </Badge>
            )}
            {clip.video.length > 0 && !processing && (
              <Badge
                pos="absolute"
                bottom={2}
                right={2}
                size="xs"
                radius="sm"
                leftSection={<IconClock size={8} />}
                style={{
                  background: "rgba(10, 10, 20, 0.85)",
                  backdropFilter: "blur(4px)",
                  color: "#00d4ff",
                  fontSize: "9px",
                  padding: "2px 4px",
                  border: "1px solid rgba(0, 212, 255, 0.2)",
                }}
              >
                {formatDuration(clip.video.length)}
              </Badge>
            )}
          </Box>

          {/* Clip Info */}
          <Stack gap={4} style={{ flex: 1, minWidth: 0 }}>
            <Text
              size="sm"
              fw={isCurrentClip ? 600 : 500}
              lineClamp={1}
              style={
                isCurrentClip
                  ? {
                      background:
                        "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
                      WebkitBackgroundClip: "text",
                      WebkitTextFillColor: "transparent",
                    }
                  : undefined
              }
            >
              {clip.video.title}
            </Text>
            <Group gap={4}>
              {clipOwner && (
                <>
                  <Avatar
                    src={clipOwner.avatar}
                    alt={clipOwner.username}
                    size={12}
                    radius="xl"
                  />
                  <Text size="xs" c="dimmed" fw={500}>
                    {clipOwner.globalName || clipOwner.username}
                  </Text>
                </>
              )}
              <Text size="xs" c="dimmed">
                {formatDate(clip.createdAt.toString())}
              </Text>
            </Group>
          </Stack>

          {/* Remove Button */}
          {isHovered && !isCurrentClip && (
            <Tooltip label="Remove from playlist">
              <ActionIcon
                variant="subtle"
                size="sm"
                onClick={onRemove}
                style={{
                  color: "#ec4899",
                  transition: "all 0.2s ease",
                }}
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
