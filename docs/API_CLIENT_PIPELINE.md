# API Client Pipeline

The Reelshelf frontend uses a committed TypeScript client generated from the Reelshelf API OpenAPI document.

- OpenAPI document: `frontend/src/api-client/Reelshelf.json`
- Generated source: `frontend/src/api-client/apis`, `frontend/src/api-client/models`, `frontend/src/api-client/runtime.ts`, `frontend/src/api-client/index.ts`
- Consumer: `frontend`

Commands:

```sh
bun run generate:openapi:reelshelf
bun run generate:api-client
bun run check:api-client-drift
```

Do not hand-edit generated files; update the backend API and run the generation command instead.
