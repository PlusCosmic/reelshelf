import { apiConfig } from "@repo/shared";
import { Avatar, Badge, Box, Card, Group, Image, Skeleton, Stack, Text, Tooltip } from "@mantine/core";
import { IconClock } from "@tabler/icons-react";
import { Link } from "@tanstack/react-router";
import { formatDate, formatDuration, getProcessingStatusMessage, isClipProcessing } from "../../utils/format";
import { useUserById } from "../../hooks/queries";
import type { Clip } from "@repo/nucleus-api-client";

interface SidebarClipCardProps {
  clip: Clip;
}

export function SidebarClipCard({ clip }: SidebarClipCardProps) {
  const { data: clipOwner } = useUserById(clip.ownerId);
  const processing = isClipProcessing(clip.video.status);
  const processingMessage = getProcessingStatusMessage(clip.video.status, clip.video.encodeProgress);

  return (
    <Link
      to="/apex-legends/$clipId"
      params={{ clipId: clip.clipId }}
      style={{ textDecoration: "none", color: "inherit" }}
    >
      <Card
        p="sm"
        radius="md"
        style={{
          cursor: "pointer",
          transition: "all 0.2s ease",
          border: "1px solid rgba(255, 255, 255, 0.05)",
          background: "rgba(255, 255, 255, 0.02)",
        }}
        styles={{
          root: {
            "&:hover": {
              transform: "translateY(-2px)",
              boxShadow: "0 4px 12px rgba(0, 0, 0, 0.1)",
              background: "rgba(255, 255, 255, 0.04)",
            },
          },
        }}
      >
        <Stack gap="xs">
          <Box pos="relative">
            {processing ? (
              <Skeleton
                style={{
                  aspectRatio: "16/9",
                  width: "100%",
                }}
                radius="sm"
              />
            ) : (
              <Image
                src={`${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`}
                style={{
                  aspectRatio: "16/9",
                  width: "100%",
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
                style={{
                  background: "rgba(245, 158, 11, 0.9)",
                  backdropFilter: "blur(4px)",
                  color: "white",
                  fontSize: "9px",
                  fontWeight: 700,
                }}
              >
                PROCESSING
              </Badge>
            )}
            {clip.video.length && !processing && (
              <Badge
                pos="absolute"
                bottom={4}
                right={4}
                size="xs"
                radius="sm"
                leftSection={<IconClock size={10} />}
                style={{
                  background: "rgba(0, 0, 0, 0.75)",
                  backdropFilter: "blur(4px)",
                  color: "white",
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
                  fontSize: "9px",
                }}
              >
                {processingMessage}
              </Badge>
            )}
            {/* Owner Avatar Badge */}
            {clipOwner && (
              <Tooltip
                label={clipOwner.globalName || clipOwner.username}
                position="right"
                withArrow
                offset={4}
              >
                <Avatar
                  src={clipOwner.avatar}
                  alt={clipOwner.username}
                  size={28}
                  radius="xl"
                  pos="absolute"
                  bottom={4}
                  left={4}
                  style={{
                    border: "2px solid rgba(255, 255, 255, 0.9)",
                    boxShadow: "0 2px 8px rgba(0, 0, 0, 0.3)",
                    cursor: "pointer",
                  }}
                />
              </Tooltip>
            )}
          </Box>
          <Stack gap={4}>
            <Text
              size="sm"
              fw={600}
              lineClamp={2}
              style={{
                lineHeight: 1.3,
              }}
            >
              {clip.video.title}
            </Text>
            <Group gap={4} justify={"space-between"}>
              {clipOwner && (
                <Group gap={4}>
                  <Avatar
                    src={clipOwner.avatar}
                    alt={clipOwner.username}
                    size={12}
                    radius="xl"
                  />
                  <Text size="xs" c="dimmed" fw={500}>
                    {clipOwner.globalName || clipOwner.username}
                  </Text>
                </Group>
              )}
              <Group gap={4}>
                <IconClock size={12} style={{ opacity: 0.6 }} />
                <Text size="xs" c="dimmed">
                  {formatDate(clip.createdAt.toString())}
                </Text>
              </Group>
            </Group>
          </Stack>
        </Stack>
      </Card>
    </Link>
  );
}
