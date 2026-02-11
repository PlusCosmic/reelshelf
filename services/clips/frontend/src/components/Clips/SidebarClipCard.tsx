import { apiConfig } from "@repo/shared";
import { Avatar, Badge, Box, Card, Group, Image, Skeleton, Stack, Text, Tooltip } from "@mantine/core";
import { IconClock } from "@tabler/icons-react";
import { Link } from "@tanstack/react-router";
import type { Clip } from "@repo/nucleus-api-client";
import { formatDate, formatDuration, getProcessingStatusMessage, isClipProcessing } from "@/utils/format.ts";
import { useUserById } from "@/hooks/queries.ts";

interface SidebarClipCardProps {
  clip: Clip;
  categorySlug: string;
}

export function SidebarClipCard({ clip, categorySlug }: SidebarClipCardProps) {
  const { data: clipOwner } = useUserById(clip.ownerId);
  const processing = isClipProcessing(clip.video.status);
  const processingMessage = getProcessingStatusMessage(clip.video.status, clip.video.encodeProgress);

  return (
    <Link
      to="/games/$slug/$clipId"
      params={{ slug: categorySlug, clipId: clip.clipId }}
      style={{ textDecoration: "none", color: "inherit" }}
    >
      <Card
        p="sm"
        radius="md"
        style={{
          cursor: "pointer",
          transition: "all 0.3s cubic-bezier(0.4, 0, 0.2, 1)",
          border: "1px solid rgba(0, 212, 255, 0.1)",
          background: "linear-gradient(135deg, rgba(15, 15, 25, 0.8) 0%, rgba(20, 20, 35, 0.7) 100%)",
        }}
        styles={{
          root: {
            "&:hover": {
              transform: "translateY(-3px)",
              boxShadow: "0 8px 25px rgba(0, 0, 0, 0.3), 0 0 20px rgba(0, 212, 255, 0.1)",
              background: "linear-gradient(135deg, rgba(15, 15, 25, 0.95) 0%, rgba(20, 20, 35, 0.9) 100%)",
              borderColor: "rgba(0, 212, 255, 0.3)",
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
                  background: "linear-gradient(135deg, #f59e0b 0%, #d97706 100%)",
                  backdropFilter: "blur(4px)",
                  color: "white",
                  fontSize: "9px",
                  fontWeight: 700,
                  border: "1px solid rgba(245, 158, 11, 0.5)",
                  boxShadow: "0 2px 8px rgba(245, 158, 11, 0.4)",
                }}
              >
                PROCESSING
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
                  border: "1px solid rgba(245, 158, 11, 0.5)",
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
                    border: "2px solid rgba(0, 212, 255, 0.5)",
                    boxShadow: "0 2px 8px rgba(0, 212, 255, 0.3)",
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
