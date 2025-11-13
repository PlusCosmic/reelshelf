export interface WeatherConfig {
  name: string;
  latitude: number;
  longitude: number;
  timezone: string;
}

export interface FrequentLink {
  title: string;
  url: string;
  thumbnail?: string;
}
