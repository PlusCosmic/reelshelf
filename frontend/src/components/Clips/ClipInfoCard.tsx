import {
  ActionIcon,
  Avatar,
  Card,
  Group,
  Stack,
  TagsInput,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { IconDownload, IconTrash } from "@tabler/icons-react";
import type { RefObject } from "react";
import type { Clip, DiscordUser } from "@/api-client";

interface ClipInfoCardProps {
  clip: Clip | null | undefined;
  clipOwner: DiscordUser | null | undefined;
  isLoadingClip: boolean;
  isLoadingClipOwner: boolean;
  titleValue: string;
  setTitleValue: (value: string) => void;
  tagsValue: Array<string>;
  setTagsValue: (value: Array<string>) => void;
  topTags: Array<string>;
  titleInputRef: RefObject<HTMLInputElement | null>;
  onSave: () => void;
  onDownload: () => void;
  onDelete: () => void;
}

export function ClipInfoCard({
  clip,
  clipOwner,
  isLoadingClip,
  isLoadingClipOwner,
  titleValue,
  setTitleValue,
  tagsValue,
  setTagsValue,
  topTags,
  titleInputRef,
  onSave,
  onDownload,
  onDelete,
}: ClipInfoCardProps) {
  return (
    <Card
      radius="xl"
      p="lg"
      mih={"170px"}
      style={{
        background:
          "linear-gradient(135deg, rgba(15, 15, 25, 0.9) 0%, rgba(20, 20, 35, 0.8) 100%)",
        backdropFilter: "blur(10px)",
        border: "1px solid rgba(0, 212, 255, 0.15)",
        boxShadow:
          "0 4px 30px rgba(0, 0, 0, 0.3), 0 0 30px rgba(0, 212, 255, 0.05)",
      }}
    >
      <Stack gap="md">
        {!isLoadingClip && clip && (
          <TextInput
            ref={titleInputRef}
            size="xl"
            variant="unstyled"
            value={titleValue}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                onSave();
              }
            }}
            onChange={(event) => setTitleValue(event.currentTarget.value)}
            styles={{
              input: {
                fontSize: "1.5rem",
                fontWeight: 600,
                letterSpacing: "-0.5px",
                background: "linear-gradient(90deg, #f8fafc 0%, #00d4ff 100%)",
                WebkitBackgroundClip: "text",
                WebkitTextFillColor: "transparent",
              },
            }}
          />
        )}

        <Group justify="space-between" wrap="nowrap">
          <Group gap="md">
            {!isLoadingClipOwner && clipOwner && (
              <>
                {clipOwner.avatar ? (
                  <Avatar
                    src={clipOwner.avatar}
                    radius="xl"
                    size="lg"
                    style={{
                      border: "2px solid rgba(0, 212, 255, 0.4)",
                      boxShadow: "0 0 15px rgba(0, 212, 255, 0.2)",
                    }}
                  />
                ) : (
                  <Avatar
                    variant="filled"
                    radius="xl"
                    color="cyan"
                    size="lg"
                    style={{
                      border: "2px solid rgba(0, 212, 255, 0.4)",
                      boxShadow: "0 0 15px rgba(0, 212, 255, 0.2)",
                    }}
                  />
                )}
              </>
            )}
            {!isLoadingClipOwner && clipOwner && !isLoadingClip && clip && (
              <Stack gap="xs">
                <Text fw={500} size="sm">
                  {clipOwner.username}
                </Text>
                <Text c="dimmed" size="xs">
                  {new Date(clip.video.dateUploaded).toLocaleDateString()}
                </Text>
              </Stack>
            )}
          </Group>

          <Group gap="xs">
            <TagsInput
              data={topTags}
              value={tagsValue}
              onChange={setTagsValue}
              maxTags={5}
              placeholder="Add tags"
              styles={{
                input: {
                  backgroundColor: "rgba(168, 85, 247, 0.05)",
                  border: "1px solid rgba(168, 85, 247, 0.2)",
                  "&:focus-within": {
                    borderColor: "rgba(168, 85, 247, 0.5)",
                    boxShadow: "0 0 15px rgba(168, 85, 247, 0.15)",
                  },
                },
                pill: {
                  background:
                    "linear-gradient(135deg, rgba(168, 85, 247, 0.3) 0%, rgba(236, 72, 153, 0.3) 100%)",
                  border: "1px solid rgba(168, 85, 247, 0.4)",
                  color: "#f8fafc",
                },
              }}
            />
            <Tooltip label="Download clip">
              <ActionIcon
                variant="light"
                color="green"
                size="lg"
                radius="md"
                onClick={onDownload}
                style={{
                  transition: "all 0.2s ease",
                  background: "rgba(16, 185, 129, 0.15)",
                  border: "1px solid rgba(16, 185, 129, 0.3)",
                }}
              >
                <IconDownload size={18} style={{ color: "#10b981" }} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete clip">
              <ActionIcon
                variant="light"
                color="pink"
                size="lg"
                radius="md"
                onClick={onDelete}
                style={{
                  transition: "all 0.2s ease",
                  background: "rgba(236, 72, 153, 0.15)",
                  border: "1px solid rgba(236, 72, 153, 0.3)",
                }}
              >
                <IconTrash size={18} style={{ color: "#ec4899" }} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>
      </Stack>
    </Card>
  );
}
