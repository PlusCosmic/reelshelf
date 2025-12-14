# Build stage
FROM node:22-alpine AS builder

# Install pnpm
RUN corepack enable && corepack prepare pnpm@latest --activate

WORKDIR /app

# Copy workspace config files
COPY pnpm-lock.yaml pnpm-workspace.yaml package.json turbo.json ./

# Copy all package.json files for dependency resolution
COPY apps/dropzone/package.json ./apps/dropzone/
COPY packages/nucleus-api-client/package.json ./packages/nucleus-api-client/
COPY packages/shared/package.json ./packages/shared/
COPY packages/ui/package.json ./packages/ui/
COPY packages/typescript-config/package.json ./packages/typescript-config/
COPY packages/eslint-config/package.json ./packages/eslint-config/

# Install dependencies
RUN pnpm install --frozen-lockfile

# Copy source files
COPY . .

# Build with turbo (handles dependency ordering)
RUN pnpm turbo run build --filter=dropzone

# Production stage - serve static files
FROM nginx:alpine AS production

# Copy built assets
COPY --from=builder /app/apps/dropzone/dist /usr/share/nginx/html

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
