# Build stage
FROM oven/bun:alpine AS builder

WORKDIR /app

# Copy workspace config files
COPY bun.lock package.json turbo.json ./

# Copy all package.json files for dependency resolution
COPY apps/home/package.json ./apps/home/
COPY packages/nucleus-api-client/package.json ./packages/nucleus-api-client/
COPY packages/shared/package.json ./packages/shared/
COPY packages/ui/package.json ./packages/ui/
COPY packages/typescript-config/package.json ./packages/typescript-config/
COPY packages/eslint-config/package.json ./packages/eslint-config/

# Install dependencies
RUN bun install --frozen-lockfile

# Copy source files
COPY . .

# Build with turbo (handles dependency ordering)
RUN bun turbo run build --filter=home

# Production stage - serve static files
FROM nginx:alpine AS production

# Copy built assets
COPY --from=builder /app/apps/home/dist /usr/share/nginx/html

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
