// This configuration is used for the openapi-typescript generated API client
export const apiConfig = {
  // Use relative URL for same-origin API calls (frontend + backend served together)
  baseUrl: import.meta.env.VITE_API_BASE_URL || "/api",
  bunnyBaseUrl: "https://vz-cd8f9809-39a.b-cdn.net"
};