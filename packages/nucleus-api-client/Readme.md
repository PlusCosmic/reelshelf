## Code Generation Instructions
- First clear the src directory in order to have a clean start.
- Then from this directory run: 	
```
npx openapi-generator-cli generate -i Nucleus.json -g typescript-fetch -o src  --additional-properties=supportsES6=true
```
- and then
```
pnpm run build
```