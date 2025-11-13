import {
  Box,
  Card,
  Center,
  Group,
  Image,
  RingProgress,
  Stack,
  Tabs,
  Text,
  Transition,
} from "@mantine/core";
import { useHover, useMediaQuery } from "@mantine/hooks";
import { useApexMapRotation } from "../../hooks/queries";
import { IconClock, IconTrophy } from "@tabler/icons-react";

export default function ApexLegendsMaps() {
  const { data: mapRotation, isLoading } = useApexMapRotation();
  const isMobile = useMediaQuery("(max-width: 768px)");
  const { hovered, ref } = useHover();

  const formatRemainingTime = (mins: number): string => {
    const hours = Math.floor(mins / 60);
    const minutes = mins % 60;
    return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
  };

  const calculateProgress = (remainingMins: number, totalMins: number = 90) => {
    return (remainingMins / totalMins) * 100;
  };

  if (isLoading) {
    return (
      <Card
        shadow="lg"
        padding="xl"
        radius="xl"
        style={{
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
        }}
      >
        <Center h={300}>
          <Text c="dimmed">Loading map rotation...</Text>
        </Center>
      </Card>
    );
  }

  if (!mapRotation) return null;

  return (
    <Box ref={ref} pos="relative" style={{ width: 120 }}>
      {/* Compact view - always visible */}
      <Card
        w={120}
        shadow="lg"
        padding="xs"
        radius="xl"
        style={{
          background: 'linear-gradient(135deg, rgba(var(--mantine-color-red-9-rgb), 0.08) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(var(--mantine-color-red-5-rgb), 0.2)',
          cursor: 'pointer',
          opacity: hovered ? 0 : 1,
          transition: 'opacity 0.2s ease',
        }}
      >
        <Stack gap="xs" align="center">
          <RingProgress
            size={90}
            thickness={5}
            sections={[
              {
                value: calculateProgress(mapRotation.ranked.current.remainingMins, 1440),
                color: 'red',
              },
            ]}
            label={
              <Center>
                <Image
                  src={mapRotation.ranked.current.imageUrl}
                  alt={mapRotation.ranked.current.map}
                  radius="50%"
                  h={68}
                  w={68}
                  style={{
                    border: '2px solid rgba(var(--mantine-color-red-5-rgb), 0.3)',
                  }}
                />
              </Center>
            }
          />
          <Stack gap={0} align="center">
            <Text size="xs" fw={600} style={{ lineHeight: 1 }}>
              {mapRotation.ranked.current.map}
            </Text>
            <Text size="xs" c="dimmed" style={{ lineHeight: 1.2 }}>
              {formatRemainingTime(mapRotation.ranked.current.remainingMins)}
            </Text>
          </Stack>
        </Stack>
      </Card>

      {/* Expanded view - overlays on hover */}
      <Transition
        mounted={hovered}
        transition="pop-bottom-left"
        duration={300}
        timingFunction="ease"
      >
        {(styles) => (
          <Card
            w={300}
            shadow="xl"
            padding="sm"
            radius="xl"
            style={{
              ...styles,
              position: 'absolute',
              bottom: 0,
              left: 0,
              background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
              backdropFilter: 'blur(10px)',
              border: '1px solid rgba(255, 255, 255, 0.1)',
              maxWidth: isMobile ? '100%' : 420,
              zIndex: 1000,
            }}
          >
      <Tabs defaultValue="ranked" variant="pills" radius="xl">
        <Tabs.List grow mb="lg">
          <Tabs.Tab
            value="standard"
            leftSection={<IconClock size={16} />}
            style={{
              '&[data-active]': {
                background: 'linear-gradient(135deg, rgba(var(--mantine-color-cyan-9-rgb), 0.2) 0%, rgba(var(--mantine-color-cyan-9-rgb), 0.1) 100%)',
              }
            }}
          >
            Standard
          </Tabs.Tab>
          <Tabs.Tab
            value="ranked"
            leftSection={<IconTrophy size={16} />}
            style={{
              '&[data-active]': {
                background: 'linear-gradient(135deg, rgba(var(--mantine-color-red-9-rgb), 0.2) 0%, rgba(var(--mantine-color-red-9-rgb), 0.1) 100%)',
              }
            }}
          >
            Ranked
          </Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="standard">
          <MapRotationContent
            color="cyan"
            current={mapRotation.standard.current}
            next={mapRotation.standard.next}
            formatRemainingTime={formatRemainingTime}
            calculateProgress={calculateProgress}
            totalMins={90}
            isMobile={isMobile}
          />
        </Tabs.Panel>

        <Tabs.Panel value="ranked">
          <MapRotationContent
            color="red"
            current={mapRotation.ranked.current}
            next={mapRotation.ranked.next}
            formatRemainingTime={formatRemainingTime}
            calculateProgress={calculateProgress}
            totalMins={1440}
            isMobile={isMobile}
          />
        </Tabs.Panel>
      </Tabs>

            {/* Data Attribution */}
            <Center mt="md" pt="md" style={{ borderTop: '1px solid rgba(255, 255, 255, 0.08)' }}>
              <Text size="xs" c="dimmed">
                Data from{" "}
                <Text
                  component="a"
                  href="https://apexlegendsstatus.com/"
                  target="_blank"
                  size="xs"
                  style={{ textDecoration: 'none' }}
                >
                  Apex Legends Status
                </Text>
              </Text>
            </Center>
          </Card>
        )}
      </Transition>
    </Box>
  );
}

interface MapRotationContentProps {
  color: string;
  current: {
    map: string;
    imageUrl: string;
    end: string;
    remainingMins: number;
  };
  next: {
    map: string;
    imageUrl: string;
    start: string;
  };
  formatRemainingTime: (mins: number) => string;
  calculateProgress: (remainingMins: number, totalMins?: number) => number;
  totalMins: number;
  isMobile?: boolean;
}

function MapRotationContent({
  color,
  current,
  next,
  formatRemainingTime,
  calculateProgress,
  totalMins,
  isMobile,
}: MapRotationContentProps) {
  return (
    <Stack gap="lg">
      {/* Current Map */}
      <Transition
        mounted={true}
        transition="fade"
        duration={400}
        timingFunction="ease"
      >
        {(styles) => (
          <Box style={styles}>
            <Text size="xs" c="dimmed" mb="xs" fw={600} tt="uppercase">
              Current Map
            </Text>
            <Group gap="md" wrap="nowrap">
              <Box pos="relative">
                <RingProgress
                  size={isMobile ? 100 : 120}
                  thickness={6}
                  sections={[
                    {
                      value: calculateProgress(current.remainingMins, totalMins),
                      color: color,
                    },
                  ]}
                  label={
                    <Center>
                      <Image
                        src={current.imageUrl}
                        alt={current.map}
                        radius="50%"
                        h={isMobile ? 75 : 90}
                        w={isMobile ? 75 : 90}
                        style={{
                          border: `2px solid rgba(var(--mantine-color-${color}-5-rgb), 0.3)`,
                        }}
                      />
                    </Center>
                  }
                />
              </Box>
              <Stack gap={4} style={{ flex: 1 }}>
                <Text fw={700} size={isMobile ? "md" : "lg"}>
                  {current.map}
                </Text>
                <Group gap="xs">
                  <IconClock size={14} color="var(--mantine-color-dimmed)" />
                  <Text size="xs" c="dimmed">
                    {formatRemainingTime(current.remainingMins)} left
                  </Text>
                </Group>
                <Text size="xs" c="dimmed">
                  Until{" "}
                  {new Date(current.end).toLocaleTimeString([], {
                    hour: "2-digit",
                    minute: "2-digit",
                    hour12: false,
                  })}
                </Text>
              </Stack>
            </Group>
          </Box>
        )}
      </Transition>

      {/* Divider */}
      <Box
        h={1}
        style={{
          background: `linear-gradient(90deg,
            transparent 0%,
            rgba(var(--mantine-color-${color}-5-rgb), 0.3) 50%,
            transparent 100%)`,
        }}
      />

      {/* Next Map */}
      <Box>
        <Text size="xs" c="dimmed" mb="xs" fw={600} tt="uppercase">
          Next Map
        </Text>
        <Group gap="md" wrap="nowrap">
          <Image
            src={next.imageUrl}
            alt={next.map}
            h={isMobile ? 55 : 65}
            w={isMobile ? 73 : 87}
            radius="md"
            style={{
              border: `1px solid rgba(var(--mantine-color-${color}-5-rgb), 0.2)`,
            }}
          />
          <Stack gap={4} style={{ flex: 1 }}>
            <Text fw={600} size="sm">
              {next.map}
            </Text>
            <Text size="xs" c="dimmed">
              Starts at{" "}
              {new Date(next.start).toLocaleTimeString([], {
                hour: "2-digit",
                minute: "2-digit",
                hour12: false,
              })}
            </Text>
          </Stack>
        </Group>
      </Box>
    </Stack>
  );
}
