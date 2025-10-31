import { useQuery } from "@tanstack/react-query";
import { loadWeatherConfig } from "../services/config";
import { getWeather } from "../services/weather";

export function useWeather() {
  return useQuery({
    queryKey: ["weather"],
    queryFn: async () => {
      const cfg = await loadWeatherConfig();
      const data = await getWeather(cfg);
      return { cfg, data } as const;
    },
    staleTime: 5 * 60_000,
  });
}
