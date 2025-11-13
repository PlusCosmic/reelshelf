import { useWeather } from "../hooks/queries";
import { weatherEmoji } from "../services/weather";
import { Card, Group, Skeleton, Stack, Text } from "@mantine/core";

export default function WeatherPane() {
  const { data, isLoading, error } = useWeather();
  const place = data?.cfg?.name ?? "Weather";

  return (
    <Card
      radius="lg"
      p="md"
      style={{
        background: 'linear-gradient(135deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.02) 100%)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(255, 255, 255, 0.1)',
        minWidth: '200px',
      }}
    >
      {isLoading && (
        <Stack gap="xs">
          <Skeleton height={24} radius="md" />
          <Skeleton height={16} radius="md" width="60%" />
        </Stack>
      )}
      {!isLoading && error && (
        <Text c="red" size="sm">Failed to load weather</Text>
      )}
      {!isLoading && data?.data && (
        <Group gap="md" wrap="nowrap">
          <Text size="2rem" style={{ lineHeight: 1 }}>
            {weatherEmoji(data.data.current.weathercode)}
          </Text>
          <Stack gap={4}>
            <Text fw={700} size="xl" style={{ lineHeight: 1 }}>
              {Math.round(data.data.current?.temperature ?? 0)}°C
            </Text>
            <Text c="dimmed" size="sm">
              {place}
            </Text>
          </Stack>
        </Group>
      )}
    </Card>
  );
}
