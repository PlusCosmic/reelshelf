import { useEffect, useRef, useState } from "react";
import {
  apiConfig,
  downloadVideo,
} from "@repo/shared";
import {
  ActionIcon,
  Avatar,
  Badge,
  Box,
  Card,
  Group,
  Image,
  ScrollArea,
  Stack,
  TagsInput,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { IconClock, IconDownload, IconTrash } from "@tabler/icons-react";
import { notifications } from '@mantine/notifications';
import { modals } from '@mantine/modals';
import { Link, useNavigate } from '@tanstack/react-router';
import {
  useAddTag,
  useApexClips,
  useClip,
  useDeleteClip,
  useMarkAsViewed,
  useRemoveTag,
  useTopTags,
  useUpdateClipTitle,
  useUserById
} from '../../hooks/queries';
import type { Clip } from "@repo/nucleus-api-client";

interface ApexClipProps {
  clipId: string;
}

function SidebarClipCard({ clip }: { clip: Clip }) {
  const formatDuration = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diffTime = Math.abs(now.getTime() - date.getTime());
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'Today';
    if (diffDays === 1) return 'Yesterday';
    if (diffDays < 7) return `${diffDays} days ago`;
    if (diffDays < 30) return `${Math.floor(diffDays / 7)} weeks ago`;
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  };

  return (
    <Link
      to="/apex-legends/$clipId"
      params={{ clipId: clip.clipId }}
      style={{ textDecoration: 'none', color: 'inherit' }}
    >
      <Card
        p="sm"
        radius="md"
        style={{
          cursor: 'pointer',
          transition: 'all 0.2s ease',
          border: '1px solid rgba(255, 255, 255, 0.05)',
          background: 'rgba(255, 255, 255, 0.02)',
        }}
        styles={{
          root: {
            '&:hover': {
              transform: 'translateY(-2px)',
              boxShadow: '0 4px 12px rgba(0, 0, 0, 0.1)',
              background: 'rgba(255, 255, 255, 0.04)',
            }
          }
        }}
      >
        <Stack gap="xs">
          <Box pos="relative">
            <Image
              src={`${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`}
              style={{
                aspectRatio: "16/9",
                width: "100%",
              }}
              radius="sm"
            />
            {clip.video.length && (
              <Badge
                pos="absolute"
                bottom={4}
                right={4}
                size="xs"
                radius="sm"
                leftSection={<IconClock size={10} />}
                style={{
                  background: 'rgba(0, 0, 0, 0.75)',
                  backdropFilter: 'blur(4px)',
                  color: 'white',
                }}
              >
                {formatDuration(clip.video.length)}
              </Badge>
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
            <Group gap="xs">
              <IconClock size={12} style={{ opacity: 0.6 }} />
              <Text size="xs" c="dimmed">
                {formatDate(clip.createdAt.toString())}
              </Text>
            </Group>
          </Stack>
        </Stack>
      </Card>
    </Link>
  );
}

export function ApexClip({ clipId }: ApexClipProps) {
  const navigate = useNavigate();
  const titleInputRef = useRef<HTMLInputElement>(null);

  // Local state for form inputs
  const [tagsValue, setTagsValue] = useState<Array<string>>([]);
  const [titleValue, setTitleValue] = useState<string>("");

  // Query hooks
  const { data: clip, isLoading: loadingClip } = useClip(clipId);
  const { data: clipOwner, isLoading: loadingClipOwner } = useUserById(clip?.ownerId);
  const { data: topTagsData } = useTopTags();
  const topTags = topTagsData?.map((t) => t.name.toLowerCase()) || [];

  // Related clips - fetch first 50 and filter
  const { data: relatedClipsData, isLoading: loadingRelatedClips } = useApexClips({
    page: 1,
    pageSize: 50,
  });
  const relatedClips = relatedClipsData?.clips.filter(c => c.clipId !== clipId).slice(0, 5) || [];

  // Mutation hooks
  const addTag = useAddTag();
  const removeTag = useRemoveTag();
  const updateTitle = useUpdateClipTitle();
  const deleteClip = useDeleteClip();
  const markAsViewed = useMarkAsViewed();

  // Initialize form values when clip loads
  useEffect(() => {
    if (clip) {
      setTagsValue(clip.tags);
      setTitleValue(clip.video.title);
    }
  }, [clip]);

  // Mark clip as viewed when it loads
  useEffect(() => {
    if (clipId) {
      markAsViewed.mutate(clipId);
    }
  }, [clipId]);

  // Sync tags when they change
  useEffect(() => {
    if (!clip) return;

    const current = new Set(clip.tags);
    const nextLower = tagsValue.map((t) => t.toLowerCase());
    const next = new Set(nextLower);

    const toAdd = [...next].filter((t) => !current.has(t));
    const toRemove = [...current].filter((t) => !next.has(t));

    // Call mutations for each tag change
    toAdd.forEach((tag) => addTag.mutate({ clipId: clip.clipId, tag }));
    toRemove.forEach((tag) => removeTag.mutate({ clipId: clip.clipId, tag }));
  }, [tagsValue, clip?.clipId]);

  function handleSave() {
    if(!clip) {
      return;
    }
    updateTitle.mutate(
      { clipId: clip.clipId, title: titleValue },
      {
        onSuccess: () => {
          titleInputRef.current?.blur();
          notifications.show({
            title: 'Title Changed ✅',
            message: `Title was updated to ${titleValue}`,
          });
        },
        onError: () => {
          notifications.show({
            title: 'Update Failed',
            message: 'Failed to update title',
            color: 'red',
          });
        }
      }
    );
  }

  async function handleDownload() {
    if (!clip) {
      return;
    }

    try {
      await downloadVideo(clip.video.guid);
      notifications.show({
        title: 'Download Started',
        message: 'Your video download has started',
        color: 'green',
      });
    } catch (error) {
      notifications.show({
        title: 'Download Failed',
        message: error instanceof Error ? error.message : 'Failed to download video',
        color: 'red',
      });
    }
  }

  function handleDelete() {
    if (!clip) {
      return;
    }

    modals.openConfirmModal({
      title: 'Delete Video',
      children: (
        <Text size="sm">
          Are you sure you want to delete "{titleValue}"? This action cannot be undone.
        </Text>
      ),
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => {
        deleteClip.mutate(clip.clipId, {
          onSuccess: () => {
            notifications.show({
              title: 'Video Deleted',
              message: 'The video has been successfully deleted',
              color: 'green',
            });
            navigate({ to: '/apex-legends' });
          },
          onError: () => {
            notifications.show({
              title: 'Delete Failed',
              message: 'Failed to delete video',
              color: 'red',
            });
          }
        });
      },
    });
  }

  return (
    <div style={{ height: "calc(100vh - 122px)", display: "flex", gap: "1rem" }}>
      {/* Main Content - Left Side */}
      <Stack style={{ flex: 1, minWidth: 0 }} gap="md">
        {/* Video Player Card */}
        <Card
          radius="xl"
          p={20}
          style={{
            background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
            backdropFilter: 'blur(10px)',
            border: '1px solid rgba(255, 255, 255, 0.1)',
            display: 'flex',
            justifyContent: 'center',
            alignItems: 'center',
          }}
        >
          {!loadingClip && clip && (
            <div style={{
              width: "100%",
              height: "100%",
              alignContent: "center"
            }}>
              <Group h={"100%"} justify={"center"}>
                <iframe
                  src={`https://player.mediadelivery.net/embed/${clip.video.videoLibraryId}/${clip.video.guid}?autoplay=false`}
                  loading="lazy"
                  style={{
                    border: "none",
                    height: "100%",
                    aspectRatio: "16/9",
                    borderRadius: "12px",
                  }}
                  allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture;"
                  allowFullScreen={true}
                />
              </Group>
            </div>
          )}
        </Card>

        {/* Video Info Card */}
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
            {!loadingClip && clip && (
              <TextInput
                ref={titleInputRef}
                size="xl"
                variant="unstyled"
                value={titleValue}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.preventDefault();
                    handleSave();
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
                {!loadingClipOwner && clipOwner && (
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
                {!loadingClipOwner && clipOwner && !loadingClip && clip && (
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
                    onClick={handleDownload}
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
                    onClick={handleDelete}
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
      </Stack>

      {/* Sidebar - Right Side */}
      <Card
        radius="xl"
        p="md"
        style={{
          width: '380px',
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
          flexShrink: 0,
        }}
      >
        <Stack gap="md" h="100%">
          <Text size="lg" fw={600} style={{ letterSpacing: '-0.3px' }}>
            Up Next
          </Text>

          <ScrollArea
            h="100%"
            type="scroll"
            scrollbarSize={8}
            styles={{
              scrollbar: {
                '&:hover': {
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                }
              },
              thumb: {
                backgroundColor: 'rgba(255, 255, 255, 0.2)',
                '&:hover': {
                  backgroundColor: 'rgba(255, 255, 255, 0.3)',
                }
              }
            }}
          >
            <Stack gap="xs">
              {!loadingRelatedClips && relatedClips.map((relatedClip) => (
                <SidebarClipCard key={relatedClip.clipId} clip={relatedClip} />
              ))}
            </Stack>
          </ScrollArea>
        </Stack>
      </Card>
    </div>
  );
}
