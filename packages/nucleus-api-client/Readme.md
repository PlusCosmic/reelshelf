## Code Generation Instructions
- From the monorepo root, refresh the API specs and regenerate the client:
```
bun run generate:api-client
```
- Or from this package, regenerate from the existing `Nucleus.json` snapshot:
```
bun run generate
bun run build
```
