import type { WeatherConfig } from "../models/config";
import { tryGetJson } from "./http";

export async function loadJson<T>(path: string): Promise<T | null> {
  return await tryGetJson<T>(path);
}

export async function loadWeatherConfig(): Promise<WeatherConfig | null> {
  return await loadJson<WeatherConfig>("/weather-config.json");
}
