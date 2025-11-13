export interface WeatherCurrent {
  temperature: number;
  windspeed: number;
  winddirection: number;
  weathercode: number;
  time: string; // ISO
}

export interface WeatherDaily {
  time: string[];
  temperature_2m_max: number[];
  temperature_2m_min: number[];
  precipitation_sum: number[];
}

export interface WeatherResponse {
  latitude: number;
  longitude: number;
  timezone: string;
  current: WeatherCurrent;
  daily: WeatherDaily;
}

export interface OpenMeteoResponse {
  latitude: number;
  longitude: number;
  timezone?: string;
  current?: {
    temperature_2m?: number;
    temperature?: number;
    wind_speed_10m?: number;
    windspeed?: number;
    wind_direction_10m?: number;
    winddirection?: number;
    weather_code?: number;
    weathercode?: number;
    time?: string;
  };
  current_weather?: {
    temperature?: number;
    windspeed?: number;
    winddirection?: number;
    weathercode?: number;
    time?: string;
  };
  daily?: {
    time: string[];
    temperature_2m_max: number[];
    temperature_2m_min: number[];
    precipitation_sum: number[];
  };
}
