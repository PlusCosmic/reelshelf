# Build stage
FROM oven/bun:1.3.9-alpine AS builder

WORKDIR /app

# Copy workspace config files
COPY bun.lock package.json turbo.json ./

# Copy package manifests for workspace dependency resolution
COPY apps/personal-site/package.json ./apps/personal-site/
COPY packages/clips-api-client/package.json ./packages/clips-api-client/
COPY packages/minecraft-api-client/package.json ./packages/minecraft-api-client/
COPY packages/shared/package.json ./packages/shared/
COPY packages/ui/package.json ./packages/ui/
COPY packages/typescript-config/package.json ./packages/typescript-config/
COPY packages/eslint-config/package.json ./packages/eslint-config/
COPY services/clips/frontend/package.json ./services/clips/frontend/
COPY services/minecraft/frontend/package.json ./services/minecraft/frontend/

# Install dependencies
RUN bun install --frozen-lockfile

# Copy source files
COPY . .

# Build with turbo (handles dependency ordering)
RUN bun turbo run build --filter=minecraft

# Production stage - serve static files
FROM nginx:alpine AS production

# Copy built assets
COPY --from=builder /app/services/minecraft/frontend/dist /usr/share/nginx/html

# Copy nginx config for SPA routing
RUN echo 'server { \
    listen 80; \
    root /usr/share/nginx/html; \
    index index.html; \
    location / { \
    try_files $uri $uri/ /index.html; \
    } \
    }' > /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
