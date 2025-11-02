import { useEffect, useRef, useState } from "react";
import {
  addTagToVideo,
  deleteVideo,
  fetchUser,
  getTopTags,
  getVideo,
  markClipAsViewed,
  removeTagFromVideo,
  updateVideoTitle
} from "@repo/shared";
import {
  Avatar,
  Button,
  Card,
  Group,
  Stack,
  TagsInput,
  Text,
  TextInput,
} from "@mantine/core";
import { IconDownload, IconTrash } from "@tabler/icons-react";
import { notifications } from '@mantine/notifications';
import { modals } from '@mantine/modals';
import { useNavigate } from '@tanstack/react-router';
import type { Clip, DiscordUser } from "@repo/nucleus-api-client";

interface ApexClipProps {
  clipId: string;
}

export function ApexClip({ clipId }: ApexClipProps) {
  const navigate = useNavigate();
  const [clipOwner, setClipOwner] = useState<DiscordUser | null>(null);
  const [loadingClipOwner, setLoadingClipOwner] = useState(true);
  const [clip, setClip] = useState<Clip | null>(null);
  const [loadingClip, setLoadingClip] = useState(true);
  const [tagsValue, setTagsValue] = useState<Array<string>>([]);
  const [titleValue, setTitleValue] = useState<string>("");
  const [topTags, setTopTags] = useState<Array<string>>([]);
  const titleInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    (async () => {
      try {
        const tags = await getTopTags();
        setTopTags(tags.map((t) => t.name.toLowerCase()));
      } catch (e) {
        console.error(e);
      } finally {
      }
    })();
  }, []);

  useEffect(() => {
    setLoadingClip(true);
    (async () => {
      try {
        const fetchedClip = await getVideo(clipId);
        setClip(fetchedClip);
        if (!fetchedClip) return;
        setTagsValue(fetchedClip.tags)
        setTitleValue(fetchedClip.video.title)

        // Mark clip as viewed
        await markClipAsViewed(clipId);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingClip(false);
      }
    })();
  }, [clipId]);

  useEffect(() => {
    setLoadingClipOwner(true);
    (async () => {
      if (!clip) {
        return;
      }
      try {
        const user = await fetchUser(clip.ownerId);
        setClipOwner(user);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingClipOwner(false);
      }
    })();
  }, [clip]);

  useEffect(() => {
    (async () => {
      if (!clip) return;

      const current = new Set(clip.tags);
      const nextLower = (tagsValue).map((t) => t.toLowerCase());
      const next = new Set(nextLower);

      const toAdd = [...next].filter((t) => !current.has(t));
      const toRemove = [...current].filter((t) => !next.has(t));

      try {
        await Promise.all([
          ...toAdd.map((t) => addTagToVideo(clip.clipId, t)),
          ...toRemove.map((t) => removeTagFromVideo(clip.clipId, t)),
        ]);

        setClip((prev) => {
          if (!prev) return prev;
          const updated = new Set(prev.tags);
          toAdd.forEach((t) => updated.add(t));
          toRemove.forEach((t) => updated.delete(t));
          return { ...prev, tags: Array.from(updated) };
        });

      } catch (e) {
        console.error(e);
      }
    })();
  }, [tagsValue]);

  async function handleSave() {
    if(!clip) {
      return;
    }
    await updateVideoTitle(clip.clipId, titleValue);
    titleInputRef.current?.blur();
    notifications.show({
      title: 'Title Changed ✅',
      message: `Title was updated to ${titleValue}`,
    })
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
      onConfirm: async () => {
        try {
          await deleteVideo(clip.clipId);
          notifications.show({
            title: 'Video Deleted',
            message: 'The video has been successfully deleted',
            color: 'green',
          });
          navigate({ to: '/apex-legends' });
        } catch (error) {
          notifications.show({
            title: 'Delete Failed',
            message: error instanceof Error ? error.message : 'Failed to delete video',
            color: 'red',
          });
        }
      },
    });
  }

  return (
    <Group>
      <Card radius="lg">
        <Stack>
          {!loadingClip && clip && (
            <div
              style={{
                position: "relative",
                paddingTop: "56.25%",
                height: "70vh",
                width: "70vw",
                overflow: "hidden",
                borderRadius: "10px",
              }}
            >
              <iframe
                src={`https://player.mediadelivery.net/embed/${clip.video.videoLibraryId}/${clip.video.guid}?autoplay=false`}
                loading="lazy"
                style={{
                  border: "none",
                  position: "absolute",
                  top: "0",
                  height: "100%",
                  width: "100%",
                }}
                allow="accelerometer; gyroscope; autoplay; encrypted-media; picture-in-picture;"
                allowFullScreen={true}
              ></iframe>
            </div>
          )}
          <Group justify={"space-between"}>
            <Stack>
              {!loadingClip && clip && (
                <TextInput
                  ref={titleInputRef}
                  size={"xl"}
                  w={"400px"}
                  variant={"unstyled"}
                  value={titleValue}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") {
                      event.preventDefault();
                      handleSave();
                    }
                  }}
                  onChange={(event) => setTitleValue(event.currentTarget.value)}
                />
              )}
              <Group>
                <div>
                  {!loadingClipOwner && clipOwner && (
                    <div>
                      {clipOwner.avatar && (
                        <Avatar
                          style={{ cursor: "pointer" }}
                          src={clipOwner.avatar}
                          radius="xl"
                        />
                      )}
                      {!clipOwner.avatar && (
                        <Avatar
                          style={{ cursor: "pointer" }}
                          variant="filled"
                          radius="xl"
                          color="green"
                        />
                      )}
                    </div>
                  )}
                </div>
                <div>
                  {!loadingClipOwner && clipOwner && !loadingClip && clip && (
                    <Stack align={"stretch"} justify={"flex-start"} gap={"xs"}>
                      <Text c={"dimmed"} size={"sm"}>
                        Uploaded by {clipOwner.username}
                      </Text>
                      <Text c={"dimmed"} size={"sm"}>
                        {clip.video.dateUploaded.toDateString()}
                      </Text>
                    </Stack>
                  )}
                </div>
              </Group>
            </Stack>
            <Group align={"flex-end"}>
              <div style={{height:"100%"}}>
                <Stack justify={"flex-start"} h={"100%"}>
                  <TagsInput data={topTags} value={tagsValue} onChange={setTagsValue} maxTags={5} />
                </Stack>
              </div>
              <Stack>
                <Button
                  style={{
                    background: "var(--mantine-color-red-9)",
                    hover: "var(--mantine-color-red-8)",
                    color: "var(--mantine-color-white)",
                  }}
                  leftSection={<IconTrash />}
                  onClick={handleDelete}
                >
                  Delete
                </Button>
                <Button leftSection={<IconDownload />}>Download</Button>
              </Stack>
            </Group>
          </Group>
        </Stack>
      </Card>
    </Group>
  );
}
