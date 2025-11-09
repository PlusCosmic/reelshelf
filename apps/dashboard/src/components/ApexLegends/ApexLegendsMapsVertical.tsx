import {
  Affix,
  Anchor,
  Card,
  Center,
  Group,
  Image,
  Skeleton,
  Stack,
  Text,
  ThemeIcon,
} from "@mantine/core";
import { useApexMapRotation } from "../../hooks/queries";

export default function ApexLegendsMapsVertical() {
  const { data: mapRotation, isLoading, error } = useApexMapRotation();

  // Format remaining time in a more readable format
  const formatRemainingTime = (mins: number): string => {
    const hours = Math.floor(mins / 60);
    const minutes = mins % 60;
    return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
  };

  return (
    <Affix position={{ bottom: 20, left: 20 }}>
      <Card
        shadow="lg"
        radius="xl"
        padding="lg"
        miw="280"
        style={{
          background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
        }}
      >
        {isLoading && <Text>Loading…</Text>}
        {!isLoading && error && <Text c="red">Failed to load Apex Legends map data</Text>}
        <Center mb="sm">
          <Group>
            <Image
              src="https://drop-assets.ea.com/images/F1GeiHWipvvKj7GtUVP3U/31bb122451e2dea6d14c9b497f8e09d4/apex-white-nav-logo.svg"
              alt="Apex Legends"
              h={20}
              w="auto"
            />
            <Text fw={600}>Map Rotation</Text>
          </Group>
        </Center>
        <Text c="dimmed" fw={600} mb="sm">
          Battle Royale - Standard
        </Text>
        <Group justify="space-between" mb="xs">
          <Text c="dimmed" size="sm">
            Current Map
          </Text>
          {!isLoading && mapRotation && (
            <Text size="sm" c="dimmed">
              {formatRemainingTime(mapRotation.standard.current.remainingMins)}{" "}
              remaining
            </Text>
          )}
          {isLoading && <Skeleton height={8} radius="xl" />}
        </Group>
        <Group justify="flex-start" mb="xs">
          {!isLoading && mapRotation && (
            <Image
              src={mapRotation.standard.current.imageUrl}
              alt={mapRotation.standard.current.map}
              h={60}
              w={80}
              radius="md"
            />
          )}
          {isLoading && <Skeleton h={60} w={80} radius="md" />}
          {!isLoading && mapRotation && (
            <Stack justify="center" gap={0}>
              <Text fw={600}>{mapRotation.standard.current.map}</Text>
              <Text c="dimmed" size="xs">
                Until{" "}
                {new Date(mapRotation.standard.current.end).toLocaleTimeString(
                  [],
                  {
                    hour: "2-digit",
                    minute: "2-digit",
                    hour12: false,
                  },
                )}
              </Text>
            </Stack>
          )}
          {isLoading && (
            <Stack>
              <Skeleton height={8} radius="xl" />
              <Skeleton height={8} radius="xl" />
            </Stack>
          )}
        </Group>
        <Text size="sm" c="dimmed" mb="xs">
          Next Map
        </Text>
        <Group justify="flex-start" mb="xs">
          {!isLoading && mapRotation && (
            <Image
              src={mapRotation.standard.next.imageUrl}
              alt={mapRotation.standard.next.map}
              h={45}
              w={60}
              radius="md"
            />
          )}
          {isLoading && <Skeleton h={45} w={60} radius="md" />}
          {!isLoading && mapRotation && (
            <Stack justify="center" gap={0}>
              <Text fw={600} size="sm">
                {mapRotation.standard.next.map}
              </Text>
              <Text c="dimmed" size="xs">
                Starts at{" "}
                {new Date(mapRotation.standard.next.start).toLocaleTimeString(
                  [],
                  {
                    hour: "2-digit",
                    minute: "2-digit",
                    hour12: false,
                  },
                )}
              </Text>
            </Stack>
          )}
          {isLoading && (
            <Stack>
              <Skeleton height={8} radius="xl" />
              <Skeleton height={8} radius="xl" />
            </Stack>
          )}
        </Group>
        <Text fw={600} c="dimmed" mb="sm">
          Battle Royale - Ranked
        </Text>
        <Group mb="xs" justify="space-between">
          <Text c="dimmed" size="sm">
            Current Map
          </Text>
          {!isLoading && mapRotation && (
            <Text c="dimmed" size="sm">
              {formatRemainingTime(mapRotation.ranked.current.remainingMins)}{" "}
              remaining
            </Text>
          )}
          {isLoading && <Skeleton height={8} radius="xl" />}
        </Group>
        <Group mb="xs" justify="flex-start">
          {!isLoading && mapRotation && (
            <Image
              src={mapRotation.ranked.current.imageUrl}
              alt={mapRotation.ranked.current.map}
              h={60}
              w={80}
              radius="md"
            />
          )}
          {isLoading && <Skeleton h={60} w={80} radius="md" />}
          {!isLoading && mapRotation && (
            <Stack justify="center" gap={0}>
              <Text fw={600}>{mapRotation.ranked.current.map}</Text>
              <Text c="dimmed" size="xs">
                Until{" "}
                {new Date(mapRotation.ranked.current.end).toLocaleTimeString(
                  [],
                  {
                    hour: "2-digit",
                    minute: "2-digit",
                    hour12: false,
                  },
                )}
              </Text>
            </Stack>
          )}
          {isLoading && (
            <Stack>
              <Skeleton height={8} radius="xl" />
              <Skeleton height={8} radius="xl" />
            </Stack>
          )}
        </Group>
        <Text mb="xs" size="sm" c="dimmed">
          Next Map
        </Text>
        <Group mb="xs" justify="flex-start">
          {!isLoading && mapRotation && (
            <Image
              src={mapRotation.ranked.next.imageUrl}
              alt={mapRotation.ranked.next.map}
              h={45}
              w={60}
              radius="md"
            />
          )}
          {isLoading && <Skeleton h={45} w={60} radius="md" />}
          {!isLoading && mapRotation && (
            <Stack justify="center" gap={0}>
              <Text size="sm" fw={600}>
                {mapRotation.ranked.next.map}
              </Text>
              <Text c="dimmed" size="xs">
                Starts at{" "}
                {new Date(mapRotation.ranked.next.start).toLocaleTimeString(
                  [],
                  {
                    hour: "2-digit",
                    minute: "2-digit",
                    hour12: false,
                  },
                )}
              </Text>
            </Stack>
          )}
          {isLoading && (
            <Stack>
              <Skeleton height={8} radius="xl" />
              <Skeleton height={8} radius="xl" />
            </Stack>
          )}
        </Group>
        <Card.Section
          withBorder
          style={{
            borderTop: '1px solid rgba(255, 255, 255, 0.08)',
          }}
        >
          <Center>
            <Anchor
              size="xs"
              href="https://apexlegendsstatus.com/"
              target="_blank"
              mt={4}
              mb={4}
              c="dimmed"
              style={{
                transition: 'color 0.2s ease',
                '&:hover': {
                  color: 'var(--mantine-color-white)',
                }
              }}
            >
              Data provided by Apex Legends Status
              <ThemeIcon color="transparent">
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="10"
                  height="10"
                  fill="currentColor"
                  className="ms-1"
                  viewBox="0 0 16 16"
                >
                  <path
                    fillRule="evenodd"
                    d="M8.636 3.5a.5.5 0 0 0-.5-.5H1.5A1.5 1.5 0 0 0 0 4.5v10A1.5 1.5 0 0 0 1.5 16h10a1.5 1.5 0 0 0 1.5-1.5V7.864a.5.5 0 0 0-1 0V14.5a.5.5 0 0 1-.5.5h-10a.5.5 0 0 1-.5-.5v-10a.5.5 0 0 1 .5-.5h6.636a.5.5 0 0 0 .5-.5z"
                  />
                  <path
                    fillRule="evenodd"
                    d="M16 .5a.5.5 0 0 0-.5-.5h-5a.5.5 0 0 0 0 1h3.793L6.146 9.146a.5.5 0 1 0 .708.708L15 1.707V5.5a.5.5 0 0 0 1 0v-5z"
                  />
                </svg>
              </ThemeIcon>
            </Anchor>
          </Center>
        </Card.Section>
      </Card>
    </Affix>
  );
}
