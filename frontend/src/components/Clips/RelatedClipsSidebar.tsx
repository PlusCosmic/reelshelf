import {
  ActionIcon,
  Card,
  Group,
  ScrollArea,
  Stack,
  Text,
} from "@mantine/core";
import { IconArrowLeft } from "@tabler/icons-react";
import { useNavigate } from "@tanstack/react-router";
import { SidebarClipCard } from "./SidebarClipCard";
import type { Clip } from "@/api-client";

interface RelatedClipsSidebarProps {
  relatedClips: Array<Clip>;
  isLoading: boolean;
  categorySlug: string;
}

export function RelatedClipsSidebar({
  relatedClips,
  isLoading,
  categorySlug,
}: RelatedClipsSidebarProps) {
  const navigate = useNavigate();

  return (
    <Card
      radius="xl"
      p="md"
      style={{
        width: "380px",
        background:
          "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
        backdropFilter: "blur(10px)",
        border: "1px solid rgba(0, 212, 255, 0.15)",
        boxShadow:
          "0 4px 30px rgba(0, 0, 0, 0.3), 0 0 20px rgba(0, 212, 255, 0.05)",
        flexShrink: 0,
      }}
    >
      <Stack gap="md" h="100%">
        <Group justify="center" align="center" style={{ position: "relative" }}>
          <ActionIcon
            variant="subtle"
            size="lg"
            onClick={() =>
              navigate({ to: "/games/$slug", params: { slug: categorySlug } })
            }
            aria-label="Back to clips list"
            style={{
              position: "absolute",
              left: 0,
              color: "#00d4ff",
              transition: "all 0.2s ease",
            }}
          >
            <IconArrowLeft size={20} />
          </ActionIcon>
          <Text
            size="lg"
            fw={600}
            style={{
              letterSpacing: "-0.3px",
              background: "linear-gradient(90deg, #00d4ff 0%, #a855f7 100%)",
              WebkitBackgroundClip: "text",
              WebkitTextFillColor: "transparent",
            }}
          >
            Up Next
          </Text>
        </Group>

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
          <Stack gap="xs">
            {!isLoading &&
              relatedClips.map((relatedClip) => (
                <SidebarClipCard
                  key={relatedClip.clipId}
                  clip={relatedClip}
                  categorySlug={categorySlug}
                />
              ))}
          </Stack>
        </ScrollArea>
      </Stack>
    </Card>
  );
}
