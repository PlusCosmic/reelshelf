// This configuration is used for the openapi-typescript generated API client
export const apiConfig = {
  // Optional API origin/base for split frontend/backend deployments.
  // Generated clients already include the API route prefix in their paths.
  baseUrl: import.meta.env.VITE_API_BASE_URL || "",
};
