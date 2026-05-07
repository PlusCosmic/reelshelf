# API Client Pipeline

The repo generates a TypeScript API client for the Clips backend service.

## Packages

- `@repo/clips-api-client`
  - OpenAPI document: `packages/clips-api-client/Nucleus.Clips.json`
  - Generated source: `packages/clips-api-client/apis`, `packages/clips-api-client/models`, `packages/clips-api-client/runtime.ts`, `packages/clips-api-client/index.ts`
  - Consumer: `services/clips/frontend`

The generated client files are committed. Do not hand-edit generated files; update the backend API and run the generation command instead.

## Commands

- Generate Clips OpenAPI: `bun run generate:openapi:clips`
- Generate and build the client: `bun run generate:api-clients`
- Check for stale generated output: `bun run check:api-client-drift`

`@repo/shared` owns cross-service frontend helpers such as auth/user helpers, API factories, and normalized API error handling. Service-specific wrappers should import the matching generated client package.
