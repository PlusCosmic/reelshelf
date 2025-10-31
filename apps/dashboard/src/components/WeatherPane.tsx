import { useWeather } from "../hooks/queries";
import { weatherEmoji } from "../services/weather";
import { Group, Text } from "@mantine/core";

export default function WeatherPane() {
  const { data, isLoading, error } = useWeather();
  const place = data?.cfg?.name ?? "Weather";

  return (
    <div>
      {isLoading && <p>Loading…</p>}
      {!isLoading && error && <p>{String(error)}</p>}
      {!isLoading && data?.data && (
        <>
          <Group gap="xs">
            <Text fw={700}>
              {Math.round(data.data.current?.temperature ?? 0)}°C
            </Text>
            <Text>{weatherEmoji(data.data.current.weathercode)}</Text>
            <Text c="dimmed">{place}</Text>
          </Group>
        </>
      )}
    </div>
  );
}
