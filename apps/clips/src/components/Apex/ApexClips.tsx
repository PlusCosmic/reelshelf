import { useEffect, useRef, useState } from "react";
import { apiConfig, fetchApexClips, fetchMe } from "@repo/shared";
import {
  Card,
  Center,
  Divider,
  Group,
  Image,
  Pagination,
  Space,
  Stack,
  Text,
  Title,
  UnstyledButton,
} from "@mantine/core";
import { Link } from "@tanstack/react-router";
import { VideoUpload } from "../VideoUpload.tsx";
import classes from "./ApexClips.module.scss";
import type { Clip, DiscordUser } from "@repo/nucleus-api-client";

export function ApexClips() {
  const [user, setUser] = useState<DiscordUser | null>(null);
  const [loadingUser, setLoadingUser] = useState(true);
  const [clips, setClips] = useState<Array<Clip>>([]);
  const [loadingClips, setLoadingClips] = useState(true);
  const [hoveredGuid, setHoveredGuid] = useState<string | null>(null);
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(10);
  const [totalPages, setTotalPages] = useState<number>(1);
  const containerRef = useRef<HTMLDivElement>(null);

  // Calculate page size based on available screen space
  useEffect(() => {
    const calculatePageSize = () => {
      if (!containerRef.current) {
        console.log('Container ref not available');
        return;
      }

      const containerWidth = containerRef.current.offsetWidth;
      const containerHeight = containerRef.current.offsetHeight;

      console.log('Attempting calculation with dimensions:', containerWidth, containerHeight);

      // Skip calculation if container hasn't been rendered yet
      if (containerWidth === 0 || containerHeight === 0) {
        console.log('Container has zero dimensions, skipping calculation');
        return;
      }

      // Card dimensions: 320px width + margins, 240px height + margins
      const cardWidth = 352 + 16; // 16px gap between cards
      const cardHeight = 240 + 16;

      const itemsPerRow = Math.floor(containerWidth / cardWidth);
      const rows = Math.floor(containerHeight / cardHeight);
      const calculatedPageSize = Math.max(itemsPerRow * rows, 1); // At least 1 item
      console.log(`Calculated page size: ${calculatedPageSize}`)
      console.log(`Bounding Box Size (w*h): ${containerWidth} x ${containerHeight}`)
      setPageSize(calculatedPageSize);
    };

    // Small delay to ensure DOM is fully rendered
    const timeoutId = setTimeout(calculatePageSize, 0);

    // Use ResizeObserver to detect when container is rendered and sized
    const resizeObserver = new ResizeObserver(() => {
      console.log('ResizeObserver triggered');
      calculatePageSize();
    });

    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }

    // Also keep window resize listener as fallback
    window.addEventListener('resize', calculatePageSize);

    return () => {
      clearTimeout(timeoutId);
      resizeObserver.disconnect();
      window.removeEventListener('resize', calculatePageSize);
    };
  }, [clips]);

  useEffect(() => {
    setLoadingUser(true);
    (async () => {
      try {
        const me = await fetchMe();
        setUser(me);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingUser(false);
      }
    })();
  }, []);

  useEffect(() => {
    (async () => {
      if (!user) {
        setClips([]);
        return;
      }
      setLoadingClips(true);
      try {
        const xs = await fetchApexClips(page, pageSize);
        if (!xs) return;
        setTotalPages(xs.totalPages);
        setClips(xs.clips);
      } catch (e) {
        console.error(e);
      } finally {
        setLoadingClips(false);
      }
    })();
  }, [user, page, pageSize]);

  const items = clips.map((clip) => (
    <Card className={classes.item} key={clip.video.title}>
      <Link to={`/apex-legends/$clipId`} params={{
        clipId: clip.clipId,
      }}>
        <UnstyledButton
          onMouseEnter={() => setHoveredGuid(clip.clipId)}
          onMouseLeave={() => setHoveredGuid(null)}
        >
          <Center className={classes.thumb}>
            <div className={classes["thumb-wrap"]}>
              <Image
                src={
                  hoveredGuid === clip.clipId
                    ? `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/preview.webp`
                    : `${apiConfig.bunnyBaseUrl}/${clip.video.guid}/thumbnail.jpg`
                }
                width={320}
                height={180}
              />
            </div>
          </Center>
          <Stack>
            <Text c={"dimmed"} size={"xs"} mt={7}>
              {clip.video.title}
            </Text>
          </Stack>
        </UnstyledButton>
      </Link>
    </Card>
  ));

  const isLoading = loadingUser || loadingClips;
  return (
    <div style={{ height: "calc(100vh - 122px)" }}>
      <Stack align="stretch" justify="space-between" h="100%">
        <div>
          <Group justify="space-between">
            <Title>Apex Legends Clips</Title>
            <VideoUpload />
          </Group>
        </div>
        <div style={{ width: "100%", height: "100%" }}>
          <Divider />
          <Space h="md" />
          {isLoading && <Text>Loading categories..</Text>}
          {!isLoading && clips.length > 0 && (
            <div style={{ width: "100%", height: "100%" }}>
              <Group ref={containerRef} h={"100%"} justify={"space-evenly"} align={"stretch"}>
                {items.map((item) => (
                  <div key={item.key}>{item}</div>
                ))}
              </Group>
            </div>
          )}
          <Space h="md" />
        </div>
        <div style={{ width: "100%" }}>
          <Divider />
          <Space h="md" />
          <Center>
            <Pagination total={totalPages} value={page} onChange={setPage} />
          </Center>
        </div>
      </Stack>
    </div>
  );
}
