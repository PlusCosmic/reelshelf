import type { RefObject } from 'react';
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
} from '@mantine/core';
import { IconDownload, IconTrash } from '@tabler/icons-react';
import type { Clip, DiscordUser } from '@repo/nucleus-api-client';

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
        background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(255, 255, 255, 0.1)',
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
                fontSize: '1.5rem',
                fontWeight: 600,
                letterSpacing: '-0.5px',
              }
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
                  />
                ) : (
                  <Avatar
                    variant="filled"
                    radius="xl"
                    color="green"
                    size="lg"
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
                  {clip.video.dateUploaded.toDateString()}
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
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.08)',
                }
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
                  transition: 'all 0.2s ease',
                }}
              >
                <IconDownload size={18} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete clip">
              <ActionIcon
                variant="light"
                color="red"
                size="lg"
                radius="md"
                onClick={onDelete}
                style={{
                  transition: 'all 0.2s ease',
                }}
              >
                <IconTrash size={18} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>
      </Stack>
    </Card>
  );
}
