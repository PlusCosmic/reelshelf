import { getJson, withQuery } from "@repo/shared";
import type { WeatherConfig } from "../models/config";
import type { OpenMeteoResponse, WeatherResponse } from "../models/weather";

export async function getWeather(
  cfg: WeatherConfig | null,
): Promise<WeatherResponse | null> {
  if (!cfg) return null;
  const base = "https://api.open-meteo.com/v1/forecast";
  const url = withQuery(base, {
    latitude: cfg.latitude,
    longitude: cfg.longitude,
    timezone: cfg.timezone,
    current: "temperature_2m,wind_speed_10m,wind_direction_10m,weather_code",
    daily: "temperature_2m_max,temperature_2m_min,precipitation_sum",
  });
  try {
    // Use the proper interface instead of any
    const data = await getJson<OpenMeteoResponse>(url, { timeoutMs: 15000 });
    // Provide a minimal adapter now
    const currentProper = data.current ?? {};
    const currentBackup = data.current_weather ?? {};
    return {
      latitude: data.latitude,
      longitude: data.longitude,
      timezone: data.timezone ?? cfg.timezone,
      current: {
        temperature:
          currentProper.temperature_2m ?? currentBackup.temperature ?? 0,
        windspeed: currentProper.wind_speed_10m ?? currentBackup.windspeed ?? 0,
        winddirection:
          currentProper.wind_direction_10m ?? currentBackup.winddirection ?? 0,
        weathercode:
          currentProper.weather_code ?? currentBackup.weathercode ?? 0,
        time: currentProper.time ?? new Date().toISOString(),
      },
      daily: data.daily ?? {
        time: [],
        temperature_2m_max: [],
        temperature_2m_min: [],
        precipitation_sum: [],
      },
    };
  } catch {
    return null;
  }
}

export function weatherEmoji(code: number): string {
  switch (code) {
    case 0:
      return "☀️";
    case 1:
      return "🌤️";
    case 2:
      return "⛅";
    case 3:
      return "☁️";
    case 45:
    case 48:
      return "🌫️";
    case 51:
    case 53:
    case 55:
      return "🌦️";
    case 56:
    case 57:
      return "🌧️";
    case 61:
    case 63:
    case 65:
      return "🌧️";
    case 66:
    case 67:
      return "🌧️";
    case 71:
    case 73:
    case 75:
      return "🌨️";
    case 77:
      return "🌨️";
    case 80:
    case 81:
    case 82:
      return "🌦️";
    case 85:
    case 86:
      return "🌨️";
    case 95:
      return "⛈️";
    case 96:
    case 99:
      return "⛈️";
    default:
      return "🌡️";
  }
}
